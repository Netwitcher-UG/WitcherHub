using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WitcherHub.Application.Common.CacheKeys;
using WitcherHub.Application.Common.Caching;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.Services.Lexware
{
    public class LexwareInvoiceSyncService
    {
        private readonly AppDbContext _db;
        private readonly LexwareClient _lex;
        private readonly LexwareOptions _opt;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<LexwareInvoiceSyncService> _logger;
        private readonly IAppCache _cache;

        public LexwareInvoiceSyncService(
            AppDbContext db,
            LexwareClient lex,
            IOptions<LexwareOptions> opt,
            IWebHostEnvironment env,
            ILogger<LexwareInvoiceSyncService> logger,
            IAppCache cache)
        {
            _db = db;
            _lex = lex;
            _opt = opt.Value;
            _env = env;
            _logger = logger;
            _cache = cache;
        }

        // =========================================
        // PUBLIC API
        // =========================================

        public async Task CreateOneTimeInvoiceFromContractAsync(Guid contractId, CancellationToken ct)
        {
            var contract = await LoadContractAsync(contractId, ct);
            var items = contract.Items
                .Where(x => x.BillingCycle == BillingCycle.OneTime)
                .OrderBy(x => x.Position)
                .ToList();

            if (items.Count == 0)
            {
                _logger.LogInformation("No one-time items found. ContractId={ContractId}", contractId);
                return;
            }

            var existing = await _db.Invoices.AnyAsync(x =>
                x.ContractId == contractId &&
                x.OriginType == InvoiceOriginType.ContractOneTime, ct);

            if (existing)
            {
                _logger.LogInformation("One-time invoice already exists. ContractId={ContractId}", contractId);
                return;
            }

            await CreateInvoiceInternalAsync(
                contract,
                items,
                invoiceDate: DateOnly.FromDateTime(DateTime.UtcNow),
                originType: InvoiceOriginType.ContractOneTime,
                recurringCycleKey: null,
                isRecurringInvoice: false,
                finalizeOnLexware: contract.InvoiceSendMode == InvoiceSendMode.Automatic,
                dispatchStatus: contract.InvoiceSendMode == InvoiceSendMode.Automatic
                    ? InvoiceDispatchStatus.SentAutomatically
                    : InvoiceDispatchStatus.PendingManualSend,
                ct: ct);
        }

        public async Task CreateRecurringInvoiceFromContractAsync(
            Guid contractId,
            DateOnly cycleDate,
            CancellationToken ct)
        {
            var contract = await LoadContractAsync(contractId, ct);

            if (!contract.RecurringEnabled || !contract.RecurringIsActive)
            {
                _logger.LogInformation("Recurring disabled/inactive. ContractId={ContractId}", contractId);
                return;
            }

            if (contract.RecurringEndDate.HasValue && cycleDate > contract.RecurringEndDate.Value)
            {
                _logger.LogInformation("Recurring cycle skipped because beyond end date. ContractId={ContractId} CycleDate={CycleDate}",
                    contractId, cycleDate);
                return;
            }

            var items = contract.Items
                .Where(x => x.BillingCycle != BillingCycle.OneTime)
                .Where(x => IsItemDueForCycle(x.BillingCycle, contract.RecurringStartDate ?? contract.StartDate ?? cycleDate, cycleDate))
                .OrderBy(x => x.Position)
                .ToList();

            if (items.Count == 0)
            {
                _logger.LogInformation("No recurring items due for cycle. ContractId={ContractId} CycleDate={CycleDate}",
                    contractId, cycleDate);

                contract.NextRecurringInvoiceDate = CalculateNextRecurringDate(cycleDate);
                contract.LastRecurringInvoiceRunAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
                return;
            }

            var cycleKey = $"{contract.Id:N}:{cycleDate:yyyy-MM-dd}";
            var exists = await _db.Invoices.AnyAsync(x =>
                x.ContractId == contractId &&
                x.RecurringCycleKey == cycleKey, ct);

            if (exists)
            {
                _logger.LogInformation("Recurring invoice already exists. ContractId={ContractId} CycleKey={CycleKey}",
                    contractId, cycleKey);
                return;
            }

            await CreateInvoiceInternalAsync(
                contract,
                items,
                invoiceDate: cycleDate,
                originType: InvoiceOriginType.ContractRecurring,
                recurringCycleKey: cycleKey,
                isRecurringInvoice: true,
                finalizeOnLexware: contract.InvoiceSendMode == InvoiceSendMode.Automatic,
                dispatchStatus: contract.InvoiceSendMode == InvoiceSendMode.Automatic
                    ? InvoiceDispatchStatus.SentAutomatically
                    : InvoiceDispatchStatus.PendingManualSend,
                ct: ct);

            contract.NextRecurringInvoiceDate = CalculateNextRecurringDate(cycleDate);
            contract.LastRecurringInvoiceRunAt = DateTimeOffset.UtcNow;

            if (contract.RecurringEndDate.HasValue && contract.NextRecurringInvoiceDate > contract.RecurringEndDate.Value)
            {
                contract.RecurringIsActive = false;
            }

            await _db.SaveChangesAsync(ct);
        }

        public async Task SendManualInvoiceAsync(Guid localInvoiceId, CancellationToken ct)
        {
            var invoice = await _db.Invoices
                .FirstOrDefaultAsync(x => x.Id == localInvoiceId, ct);

            if (invoice == null)
                throw new InvalidOperationException("Invoice not found.");

            if (invoice.DispatchStatus != InvoiceDispatchStatus.PendingManualSend)
                throw new InvalidOperationException("Invoice is not pending manual send.");

            if (string.IsNullOrWhiteSpace(invoice.LexwareInvoiceId))
                throw new InvalidOperationException("Invoice has no Lexware reference.");

            // IMPORTANT:
            // بدّل هذا السطر حسب اسم الميثود الموجودة داخل LexwareClient عندك
            // الهدف هنا: finalize / send draft invoice on Lexware
            await _lex.FinalizeInvoiceAsync(invoice.LexwareInvoiceId!, ct);

            await RefreshFromLexwareAsync(invoice, ct);

            invoice.DispatchStatus = InvoiceDispatchStatus.SentManually;
            invoice.SentAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync(ct);

            await InvalidateInvoiceCacheAsync(invoice.Id, ct);
        }

        // =========================================
        // INTERNAL CREATE
        // =========================================

        private async Task CreateInvoiceInternalAsync(
            Contract contract,
            List<ContractItem> sourceItems,
            DateOnly invoiceDate,
            InvoiceOriginType originType,
            string? recurringCycleKey,
            bool isRecurringInvoice,
            bool finalizeOnLexware,
            InvoiceDispatchStatus dispatchStatus,
            CancellationToken ct)
        {
            if (contract.Status != DocumentStatus.Signed)
                throw new InvalidOperationException("Contract is not signed yet.");

            var customer = contract.Project.Customer;

            var billing = customer.Addresses?
                .OrderByDescending(a => a.IsDefault)
                .FirstOrDefault();

            var street = (billing?.StreetRaw ?? "").Trim();
            var zip = (billing?.PostalCode ?? "").Trim();
            var city = (billing?.City ?? "").Trim();

            if (string.IsNullOrWhiteSpace(customer.Name) || string.IsNullOrWhiteSpace(city))
            {
                _logger.LogWarning("Lexware skipped: missing customer name/city. ContractId={ContractId}, CustomerId={CustomerId}",
                    contract.Id, customer.Id);
                return;
            }

            var currency = string.IsNullOrWhiteSpace(contract.Currency) ? "EUR" : contract.Currency;
            var invoiceDateUtc = new DateTimeOffset(invoiceDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

            var lexLineItems = sourceItems.Select(i => new LexwareInvoiceLineItem
            {
                Type = "custom",
                Name = string.IsNullOrWhiteSpace(i.Title) ? $"Position {i.Position}" : i.Title.Trim(),
                Quantity = i.Quantity <= 0 ? 1 : i.Quantity,
                UnitName = "Stück",
                UnitPrice = new LexwareUnitPrice
                {
                    Currency = currency,
                    NetAmount = ResolveNetAmount(i),
                    TaxRatePercentage = _opt.DefaultTaxRatePercentage
                },
                DiscountPercentage = ResolveDiscountPercent(i)
            }).ToList();

            var req = new LexwareInvoiceCreateRequest
            {
                Archived = false,
                VoucherDate = invoiceDateUtc,
                Address = new LexwareInvoiceAddress
                {
                    Name = customer.Name,
                    Street = street,
                    Zip = zip,
                    City = city,
                    CountryCode = _opt.DefaultCountryCode
                },
                LineItems = lexLineItems,
                TotalPrice = new LexwareTotalPrice { Currency = currency },
                TaxConditions = new LexwareTaxConditions { TaxType = "net" },
                PaymentConditions = new LexwarePaymentConditions
                {
                    PaymentTermLabel = _opt.DefaultPaymentTermLabel,
                    PaymentTermDuration = _opt.DefaultPaymentTermDays
                },
                ShippingConditions = new LexwareShippingConditions
                {
                    ShippingType = "service",
                    ShippingDate = invoiceDateUtc
                },
                Title = "Rechnung",
                Introduction = isRecurringInvoice
                    ? $"Recurring invoice for contract {contract.ContractNo}"
                    : $"Invoice for contract {contract.ContractNo}",
                Remark = isRecurringInvoice
                    ? $"ContractId={contract.Id};RecurringCycleKey={recurringCycleKey}"
                    : $"ContractId={contract.Id};OneTime=true"
            };

            _logger.LogInformation("Lexware create START. ContractId={ContractId} Recurring={Recurring} Finalize={Finalize}",
                contract.Id, isRecurringInvoice, finalizeOnLexware);

            var created = await _lex.CreateInvoiceAsync(req, finalize: finalizeOnLexware, ct);
            var invDoc = await _lex.GetInvoiceAsync(created.Id, ct);

            var localInvoice = new Invoice
            {
                ProjectId = contract.ProjectId,
                ContractId = contract.Id,
                Currency = currency,
                ApplyVat = contract.ApplyVat,
                OriginType = originType,
                IsRecurringInvoice = isRecurringInvoice,
                RecurringCycleDate = isRecurringInvoice ? invoiceDate : null,
                RecurringCycleKey = recurringCycleKey,
                DispatchStatus = dispatchStatus,
                SentAt = dispatchStatus == InvoiceDispatchStatus.SentAutomatically ? DateTimeOffset.UtcNow : null,
                Notes = isRecurringInvoice
                    ? $"Generated recurring invoice from contract {contract.ContractNo}"
                    : $"Generated one-time invoice from contract {contract.ContractNo}",
                LexwareInvoiceId = created.Id,
                LexwareResourceUri = created.ResourceUri,
                LexwareVersion = created.Version
            };

            ApplyLexwareSnapshotToLocalInvoice(
                localInvoice,
                invDoc,
                fallbackCurrency: currency,
                fallbackIssueAt: invoiceDateUtc,
                defaultPaymentTermDays: _opt.DefaultPaymentTermDays,
                contractItemsForServiceMapping: sourceItems);

            if (string.IsNullOrWhiteSpace(localInvoice.InvoiceNo))
            {
                localInvoice.InvoiceNo =
                    localInvoice.LexwareVoucherNumber ??
                    localInvoice.LexwareInvoiceId ??
                    $"TEMP-{Guid.NewGuid():N}".Substring(0, 20);
            }

            _db.Invoices.Add(localInvoice);
            await _db.SaveChangesAsync(ct);

            await EnsurePdfAsync(localInvoice, ct);
            await InvalidateInvoiceCacheAsync(localInvoice.Id, ct);

            _logger.LogInformation("Lexware invoice created. ContractId={ContractId} LocalId={LocalId} LexId={LexId}",
                contract.Id, localInvoice.Id, localInvoice.LexwareInvoiceId);
        }

        private async Task<Contract> LoadContractAsync(Guid contractId, CancellationToken ct)
        {
            var contract = await _db.Contracts
                .Include(c => c.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(cu => cu.Addresses)
                .Include(c => c.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(cu => cu.EmailAddresses)
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.Id == contractId, ct);

            if (contract == null)
                throw new InvalidOperationException("Contract not found.");

            return contract;
        }

        private static decimal ResolveNetAmount(ContractItem item)
        {
            var unit = item.AgreedPrice ?? item.UnitPrice;
            if (unit < 0) unit = 0m;
            return unit;
        }

        private static decimal ResolveDiscountPercent(ContractItem item)
        {
            if (item.DiscountType == DiscountType.Percent && item.DiscountValue.HasValue && item.DiscountValue.Value > 0)
                return item.DiscountValue.Value;

            return 0m;
        }

        private static bool IsItemDueForCycle(BillingCycle itemCycle, DateOnly recurringStartDate, DateOnly currentCycleDate)
        {
            if (itemCycle == BillingCycle.OneTime)
                return false;

            if (currentCycleDate < recurringStartDate)
                return false;

            var months = ((currentCycleDate.Year - recurringStartDate.Year) * 12) + (currentCycleDate.Month - recurringStartDate.Month);
            if (months < 0) return false;

            return itemCycle switch
            {
                BillingCycle.Monthly => true,
                BillingCycle.Quarterly => months % 3 == 0,
                BillingCycle.SemiAnnual => months % 6 == 0,
                BillingCycle.Annual => months % 12 == 0,
                _ => false
            };
        }

        private static DateOnly CalculateNextRecurringDate(DateOnly currentDate)
            => currentDate.AddMonths(1);

        // =========================
        // Refresh existing
        // =========================
        private async Task RefreshFromLexwareAsync(Invoice invoice, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(invoice.LexwareInvoiceId))
                return;

            var nowUtc = DateTimeOffset.UtcNow;
            var invDoc = await _lex.GetInvoiceAsync(invoice.LexwareInvoiceId!, ct);

            ApplyLexwareSnapshotToLocalInvoice(
                invoice,
                invDoc,
                fallbackCurrency: invoice.Currency ?? "EUR",
                fallbackIssueAt: invoice.IssuedAt ?? nowUtc,
                defaultPaymentTermDays: _opt.DefaultPaymentTermDays,
                contractItemsForServiceMapping: null);

            invoice.LexwareSyncedAt = nowUtc;

            if (string.IsNullOrWhiteSpace(invoice.InvoiceNo))
            {
                invoice.InvoiceNo =
                    invoice.LexwareVoucherNumber ??
                    invoice.LexwareInvoiceId ??
                    $"TEMP-{Guid.NewGuid():N}".Substring(0, 20);
            }

            await _db.SaveChangesAsync(ct);

            await EnsurePdfAsync(invoice, ct);
            await InvalidateInvoiceCacheAsync(invoice.Id, ct);

            _logger.LogInformation("Lexware invoice refreshed. LocalId={LocalId} LexId={LexId} Status={Status} Total={Total}",
                invoice.Id, invoice.LexwareInvoiceId, invoice.LexwareVoucherStatus, invoice.Totals?.Total);
        }

        // =========================
        // Apply snapshot -> local
        // =========================
        private void ApplyLexwareSnapshotToLocalInvoice(
            Invoice invoice,
            JsonDocument invDoc,
            string fallbackCurrency,
            DateTimeOffset fallbackIssueAt,
            int defaultPaymentTermDays,
            List<ContractItem>? contractItemsForServiceMapping)
        {
            var root = invDoc.RootElement;

            var voucherNumber = TryGetString(root, "voucherNumber");
            var voucherStatus = TryGetString(root, "voucherStatus");
            var voucherDateUtc = ToUtc(TryGetDateTimeOffset(root, "voucherDate") ?? fallbackIssueAt);

            invoice.LexwareSnapshot = CloneJson(invDoc);
            invoice.LexwareSyncedAt = DateTimeOffset.UtcNow;

            invoice.LexwareVoucherNumber = voucherNumber;
            invoice.LexwareVoucherStatus = voucherStatus;

            if (!string.IsNullOrWhiteSpace(voucherNumber))
                invoice.InvoiceNo = voucherNumber!;

            invoice.Currency = string.IsNullOrWhiteSpace(invoice.Currency) ? fallbackCurrency : invoice.Currency;
            invoice.IssuedAt = voucherDateUtc;
            invoice.IssueDate = DateOnly.FromDateTime(voucherDateUtc.UtcDateTime);

            var dueDate = TryGetDateOnly(root, "dueDate");
            if (dueDate is null)
            {
                var termDays = TryGetInt(root, "paymentConditions", "paymentTermDuration") ?? defaultPaymentTermDays;
                dueDate = invoice.IssueDate?.AddDays(termDays);
            }
            invoice.DueDate = dueDate;

            invoice.Status = MapVoucherStatusToDocumentStatus(voucherStatus);

            if (invoice.Status == DocumentStatus.Paid && invoice.PaidAt is null)
            {
                invoice.PaidAt = DateTimeOffset.UtcNow;
            }

            if (invoice.Items != null && invoice.Items.Count > 0)
            {
                _db.InvoiceItems.RemoveRange(invoice.Items);
                invoice.Items.Clear();
            }
            else if (invoice.Items == null)
            {
                invoice.Items = new List<InvoiceItem>();
            }

            var newItems = BuildInvoiceItemsFromLexware(root, contractItemsForServiceMapping);

            foreach (var it in newItems)
            {
                it.Invoice = invoice;
                invoice.Items.Add(it);
            }

            var totals = BuildTotalsFromLexware(root, invoice.Items, voucherStatus, _opt.DefaultTaxRatePercentage);
            totals.Invoice = invoice;

            if (invoice.Totals == null)
            {
                invoice.Totals = totals;
            }
            else
            {
                invoice.Totals.Subtotal = totals.Subtotal;
                invoice.Totals.DiscountTotal = totals.DiscountTotal;
                invoice.Totals.TaxTotal = totals.TaxTotal;
                invoice.Totals.Total = totals.Total;
                invoice.Totals.PaidTotal = totals.PaidTotal;
                invoice.Totals.BalanceDue = totals.BalanceDue;
                invoice.Totals.UpdatedAt = totals.UpdatedAt;
            }
        }

        private List<InvoiceItem> BuildInvoiceItemsFromLexware(JsonElement root, List<ContractItem>? contractItems)
        {
            var list = new List<InvoiceItem>();

            if (root.TryGetProperty("lineItems", out var li) && li.ValueKind == JsonValueKind.Array)
            {
                int pos = 1;
                foreach (var x in li.EnumerateArray())
                {
                    var name = TryGetString(x, "name") ?? $"Position {pos}";
                    var qty = TryGetDecimal(x, "quantity") ?? 1m;

                    decimal unitNet =
                        TryGetDecimal(x, "unitPrice", "netAmount")
                        ?? TryGetDecimal(x, "unitPrice", "grossAmount")
                        ?? 0m;

                    var discountPct = TryGetDecimal(x, "discountPercentage");

                    Guid? serviceId = null;
                    JsonDocument config = JsonDocument.Parse("{}");
                    BillingCycle cycle = BillingCycle.OneTime;
                    DiscountType? discountType = null;
                    decimal? discountValue = null;

                    if (contractItems != null && contractItems.Count >= pos)
                    {
                        var ci = contractItems.OrderBy(a => a.Position).ElementAt(pos - 1);
                        serviceId = ci.ServiceId;
                        config = ci.Config ?? JsonDocument.Parse("{}");
                        cycle = ci.BillingCycle;
                        discountType = ci.DiscountType;
                        discountValue = ci.DiscountValue;
                    }

                    var item = new InvoiceItem
                    {
                        Title = name,
                        Quantity = qty,
                        UnitPrice = unitNet,
                        Position = pos,
                        ServiceId = serviceId,
                        Config = config,
                        BillingCycle = cycle,
                        DiscountType = discountType,
                        DiscountValue = discountValue
                    };

                    if (discountPct.HasValue && discountPct.Value > 0 && item.DiscountType == null)
                    {
                        item.DiscountType = DiscountType.Percent;
                        item.DiscountValue = discountPct.Value;
                    }

                    list.Add(item);
                    pos++;
                }

                return list;
            }

            if (contractItems != null && contractItems.Count > 0)
            {
                foreach (var ci in contractItems.OrderBy(x => x.Position))
                {
                    list.Add(new InvoiceItem
                    {
                        Title = ci.Title,
                        Quantity = ci.Quantity <= 0 ? 1m : ci.Quantity,
                        UnitPrice = ResolveNetAmount(ci),
                        Position = ci.Position,
                        ServiceId = ci.ServiceId,
                        Config = ci.Config ?? JsonDocument.Parse("{}"),
                        BillingCycle = ci.BillingCycle,
                        DiscountType = ci.DiscountType,
                        DiscountValue = ci.DiscountValue
                    });
                }
            }

            return list;
        }

        private InvoiceTotal BuildTotalsFromLexware(
            JsonElement root,
            ICollection<InvoiceItem> items,
            string? voucherStatus,
            decimal defaultTaxRatePercent)
        {
            decimal subtotal = TryGetDecimal(root, "totalPrice", "totalNetAmount")
                              ?? items.Sum(i => i.Quantity * i.UnitPrice);

            decimal tax = TryGetDecimal(root, "totalPrice", "totalTaxAmount")
                         ?? Math.Round(subtotal * (defaultTaxRatePercent / 100m), 2);

            decimal total = TryGetDecimal(root, "totalPrice", "totalGrossAmount")
                           ?? (subtotal + tax);

            decimal balance =
                (voucherStatus ?? "").Equals("paid", StringComparison.OrdinalIgnoreCase)
                    ? 0m
                    : (TryGetDecimal(root, "openAmount")
                       ?? TryGetDecimal(root, "openGrossAmount")
                       ?? total);

            var paid = Math.Max(0m, total - balance);

            return new InvoiceTotal
            {
                Subtotal = subtotal,
                DiscountTotal = 0m,
                TaxTotal = tax,
                Total = total,
                PaidTotal = paid,
                BalanceDue = balance,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }

        private async Task EnsurePdfAsync(Invoice invoice, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(invoice.LexwareInvoiceId))
                return;

            var dir = Path.Combine(_env.ContentRootPath, "App_Data", "LexwareInvoices");
            Directory.CreateDirectory(dir);

            var existingFullPath = ResolveStoredPdfPath(invoice.LexwarePdfPath);
            if (!string.IsNullOrWhiteSpace(existingFullPath) && File.Exists(existingFullPath))
                return;

            try
            {
                var pdf = await _lex.DownloadInvoiceFileAsync(invoice.LexwareInvoiceId!, "application/pdf", ct);

                var safeName = (invoice.InvoiceNo ?? invoice.LexwareInvoiceId!)
                    .Replace("/", "_")
                    .Replace("\\", "_")
                    .Trim();

                var fileName = $"{safeName}.pdf";
                var filePath = Path.Combine(dir, fileName);

                await File.WriteAllBytesAsync(filePath, pdf, ct);

                invoice.LexwarePdfPath = fileName;
                invoice.LexwareSyncedAt = DateTimeOffset.UtcNow;

                await _db.SaveChangesAsync(ct);

                _logger.LogInformation("Lexware PDF saved. LocalId={LocalId} LexId={LexId} FileName={FileName} FullPath={FullPath}",
                    invoice.Id, invoice.LexwareInvoiceId, fileName, filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lexware PDF download/save failed. LocalId={LocalId} LexId={LexId}",
                    invoice.Id, invoice.LexwareInvoiceId);
            }
        }

        private string? ResolveStoredPdfPath(string? storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath))
                return null;

            var baseDir = Path.Combine(_env.ContentRootPath, "App_Data", "LexwareInvoices");

            // إذا كانت القيمة القديمة مسارًا كاملاً
            if (Path.IsPathRooted(storedPath))
                return Path.GetFullPath(storedPath);

            // إذا كانت القيمة الجديدة مجرد اسم ملف أو مسار نسبي
            return Path.GetFullPath(Path.Combine(baseDir, storedPath));
        }
        private async Task InvalidateInvoiceCacheAsync(Guid invoiceId, CancellationToken ct)
        {
            await _cache.RemoveAsync(InvoiceCacheKeys.Details(invoiceId), ct);
            await _cache.BumpVersionAsync(InvoiceCacheKeys.ListVersionKey, ct);
        }

        private static DateTimeOffset ToUtc(DateTimeOffset value)
            => value.Offset == TimeSpan.Zero ? value : value.ToUniversalTime();

        private static JsonDocument CloneJson(JsonDocument doc)
            => JsonDocument.Parse(doc.RootElement.GetRawText());

        private static DocumentStatus MapVoucherStatusToDocumentStatus(string? s)
        {
            var v = (s ?? "").Trim().ToLowerInvariant();
            return v switch
            {
                "draft" => DocumentStatus.Draft,
                "open" => DocumentStatus.Issued,
                "paid" => DocumentStatus.Paid,
                "voided" => DocumentStatus.Void,
                "void" => DocumentStatus.Void,
                _ => DocumentStatus.Issued
            };
        }

        private static string? TryGetString(JsonElement root, params string[] path)
        {
            if (!TryGetElement(root, out var el, path)) return null;
            return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
        }

        private static int? TryGetInt(JsonElement root, params string[] path)
        {
            if (!TryGetElement(root, out var el, path)) return null;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i)) return i;
            if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var j)) return j;
            return null;
        }

        private static decimal? TryGetDecimal(JsonElement root, params string[] path)
        {
            if (!TryGetElement(root, out var el, path)) return null;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d)) return d;
            if (el.ValueKind == JsonValueKind.String &&
                decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var x))
                return x;
            return null;
        }

        private static DateTimeOffset? TryGetDateTimeOffset(JsonElement root, params string[] path)
        {
            if (!TryGetElement(root, out var el, path)) return null;
            if (el.ValueKind != JsonValueKind.String) return null;

            if (DateTimeOffset.TryParse(el.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                return ToUtc(dto);

            return null;
        }

        private static DateOnly? TryGetDateOnly(JsonElement root, params string[] path)
        {
            if (!TryGetElement(root, out var el, path)) return null;

            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    return d;

                if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                    return DateOnly.FromDateTime(ToUtc(dto).UtcDateTime);
            }

            return null;
        }

        private static bool TryGetElement(JsonElement root, out JsonElement el, params string[] path)
        {
            el = root;
            foreach (var p in path)
            {
                if (el.ValueKind != JsonValueKind.Object) return false;
                if (!el.TryGetProperty(p, out var next)) return false;
                el = next;
            }
            return true;
        }
    }
}
