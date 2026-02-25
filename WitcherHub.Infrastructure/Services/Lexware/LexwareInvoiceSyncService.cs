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

        public async Task CreateFromContractAsync(Guid contractId, CancellationToken ct)
        {
            // ✅ إذا في Invoice محلي مرتبط بالعقد وعنده LexwareInvoiceId => اعمل refresh (مش return)
            var existingLocal = await _db.Invoices
                .Include(x => x.Items)
                .Include(x => x.Totals)
                .FirstOrDefaultAsync(x => x.ContractId == contractId && !string.IsNullOrWhiteSpace(x.LexwareInvoiceId), ct);

            if (existingLocal != null)
            {
                _logger.LogInformation("Lexware sync: invoice exists locally -> REFRESH. ContractId={ContractId} LexId={LexId}",
                    contractId, existingLocal.LexwareInvoiceId);

                await RefreshFromLexwareAsync(existingLocal, ct);
                return;
            }

            // ====== CREATE ON LEXWARE ======
            var contract = await _db.Contracts
                .Include(c => c.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(cu => cu.Addresses)
                .Include(c => c.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(cu => cu.EmailAddresses)
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.Id == contractId, ct);

            if (contract == null) throw new InvalidOperationException("Contract not found.");
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
                    contractId, customer.Id);
                return;
            }

            var nowUtc = DateTimeOffset.UtcNow; // ✅ ALWAYS UTC
            var currency = string.IsNullOrWhiteSpace(contract.Currency) ? "EUR" : contract.Currency;

            var contractItems = (contract.Items ?? new List<ContractItem>())
                .OrderBy(i => i.Position)
                .ToList();

            _logger.LogInformation("Lexware: building invoice from contract. ContractId={ContractId} ItemsCount={Count}",
                contractId, contractItems.Count);

            if (contractItems.Count == 0)
            {
                _logger.LogWarning("Lexware skipped: contract has NO items. ContractId={ContractId}", contractId);
                return;
            }

            // ✅ LineItems to Lexware
            var lexLineItems = contractItems.Select(i => new LexwareInvoiceLineItem
            {
                Type = "custom",
                Name = string.IsNullOrWhiteSpace(i.Title) ? $"Position {i.Position}" : i.Title.Trim(),
                Quantity = 1,
                UnitName = "Stück",
                UnitPrice = new LexwareUnitPrice
                {
                    Currency = currency,
                    NetAmount = i.AgreedPrice ?? 0m,
                    TaxRatePercentage = _opt.DefaultTaxRatePercentage
                },
                DiscountPercentage = 0
            }).ToList();

            var req = new LexwareInvoiceCreateRequest
            {
                Archived = false,
                VoucherDate = nowUtc, // ✅ UTC
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
                    ShippingDate = nowUtc // ✅ UTC
                },
                Title = "Rechnung",
                Introduction = $"Invoice for contract {contract.ContractNo}",
                Remark = $"ContractId={contract.Id}"
            };

            _logger.LogInformation("Lexware: CreateInvoice START. ContractId={ContractId}", contractId);
            var created = await _lex.CreateInvoiceAsync(req, finalize: true, ct);
            _logger.LogInformation("Lexware: CreateInvoice OK. ContractId={ContractId} LexId={LexId}", contractId, created.Id);

            var invDoc = await _lex.GetInvoiceAsync(created.Id, ct);

            // ====== UPSERT LOCAL INVOICE + ITEMS + TOTALS + STATUS ======
            var localInvoice = new Invoice
            {
                ProjectId = contract.ProjectId,
                ContractId = contract.Id,
                Currency = currency,
                Notes = $"Generated by Lexware from contract {contract.ContractNo}",

                LexwareInvoiceId = created.Id,
                LexwareResourceUri = created.ResourceUri,
                LexwareVersion = created.Version
            };

            ApplyLexwareSnapshotToLocalInvoice(
                localInvoice,
                invDoc,
                fallbackCurrency: currency,
                fallbackIssueAt: nowUtc,
                defaultPaymentTermDays: _opt.DefaultPaymentTermDays,
                contractItemsForServiceMapping: contractItems);

            // ✅ InvoiceNo is NOT NULL in DB, ensure fallback
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

            _logger.LogInformation("Lexware invoice created & stored locally. ContractId={ContractId} LocalId={LocalId} LexId={LexId} No={No}",
                contractId, localInvoice.Id, localInvoice.LexwareInvoiceId, localInvoice.InvoiceNo);
        }

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

            invoice.LexwareSyncedAt = nowUtc; // ✅ UTC

            // ✅ InvoiceNo NOT NULL safety
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

            // ✅ ALWAYS normalize to UTC before saving to DB
            var voucherDateUtc = ToUtc(TryGetDateTimeOffset(root, "voucherDate") ?? fallbackIssueAt);

            // update basic meta (store a safe clone)
            invoice.LexwareSnapshot = CloneJson(invDoc);
            invoice.LexwareSyncedAt = DateTimeOffset.UtcNow; // ✅ UTC

            invoice.LexwareVoucherNumber = voucherNumber;
            invoice.LexwareVoucherStatus = voucherStatus;

            if (!string.IsNullOrWhiteSpace(voucherNumber))
                invoice.InvoiceNo = voucherNumber!;

            invoice.Currency = string.IsNullOrWhiteSpace(invoice.Currency) ? fallbackCurrency : invoice.Currency;

            // ✅ IssuedAt must be UTC for PostgreSQL timestamptz
            invoice.IssuedAt = voucherDateUtc;
            invoice.IssueDate = DateOnly.FromDateTime(voucherDateUtc.UtcDateTime);

            // DueDate: try from Lexware, else compute from payment term
            var dueDate = TryGetDateOnly(root, "dueDate");
            if (dueDate is null)
            {
                var termDays = TryGetInt(root, "paymentConditions", "paymentTermDuration") ?? defaultPaymentTermDays;
                dueDate = invoice.IssueDate?.AddDays(termDays);
            }
            invoice.DueDate = dueDate;

            // Status mapping (Lexware -> our DocumentStatus)
            invoice.Status = MapVoucherStatusToDocumentStatus(voucherStatus);

            if (invoice.Status == DocumentStatus.Paid && invoice.PaidAt is null)
            {
                // best-effort
                invoice.PaidAt = DateTimeOffset.UtcNow; // ✅ UTC
            }

            // ---------- Items: remove then rebuild ----------
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
                it.Invoice = invoice; // navigation only
                invoice.Items.Add(it);
            }

            // ---------- Totals ----------
            var totals = BuildTotalsFromLexware(root, invoice.Items, voucherStatus, _opt.DefaultTaxRatePercentage);

            // ✅ one-to-one: set navigation only (cleaner)
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

            // Prefer Lexware lineItems (most accurate)
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

                    // Try map to contract item serviceId by position if provided
                    Guid? serviceId = null;
                    JsonDocument config = JsonDocument.Parse("{}");

                    if (contractItems != null && contractItems.Count >= pos)
                    {
                        var ci = contractItems.OrderBy(a => a.Position).ElementAt(pos - 1);
                        serviceId = ci.ServiceId;
                        config = ci.Config ?? JsonDocument.Parse("{}");
                    }

                    var item = new InvoiceItem
                    {
                        Title = name,
                        Quantity = qty,
                        UnitPrice = unitNet,
                        Position = pos,
                        ServiceId = serviceId,
                        Config = config
                    };

                    if (discountPct.HasValue && discountPct.Value > 0)
                    {
                        item.DiscountType = DiscountType.Percent;
                        item.DiscountValue = discountPct.Value;
                    }

                    list.Add(item);
                    pos++;
                }

                return list;
            }

            // Fallback: contract items
            if (contractItems != null && contractItems.Count > 0)
            {
                foreach (var ci in contractItems.OrderBy(x => x.Position))
                {
                    list.Add(new InvoiceItem
                    {
                        Title = ci.Title,
                        Quantity = 1m,
                        UnitPrice = ci.AgreedPrice ?? 0m,
                        Position = ci.Position,
                        ServiceId = ci.ServiceId,
                        Config = ci.Config ?? JsonDocument.Parse("{}")
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
                UpdatedAt = DateTimeOffset.UtcNow // ✅ UTC
            };
        }

        private async Task EnsurePdfAsync(Invoice invoice, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(invoice.LexwareInvoiceId))
                return;

            var path = invoice.LexwarePdfPath;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                return;

            try
            {
                var pdf = await _lex.DownloadInvoiceFileAsync(invoice.LexwareInvoiceId!, "application/pdf", ct);

                var dir = Path.Combine(_env.ContentRootPath, "App_Data", "LexwareInvoices");
                Directory.CreateDirectory(dir);

                var safeName = (invoice.InvoiceNo ?? invoice.LexwareInvoiceId!).Replace("/", "_");
                var filePath = Path.Combine(dir, $"{safeName}.pdf");

                await File.WriteAllBytesAsync(filePath, pdf, ct);

                invoice.LexwarePdfPath = filePath;
                invoice.LexwareSyncedAt = DateTimeOffset.UtcNow; // ✅ UTC

                await _db.SaveChangesAsync(ct);

                _logger.LogInformation("Lexware PDF saved. LocalId={LocalId} LexId={LexId} Path={Path}",
                    invoice.Id, invoice.LexwareInvoiceId, filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lexware PDF download/save failed. LocalId={LocalId} LexId={LexId}",
                    invoice.Id, invoice.LexwareInvoiceId);
            }
        }

        private async Task InvalidateInvoiceCacheAsync(Guid invoiceId, CancellationToken ct)
        {
            await _cache.RemoveAsync(InvoiceCacheKeys.Details(invoiceId), ct);
            await _cache.BumpVersionAsync(InvoiceCacheKeys.ListVersionKey, ct);
        }

        // =========================
        // Helpers (JSON + mapping + UTC)
        // =========================

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

        // ✅ ALWAYS returns UTC (offset 0) if parsed successfully
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
