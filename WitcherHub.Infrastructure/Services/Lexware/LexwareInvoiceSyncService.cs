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
using WitcherHub.Application.Models.DTO.Invoices;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.Services.Invoices;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.Services.Lexware
{
    public sealed class InvoiceGenerationResult
    {
        public bool Created { get; init; }
        public string Message { get; init; } = "";

        public static InvoiceGenerationResult Success(string message)
            => new() { Created = true, Message = message };

        public static InvoiceGenerationResult Warning(string message)
            => new() { Created = false, Message = message };
    }

    public class LexwareInvoiceSyncService
    {
        private const decimal FixedTaxRatePercentage = 19m;

        private readonly AppDbContext _db;
        private readonly LexwareClient _lex;
        private readonly LexwareOptions _opt;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<LexwareInvoiceSyncService> _logger;
        private readonly IAppCache _cache;
        private readonly IInvoiceNotificationService _invoiceNotificationService;

        public LexwareInvoiceSyncService(
            AppDbContext db,
            LexwareClient lex,
            IOptions<LexwareOptions> opt,
            IWebHostEnvironment env,
            ILogger<LexwareInvoiceSyncService> logger,
            IAppCache cache,
            IInvoiceNotificationService invoiceNotificationService)
        {
            _db = db;
            _lex = lex;
            _opt = opt.Value;
            _env = env;
            _logger = logger;
            _cache = cache;
            _invoiceNotificationService = invoiceNotificationService;
        }

        private sealed class InvoiceSourceContext
        {
            public Guid ProjectId { get; init; }
            public Guid? ContractId { get; init; }
            public Guid? QuoteId { get; init; }
            public Customer Customer { get; init; } = default!;
            public string DocumentNumber { get; init; } = string.Empty;
            public string DocumentLabel { get; init; } = string.Empty; // contract / quote
            public DocumentStatus Status { get; init; }
            public InvoiceSendMode InvoiceSendMode { get; init; }
            public bool ApplyVat { get; init; } = true;
            public string? Currency { get; init; }

            public Guid SourceId => ContractId ?? QuoteId ?? Guid.Empty;

            public static InvoiceSourceContext FromContract(Contract contract) => new()
            {
                ProjectId = contract.ProjectId,
                ContractId = contract.Id,
                QuoteId = null,
                Customer = contract.Project.Customer,
                DocumentNumber = contract.ContractNo,
                DocumentLabel = "contract",
                Status = contract.Status,
                InvoiceSendMode = contract.InvoiceSendMode,
                ApplyVat = true,
                Currency = contract.Currency
            };

            public static InvoiceSourceContext FromQuote(Quote quote) => new()
            {
                ProjectId = quote.ProjectId,
                ContractId = null,
                QuoteId = quote.Id,
                Customer = quote.Project.Customer,
                DocumentNumber = quote.QuoteNo,
                DocumentLabel = "quote",
                Status = quote.Status,
                InvoiceSendMode = quote.InvoiceSendMode,
                ApplyVat = quote.ApplyVat,
                Currency = quote.Currency
            };
        }

        private sealed class InvoiceSourceItemData
        {
            public Guid? ServiceId { get; init; }
            public string Title { get; init; } = string.Empty;
            public string? Description { get; init; }
            public decimal Quantity { get; init; }
            public decimal UnitPrice { get; init; }
            public int Position { get; init; }
            public JsonDocument Config { get; init; } = JsonDocument.Parse("{}");
            public BillingCycle BillingCycle { get; init; } = BillingCycle.OneTime;
            public DiscountType? DiscountType { get; init; }
            public decimal? DiscountValue { get; init; }
            public ServiceUnitType UnitType { get; init; } = ServiceUnitType.Custom;
            public string UnitName { get; init; } = string.Empty;
            public string? ServiceDefaultCurrency { get; init; }
        }

        public async Task<InvoiceGenerationResult> CreateOneTimeInvoiceFromContractAsync(
            Guid contractId,
            CancellationToken ct)
        {
            var contract = await LoadContractAsync(contractId, ct);

            var items = contract.Items
                .Where(x => x.BillingCycle == BillingCycle.OneTime)
                .OrderBy(x => x.Position)
                .ToList();

            if (items.Count == 0)
            {
                _logger.LogInformation("No one-time items found. ContractId={ContractId}", contractId);
                return InvoiceGenerationResult.Warning("No one-time items found for this contract.");
            }

            var existing = await _db.Invoices.AnyAsync(
                x => x.ContractId == contractId &&
                     x.OriginType == InvoiceOriginType.ContractOneTime,
                ct);

            if (existing)
            {
                _logger.LogInformation("One-time invoice already exists. ContractId={ContractId}", contractId);
                return InvoiceGenerationResult.Warning("A one-time invoice already exists for this contract.");
            }
            var invoiceDate = DateOnly.FromDateTime(DateTime.UtcNow);

            return await CreateInvoiceInternalAsync(
                InvoiceSourceContext.FromContract(contract),
                items.Select(MapSourceItem).ToList(),
                invoiceDate: invoiceDate,
                originType: InvoiceOriginType.ContractOneTime,
                recurringCycleKey: null,
                isRecurringInvoice: false,
                servicePeriodStart: contract.StartDate ?? invoiceDate,
                servicePeriodEnd: contract.EndDate ?? contract.RecurringEndDate,
                ct: ct);
        }

        public async Task<InvoiceGenerationResult> CreateOneTimeInvoiceFromQuoteAsync(
            Guid quoteId,
            CancellationToken ct)
        {
            var quote = await LoadQuoteAsync(quoteId, ct);

            if (quote.Status != DocumentStatus.Signed)
            {
                var latestSignature = await _db.QuoteSignatures
                    .AsNoTracking()
                    .Where(x => x.QuoteId == quoteId && x.SignedAt != null)
                    .OrderByDescending(x => x.SignedAt)
                    .FirstOrDefaultAsync(ct);

                if (latestSignature != null)
                {
                    quote.Status = DocumentStatus.Signed;
                    if (!quote.SignedAt.HasValue)
                        quote.SignedAt = latestSignature.SignedAt;

                    await _db.SaveChangesAsync(ct);
                }
            }

            var items = quote.Items
                .Where(x => x.BillingCycle == BillingCycle.OneTime)
                .OrderBy(x => x.Position)
                .ToList();

            if (items.Count == 0)
            {
                _logger.LogInformation("No one-time items found. QuoteId={QuoteId}", quoteId);
                return InvoiceGenerationResult.Warning("No one-time items found for this quote.");
            }

            var existing = await _db.Invoices.AnyAsync(
                x => x.QuoteId == quoteId &&
                     x.OriginType == InvoiceOriginType.QuoteOneTime,
                ct);

            if (existing)
            {
                _logger.LogInformation("One-time invoice already exists. QuoteId={QuoteId}", quoteId);
                return InvoiceGenerationResult.Warning("A one-time invoice already exists for this quote.");
            }

            var invoiceDate = DateOnly.FromDateTime(DateTime.UtcNow);

            var quoteServiceStart =
                ToDateOnly(quote.IssuedAt) ??
                ToDateOnly(quote.SignedAt) ??
                invoiceDate;

            return await CreateInvoiceInternalAsync(
                InvoiceSourceContext.FromQuote(quote),
                items.Select(MapSourceItem).ToList(),
                invoiceDate: invoiceDate,
                originType: InvoiceOriginType.QuoteOneTime,
                recurringCycleKey: null,
                isRecurringInvoice: false,
                servicePeriodStart: quoteServiceStart,
                servicePeriodEnd: ToDateOnly(quote.ExpiresAt) ?? quote.RecurringEndDate,
                ct: ct);
        }

        public async Task<InvoiceGenerationResult> CreateRecurringInvoiceFromContractAsync(
            Guid contractId,
            DateOnly cycleDate,
            CancellationToken ct)
        {
            var contract = await LoadContractAsync(contractId, ct);

            if (!contract.RecurringEnabled || !contract.RecurringIsActive)
            {
                _logger.LogInformation("Recurring disabled/inactive. ContractId={ContractId}", contractId);
                return InvoiceGenerationResult.Warning("Recurring billing is disabled or inactive for this contract.");
            }

            if (contract.RecurringEndDate.HasValue && cycleDate > contract.RecurringEndDate.Value)
            {
                _logger.LogInformation(
                    "Recurring cycle skipped because beyond end date. ContractId={ContractId} CycleDate={CycleDate}",
                    contractId,
                    cycleDate);

                return InvoiceGenerationResult.Warning(
                    $"Recurring billing already ended before cycle {cycleDate:yyyy-MM-dd}.");
            }

            var items = contract.Items
                .Where(x => x.BillingCycle != BillingCycle.OneTime)
                .Where(x => IsItemDueForCycle(
                    x.BillingCycle,
                    contract.RecurringStartDate ?? contract.StartDate ?? cycleDate,
                    cycleDate))
                .OrderBy(x => x.Position)
                .ToList();

            if (items.Count == 0)
            {
                _logger.LogInformation(
                    "No recurring items due for cycle. ContractId={ContractId} CycleDate={CycleDate}",
                    contractId,
                    cycleDate);

                contract.NextRecurringInvoiceDate = CalculateNextRecurringDate(cycleDate);
                contract.LastRecurringInvoiceRunAt = DateTimeOffset.UtcNow;

                await _db.SaveChangesAsync(ct);

                return InvoiceGenerationResult.Warning(
                    $"No recurring items are due for cycle {cycleDate:yyyy-MM-dd}.");
            }

            var cycleKey = $"{contract.Id:N}:{cycleDate:yyyy-MM-dd}";

            var exists = await _db.Invoices.AnyAsync(
                x => x.ContractId == contractId &&
                     x.RecurringCycleKey == cycleKey,
                ct);

            if (exists)
            {
                _logger.LogInformation(
                    "Recurring invoice already exists. ContractId={ContractId} CycleKey={CycleKey}",
                    contractId,
                    cycleKey);

                return InvoiceGenerationResult.Warning(
                    $"A recurring invoice already exists for cycle {cycleDate:yyyy-MM-dd}.");
            }

            var result = await CreateInvoiceInternalAsync(
     InvoiceSourceContext.FromContract(contract),
     items.Select(MapSourceItem).ToList(),
     invoiceDate: cycleDate,
     originType: InvoiceOriginType.ContractRecurring,
     recurringCycleKey: cycleKey,
     isRecurringInvoice: true,
     servicePeriodStart: cycleDate,
     servicePeriodEnd: contract.RecurringEndDate ?? contract.EndDate,
     ct: ct);

            if (!result.Created)
                return result;

            contract.NextRecurringInvoiceDate = CalculateNextRecurringDate(cycleDate);
            contract.LastRecurringInvoiceRunAt = DateTimeOffset.UtcNow;

            if (contract.RecurringEndDate.HasValue &&
                contract.NextRecurringInvoiceDate > contract.RecurringEndDate.Value)
            {
                contract.RecurringIsActive = false;
            }

            await _db.SaveChangesAsync(ct);

            return result;
        }

        public async Task<InvoiceGenerationResult> CreateRecurringInvoiceFromQuoteAsync(
            Guid quoteId,
            DateOnly cycleDate,
            CancellationToken ct)
        {
            var quote = await LoadQuoteAsync(quoteId, ct);

            if (quote.Status != DocumentStatus.Signed)
            {
                var latestSignature = await _db.QuoteSignatures
                    .AsNoTracking()
                    .Where(x => x.QuoteId == quoteId && x.SignedAt != null)
                    .OrderByDescending(x => x.SignedAt)
                    .FirstOrDefaultAsync(ct);

                if (latestSignature != null)
                {
                    quote.Status = DocumentStatus.Signed;
                    if (!quote.SignedAt.HasValue)
                        quote.SignedAt = latestSignature.SignedAt;

                    await _db.SaveChangesAsync(ct);
                }
            }


            if (!quote.RecurringEnabled || !quote.RecurringIsActive)
            {
                _logger.LogInformation("Recurring disabled/inactive. QuoteId={QuoteId}", quoteId);
                return InvoiceGenerationResult.Warning("Recurring billing is disabled or inactive for this quote.");
            }

            if (quote.RecurringEndDate.HasValue && cycleDate > quote.RecurringEndDate.Value)
            {
                _logger.LogInformation(
                    "Recurring cycle skipped because beyond end date. QuoteId={QuoteId} CycleDate={CycleDate}",
                    quoteId,
                    cycleDate);

                return InvoiceGenerationResult.Warning(
                    $"Recurring billing already ended before cycle {cycleDate:yyyy-MM-dd}.");
            }

            //var recurringStartDate =
            //    quote.RecurringStartDate ??
            //    (quote.SignedAt.HasValue
            //        ? DateOnly.FromDateTime(quote.SignedAt.Value.UtcDateTime)
            //        : quote.IssuedAt.HasValue
            //            ? DateOnly.FromDateTime(quote.IssuedAt.Value.UtcDateTime)
            //            : cycleDate);
            var recurringStartDate =
    quote.RecurringStartDate ??
    (quote.SignedAt.HasValue
        ? DateOnly.FromDateTime(quote.SignedAt.Value.Date)
        : quote.IssuedAt.HasValue
            ? DateOnly.FromDateTime(quote.IssuedAt.Value.Date)
            : cycleDate);

            var items = quote.Items
                .Where(x => x.BillingCycle != BillingCycle.OneTime)
                .Where(x => IsItemDueForCycle(
                    x.BillingCycle,
                    recurringStartDate,
                    cycleDate))
                .OrderBy(x => x.Position)
                .ToList();

            if (items.Count == 0)
            {
                _logger.LogInformation(
                    "No recurring items due for cycle. QuoteId={QuoteId} CycleDate={CycleDate}",
                    quoteId,
                    cycleDate);

                quote.NextRecurringInvoiceDate = CalculateNextRecurringDate(cycleDate);
                quote.LastRecurringInvoiceRunAt = DateTimeOffset.UtcNow;

                await _db.SaveChangesAsync(ct);

                return InvoiceGenerationResult.Warning(
                    $"No recurring items are due for cycle {cycleDate:yyyy-MM-dd}.");
            }

            var cycleKey = $"{quote.Id:N}:{cycleDate:yyyy-MM-dd}";

            var exists = await _db.Invoices.AnyAsync(
                x => x.QuoteId == quoteId &&
                     x.RecurringCycleKey == cycleKey,
                ct);

            if (exists)
            {
                _logger.LogInformation(
                    "Recurring invoice already exists. QuoteId={QuoteId} CycleKey={CycleKey}",
                    quoteId,
                    cycleKey);

                return InvoiceGenerationResult.Warning(
                    $"A recurring invoice already exists for cycle {cycleDate:yyyy-MM-dd}.");
            }

            var result = await CreateInvoiceInternalAsync(
           InvoiceSourceContext.FromQuote(quote),
           items.Select(MapSourceItem).ToList(),
           invoiceDate: cycleDate,
           originType: InvoiceOriginType.QuoteRecurring,
           recurringCycleKey: cycleKey,
           isRecurringInvoice: true,
           servicePeriodStart: cycleDate,
           servicePeriodEnd: quote.RecurringEndDate ?? ToDateOnly(quote.ExpiresAt),
           ct: ct);
            if (!result.Created)
                return result;

            quote.NextRecurringInvoiceDate = CalculateNextRecurringDate(cycleDate);
            quote.LastRecurringInvoiceRunAt = DateTimeOffset.UtcNow;

            if (quote.RecurringEndDate.HasValue &&
                quote.NextRecurringInvoiceDate > quote.RecurringEndDate.Value)
            {
                quote.RecurringIsActive = false;
            }

            await _db.SaveChangesAsync(ct);

            return result;
        }

        public async Task SendManualInvoiceAsync(Guid localInvoiceId, CancellationToken ct)
        {
            var invoice = await _db.Invoices.FirstOrDefaultAsync(x => x.Id == localInvoiceId, ct);

            if (invoice == null)
                throw new InvalidOperationException("Invoice not found.");

            if (invoice.DispatchStatus != InvoiceDispatchStatus.PendingManualSend)
                throw new InvalidOperationException("Invoice is not pending manual send.");

            if (string.IsNullOrWhiteSpace(invoice.LexwareInvoiceId))
                throw new InvalidOperationException("Invoice has no Lexware reference.");

            await _lex.FinalizeInvoiceAsync(invoice.LexwareInvoiceId!, ct);

            var finalized = await RefreshFromLexwareUntilNotDraftAsync(
                invoice,
                maxAttempts: 8,
                delay: TimeSpan.FromSeconds(1),
                ct);

            if (!finalized)
            {
                throw new InvalidOperationException(
                    "Lexware accepted the finalize request, but the invoice is still returned as draft. Please retry in a few seconds.");
            }

            invoice.DispatchStatus = InvoiceDispatchStatus.SentManually;
            invoice.SentAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync(ct);
            await InvalidateInvoiceCacheAsync(invoice.Id, ct);

            try
            {
                await _invoiceNotificationService.QueueInvoiceReadyEmailAsync(invoice.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Manual invoice sent but notification email failed. LocalInvoiceId={LocalInvoiceId} LexwareInvoiceId={LexwareInvoiceId}",
                    invoice.Id,
                    invoice.LexwareInvoiceId);
            }
        }

        private async Task<bool> RefreshFromLexwareUntilNotDraftAsync(
    Invoice invoice,
    int maxAttempts,
    TimeSpan delay,
    CancellationToken ct)
        {
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                await RefreshFromLexwareAsync(invoice, ct);

                if (!string.Equals(invoice.LexwareVoucherStatus, "draft", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                _logger.LogInformation(
                    "Lexware invoice still draft after finalize. LocalId={LocalId} LexId={LexId} Attempt={Attempt}/{MaxAttempts}",
                    invoice.Id,
                    invoice.LexwareInvoiceId,
                    attempt,
                    maxAttempts);

                if (attempt < maxAttempts)
                {
                    await Task.Delay(delay, ct);
                }
            }

            return false;
        }

        private async Task<InvoiceGenerationResult> CreateInvoiceInternalAsync(
          InvoiceSourceContext source,
          List<InvoiceSourceItemData> sourceItems,
          DateOnly invoiceDate,
          InvoiceOriginType originType,
          string? recurringCycleKey,
          bool isRecurringInvoice,
          DateOnly? servicePeriodStart,
          DateOnly? servicePeriodEnd,
          CancellationToken ct)
        {
            if (source.Status != DocumentStatus.Signed)
                return InvoiceGenerationResult.Warning($"{source.DocumentLabel} is not signed yet.");

            var customer = source.Customer;

            var billing = customer.Addresses?
                .OrderByDescending(a => a.IsDefault)
                .ThenByDescending(a => a.IsLexware)
                .FirstOrDefault();

            var recipientName = BuildRecipientName(customer, billing);
            var recipientSupplement = BuildRecipientSupplement(customer, billing);

            var street = CleanPrintText(billing?.StreetRaw) ?? string.Empty;
            var zip = CleanPrintText(billing?.PostalCode) ?? string.Empty;
            var city = CleanPrintText(billing?.City) ?? string.Empty;
            var countryCode = NormalizeCountryCode(billing?.CountryCode, fallback: "DE");
            var lexwareContactId = CleanPrintText(customer.LexwareContactId);

            if (string.IsNullOrWhiteSpace(recipientName) ||
                string.IsNullOrWhiteSpace(street) ||
                string.IsNullOrWhiteSpace(zip) ||
                string.IsNullOrWhiteSpace(city))
            {
                _logger.LogWarning(
                    "Lexware skipped: missing recipient address data. Source={Source} SourceId={SourceId}, CustomerId={CustomerId}",
                    source.DocumentLabel,
                    source.SourceId,
                    customer.Id);

                return InvoiceGenerationResult.Warning(
                    "Invoice was not created because recipient address data is incomplete.");
            }
            var currency = ResolveCurrency(source.Currency, sourceItems);
            var invoiceDateUtc = ToLexwareDate(invoiceDate);
            var taxRate = source.ApplyVat ? FixedTaxRatePercentage : 0m;

            var effectiveServiceStart = servicePeriodStart ?? invoiceDate;
            var effectiveServiceStartUtc = ToLexwareDate(effectiveServiceStart);
            DateTimeOffset? effectiveServiceEndUtc = servicePeriodEnd.HasValue
       ? ToLexwareDate(servicePeriodEnd.Value)
       : null;



            //var finalizeOnLexware = source.InvoiceSendMode == InvoiceSendMode.Automatic;
            //var dispatchStatus = source.InvoiceSendMode == InvoiceSendMode.Automatic
            //    ? InvoiceDispatchStatus.SentAutomatically
            //    : InvoiceDispatchStatus.PendingManualSend;

            var finalizeOnLexware = true;

            var dispatchStatus = source.InvoiceSendMode == InvoiceSendMode.Automatic
                ? InvoiceDispatchStatus.SentAutomatically
                : InvoiceDispatchStatus.SentManually;

            var lexLineItems = sourceItems
                .OrderBy(i => i.Position)
                .Select(i => new LexwareInvoiceLineItem
                {
                    Type = "custom",
                    Name = i.Title,
                    Description = i.Description,
                    Quantity = i.Quantity <= 0 ? 1m : i.Quantity,
                    UnitName = i.UnitName,
                    UnitPrice = new LexwareUnitPrice
                    {
                        Currency = currency,
                        NetAmount = i.UnitPrice,
                        TaxRatePercentage = taxRate
                    },
                    DiscountPercentage = ResolveDiscountPercent(i)
                })
                .ToList();

            var req = new LexwareInvoiceCreateRequest
            {
                Archived = false,
                Language = "de",
                VoucherDate = invoiceDateUtc,
                Address = new LexwareInvoiceAddress
                {
                    ContactId = lexwareContactId,
                    Name = recipientName,
                    Supplement = recipientSupplement,
                    Street = street,
                    Zip = zip,
                    City = city,
                    CountryCode = countryCode
                },
                LineItems = lexLineItems,
                TotalPrice = new LexwareTotalPrice
                {
                    Currency = currency
                },
                TaxConditions = new LexwareTaxConditions
                {
                    TaxType = "net"
                },
                PaymentConditions = new LexwarePaymentConditions
                {
                    PaymentTermLabel = string.IsNullOrWhiteSpace(_opt.DefaultPaymentTermLabel)
                        ? "Zahlbar sofort, rein netto"
                        : _opt.DefaultPaymentTermLabel,
                    PaymentTermDuration = _opt.DefaultPaymentTermDays
                },
                ShippingConditions = new LexwareShippingConditions
                {
                    ShippingType = effectiveServiceEndUtc.HasValue ? "serviceperiod" : "service",
                    ShippingDate = effectiveServiceStartUtc,
                    ShippingEndDate = effectiveServiceEndUtc
                },
                Title = "Rechnung",
                Introduction = BuildGermanInvoiceIntroduction(source, isRecurringInvoice, invoiceDate),
                Remark = BuildGermanInvoiceRemark()
            };

            _logger.LogInformation(
                "Lexware create START. Source={Source} SourceId={SourceId} Recurring={Recurring} Finalize={Finalize}",
                source.DocumentLabel,
                source.SourceId,
                isRecurringInvoice,
                finalizeOnLexware);

            //var created = await _lex.CreateInvoiceAsync(req, finalize: finalizeOnLexware, ct);
            //var invDoc = await _lex.GetInvoiceAsync(created.Id, ct);

            //var localInvoice = new Invoice
            //{
            //    ProjectId = source.ProjectId,
            //    ContractId = source.ContractId,
            //    QuoteId = source.QuoteId,
            //    Currency = currency,
            //    ApplyVat = source.ApplyVat,
            //    OriginType = originType,
            //    IsRecurringInvoice = isRecurringInvoice,
            //    RecurringCycleDate = isRecurringInvoice ? invoiceDate : null,
            //    RecurringCycleKey = recurringCycleKey,
            //    DispatchStatus = dispatchStatus,
            //    SentAt = dispatchStatus == InvoiceDispatchStatus.SentAutomatically
            //        ? DateTimeOffset.UtcNow
            //        : null,
            //    Notes = BuildGeneratedInvoiceNote(source, isRecurringInvoice),
            //    LexwareInvoiceId = created.Id,
            //    LexwareResourceUri = created.ResourceUri,
            //    LexwareVersion = created.Version
            //};

            _logger.LogInformation(
    "Lexware request shippingConditions: {Shipping}",
    JsonSerializer.Serialize(req.ShippingConditions));

            var created = await _lex.CreateInvoiceAsync(req, finalize: finalizeOnLexware, ct);
            var invDoc = await GetInvoiceAfterFinalizeAsync(
                created.Id,
                maxAttempts: 8,
                delay: TimeSpan.FromSeconds(1),
                ct);

            _logger.LogInformation(
    "Lexware response shippingConditions: {Shipping}",
    invDoc.RootElement.TryGetProperty("shippingConditions", out var sc)
        ? sc.GetRawText()
        : "<missing>");

            var localInvoice = new Invoice
            {
                ProjectId = source.ProjectId,
                ContractId = source.ContractId,
                QuoteId = source.QuoteId,
                Currency = currency,
                ApplyVat = source.ApplyVat,
                OriginType = originType,
                IsRecurringInvoice = isRecurringInvoice,
                RecurringCycleDate = isRecurringInvoice ? invoiceDate : null,
                RecurringCycleKey = recurringCycleKey,
                DispatchStatus = dispatchStatus,
                SentAt = DateTimeOffset.UtcNow,
                Notes = BuildGeneratedInvoiceNote(source, isRecurringInvoice),
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
                sourceItemsForServiceMapping: sourceItems,
                defaultTaxRatePercent: taxRate);

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

            if (dispatchStatus == InvoiceDispatchStatus.SentAutomatically)
            {
                try
                {
                    await _invoiceNotificationService.QueueInvoiceReadyEmailAsync(localInvoice.Id, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Automatic invoice created but notification email failed. LocalInvoiceId={LocalInvoiceId} LexwareInvoiceId={LexwareInvoiceId}",
                        localInvoice.Id,
                        localInvoice.LexwareInvoiceId);
                }
            }

            _logger.LogInformation(
                "Lexware invoice created. Source={Source} SourceId={SourceId} LocalId={LocalId} LexId={LexId}",
                source.DocumentLabel,
                source.SourceId,
                localInvoice.Id,
                localInvoice.LexwareInvoiceId);

            return InvoiceGenerationResult.Success(
                isRecurringInvoice
                    ? $"Recurring invoice created for cycle {invoiceDate:yyyy-MM-dd}."
                    : "One-time invoice created successfully.");
        }
        private async Task<JsonDocument> GetInvoiceAfterFinalizeAsync(
    string lexwareInvoiceId,
    int maxAttempts,
    TimeSpan delay,
    CancellationToken ct)
        {
            JsonDocument? lastDoc = null;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                lastDoc = await _lex.GetInvoiceAsync(lexwareInvoiceId, ct);

                var voucherStatus = TryGetString(lastDoc.RootElement, "voucherStatus");

                if (!string.Equals(voucherStatus, "draft", StringComparison.OrdinalIgnoreCase))
                {
                    return lastDoc;
                }

                _logger.LogInformation(
                    "Lexware invoice still draft right after create/finalize. LexId={LexId} Attempt={Attempt}/{MaxAttempts}",
                    lexwareInvoiceId,
                    attempt,
                    maxAttempts);

                if (attempt < maxAttempts)
                {
                    await Task.Delay(delay, ct);
                }
            }

            return lastDoc ?? await _lex.GetInvoiceAsync(lexwareInvoiceId, ct);
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
                .Include(c => c.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(cu => cu.Contacts)
                .Include(c => c.Items)
                    .ThenInclude(i => i.Service)
                .FirstOrDefaultAsync(c => c.Id == contractId, ct);

            if (contract == null)
                throw new InvalidOperationException("Contract not found.");

            return contract;
        }

        private async Task<Quote> LoadQuoteAsync(Guid quoteId, CancellationToken ct)
        {
            var quote = await _db.Quotes
                .Include(q => q.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(cu => cu.Addresses)
                .Include(q => q.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(cu => cu.EmailAddresses)
                .Include(q => q.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(cu => cu.Contacts)
                .Include(q => q.Items)
                    .ThenInclude(i => i.Service)
                .FirstOrDefaultAsync(q => q.Id == quoteId, ct);

            if (quote == null)
                throw new InvalidOperationException("Quote not found.");

            return quote;
        }

        private static InvoiceSourceItemData MapSourceItem(ContractItem item) => new()
        {
            ServiceId = item.ServiceId,
            Title = ResolveTitle(item),
            Description = ResolveDescription(item),
            Quantity = item.Quantity <= 0 ? 1m : item.Quantity,
            UnitPrice = ResolveNetAmount(item),
            Position = item.Position,
            Config = item.Config ?? JsonDocument.Parse("{}"),
            BillingCycle = item.BillingCycle,
            DiscountType = item.DiscountType,
            DiscountValue = item.DiscountValue,
            UnitType = ResolveUnitType(item),
            UnitName = ResolveUnitName(item),
            ServiceDefaultCurrency = item.Service?.DefaultCurrency
        };

        private static InvoiceSourceItemData MapSourceItem(QuoteItem item) => new()
        {
            ServiceId = item.ServiceId,
            Title = ResolveTitle(item),
            Description = ResolveDescription(item),
            Quantity = item.Quantity <= 0 ? 1m : item.Quantity,
            UnitPrice = ResolveNetAmount(item),
            Position = item.Position,
            Config = item.Config ?? JsonDocument.Parse("{}"),
            BillingCycle = item.BillingCycle,
            DiscountType = item.DiscountType,
            DiscountValue = item.DiscountValue,
            UnitType = ResolveUnitType(item),
            UnitName = ResolveUnitName(item),
            ServiceDefaultCurrency = item.Service?.DefaultCurrency
        };

        private static decimal ResolveNetAmount(ContractItem item)
        {
            var unit =
                item.AgreedPrice ??
                (item.UnitPrice > 0 ? item.UnitPrice : (decimal?)null) ??
                (item.Service != null && item.Service.BasePrice > 0 ? item.Service.BasePrice : (decimal?)null) ??
                0m;

            if (unit < 0)
                unit = 0m;

            return unit;
        }

        private static decimal ResolveNetAmount(QuoteItem item)
        {
            var unit =
                (item.UnitPrice > 0 ? item.UnitPrice : (decimal?)null) ??
                (item.Service != null && item.Service.BasePrice > 0 ? item.Service.BasePrice : (decimal?)null) ??
                0m;

            if (unit < 0)
                unit = 0m;

            return unit;
        }

        private static decimal ResolveDiscountPercent(ContractItem item)
        {
            if (item.DiscountType == DiscountType.Percent &&
                item.DiscountValue.HasValue &&
                item.DiscountValue.Value > 0)
            {
                return item.DiscountValue.Value;
            }

            return 0m;
        }

        private static decimal ResolveDiscountPercent(QuoteItem item)
        {
            if (item.DiscountType == DiscountType.Percent &&
                item.DiscountValue.HasValue &&
                item.DiscountValue.Value > 0)
            {
                return item.DiscountValue.Value;
            }

            return 0m;
        }

        private static decimal ResolveDiscountPercent(InvoiceSourceItemData item)
        {
            if (item.DiscountType == DiscountType.Percent &&
                item.DiscountValue.HasValue &&
                item.DiscountValue.Value > 0)
            {
                return item.DiscountValue.Value;
            }

            return 0m;
        }

        private static string ResolveCurrency(string? documentCurrency, IEnumerable<InvoiceSourceItemData> sourceItems)
        {
            var normalizedCurrency = CleanPrintText(documentCurrency);
            if (!string.IsNullOrWhiteSpace(normalizedCurrency))
                return normalizedCurrency!;

            var serviceCurrency = sourceItems
                .Select(i => CleanPrintText(i.ServiceDefaultCurrency))
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

            return !string.IsNullOrWhiteSpace(serviceCurrency) ? serviceCurrency! : "EUR";
        }

        private static string ResolveTitle(ContractItem item)
        {
            var title = CleanPrintText(item.Title);
            if (!string.IsNullOrWhiteSpace(title))
                return title!;

            var serviceName = CleanPrintText(item.Service?.Name);
            if (!string.IsNullOrWhiteSpace(serviceName))
                return serviceName!;

            return $"Position {item.Position}";
        }

        private static string ResolveTitle(QuoteItem item)
        {
            var title = CleanPrintText(item.Title);
            if (!string.IsNullOrWhiteSpace(title))
                return title!;

            var serviceName = CleanPrintText(item.Service?.Name);
            if (!string.IsNullOrWhiteSpace(serviceName))
                return serviceName!;

            return $"Position {item.Position}";
        }

        private static string? ResolveDescription(ContractItem item)
        {
            var description = CleanPrintText(item.Description);
            if (!string.IsNullOrWhiteSpace(description))
                return description;

            return CleanPrintText(item.Service?.Description);
        }

        private static string? ResolveDescription(QuoteItem item)
        {
            var description = CleanPrintText(item.Description);
            if (!string.IsNullOrWhiteSpace(description))
                return description;

            return CleanPrintText(item.Service?.Description);
        }

        private static ServiceUnitType ResolveUnitType(ContractItem item)
        {
            if (item.UnitType != ServiceUnitType.Custom)
                return item.UnitType;

            if (item.Service != null && item.Service.UnitType != ServiceUnitType.Custom)
                return item.Service.UnitType;

            return MapPricingModelToUnitType(item.Service?.PricingModel);
        }

        private static ServiceUnitType ResolveUnitType(QuoteItem item)
        {
            if (item.UnitType != ServiceUnitType.Custom)
                return item.UnitType;

            if (item.Service != null && item.Service.UnitType != ServiceUnitType.Custom)
                return item.Service.UnitType;

            return MapPricingModelToUnitType(item.Service?.PricingModel);
        }

        private static string ResolveUnitName(ContractItem item)
        {
            var itemUnitName = CleanPrintText(item.UnitName);
            if (!string.IsNullOrWhiteSpace(itemUnitName))
                return itemUnitName!;

            var serviceUnitName = CleanPrintText(item.Service?.UnitName);
            if (!string.IsNullOrWhiteSpace(serviceUnitName))
                return serviceUnitName!;

            return GetDefaultUnitName(ResolveUnitType(item), item.Service?.PricingModel);
        }

        private static string ResolveUnitName(QuoteItem item)
        {
            var itemUnitName = CleanPrintText(item.UnitName);
            if (!string.IsNullOrWhiteSpace(itemUnitName))
                return itemUnitName!;

            var serviceUnitName = CleanPrintText(item.Service?.UnitName);
            if (!string.IsNullOrWhiteSpace(serviceUnitName))
                return serviceUnitName!;

            return GetDefaultUnitName(ResolveUnitType(item), item.Service?.PricingModel);
        }

        private static ServiceUnitType MapPricingModelToUnitType(PricingModel? pricingModel)
        {
            return pricingModel switch
            {
                PricingModel.Hourly => ServiceUnitType.Hour,
                PricingModel.Unit => ServiceUnitType.Piece,
                PricingModel.Tiered => ServiceUnitType.Package,
                PricingModel.Fixed => ServiceUnitType.FlatRate,
                _ => ServiceUnitType.Piece
            };
        }

        private static string GetDefaultUnitName(ServiceUnitType unitType, PricingModel? pricingModel = null)
        {
            return unitType switch
            {
                ServiceUnitType.Hour => "Std.",
                ServiceUnitType.Day => "Tag",
                ServiceUnitType.Month => "Monat",
                ServiceUnitType.FlatRate => "Pauschale",
                ServiceUnitType.Package => "Paket",
                ServiceUnitType.Project => "Projekt",
                ServiceUnitType.Piece => "Stück",
                ServiceUnitType.Custom => pricingModel switch
                {
                    PricingModel.Fixed => "Pauschale",
                    PricingModel.Hourly => "Std.",
                    PricingModel.Unit => "Stück",
                    PricingModel.Tiered => "Paket",
                    _ => "Einheit"
                },
                _ => "Einheit"
            };
        }

        private static string? CleanPrintText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var s = value.Trim();

            if (s.Equals("N/A", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("NA", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("-", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return s;
        }

        private static string NormalizeCountryCode(string? value, string fallback = "DE")
        {
            var s = CleanPrintText(value);
            if (string.IsNullOrWhiteSpace(s))
                return fallback;

            s = s.Trim().ToUpperInvariant();
            return s.Length == 2 ? s : fallback;
        }

        private static string BuildRecipientName(Customer customer, CustomerAddress? billing)
        {
            var billingName = CleanPrintText(billing?.FullNameOrCompany);
            if (!string.IsNullOrWhiteSpace(billingName))
                return billingName!;

            var customerName = CleanPrintText(customer.Name);
            if (!string.IsNullOrWhiteSpace(customerName))
                return customerName!;

            var personName = string.Join(
                " ",
                new[]
                {
                    CleanPrintText(customer.FirstName),
                    CleanPrintText(customer.LastName)
                }.Where(x => !string.IsNullOrWhiteSpace(x)));

            if (!string.IsNullOrWhiteSpace(personName))
                return personName;

            return "Kunde";
        }

        private static string? BuildRecipientSupplement(Customer customer, CustomerAddress? billing)
        {
            var addressLine2 = CleanPrintText(billing?.AddressLine2);
            if (!string.IsNullOrWhiteSpace(addressLine2))
                return addressLine2;

            var primaryContact = customer.Contacts?
                .OrderByDescending(x => x.IsPrimary)
                .ThenBy(x => x.Name)
                .FirstOrDefault();

            if (primaryContact == null)
                return null;

            var contactName = string.Join(
                " ",
                new[]
                {
                    CleanPrintText(primaryContact.FirstName),
                    CleanPrintText(primaryContact.LastName)
                }.Where(x => !string.IsNullOrWhiteSpace(x)));

            if (string.IsNullOrWhiteSpace(contactName))
                contactName = CleanPrintText(primaryContact.Name);

            if (string.IsNullOrWhiteSpace(contactName))
                return null;

            return $"z. Hd. {contactName}";
        }

        private static string BuildGermanInvoiceIntroduction(
            InvoiceSourceContext source,
            bool isRecurringInvoice,
            DateOnly invoiceDate)
        {
            var basisWord = source.DocumentLabel == "contract" ? "Vertrag" : "Angebot";

            if (isRecurringInvoice)
            {
                return
                    $"Hiermit berechnen wir Ihnen die vereinbarten wiederkehrenden Leistungen " +
                    $"für den Abrechnungszeitraum {invoiceDate:MM.yyyy} gemäß {basisWord} {source.DocumentNumber}.";
            }

            return $"Hiermit berechnen wir Ihnen die vereinbarten Leistungen gemäß {basisWord} {source.DocumentNumber}.";
        }

        private static string BuildGeneratedInvoiceNote(InvoiceSourceContext source, bool isRecurringInvoice)
        {
            return isRecurringInvoice
                ? $"Generated recurring invoice from {source.DocumentLabel} {source.DocumentNumber}"
                : $"Generated one-time invoice from {source.DocumentLabel} {source.DocumentNumber}";
        }

        private static string BuildGermanInvoiceRemark()
            => "Vielen Dank für Ihren Auftrag.";

        private static string BuildLineItemName(ContractItem item)
        {
            var title = ResolveTitle(item);
            var description = ResolveDescription(item);

            return string.IsNullOrWhiteSpace(description)
                ? title
                : $"{title} - {description}";
        }

        private static bool IsItemDueForCycle(
            BillingCycle itemCycle,
            DateOnly recurringStartDate,
            DateOnly currentCycleDate)
        {
            if (itemCycle == BillingCycle.OneTime)
                return false;

            if (currentCycleDate < recurringStartDate)
                return false;

            var months =
                ((currentCycleDate.Year - recurringStartDate.Year) * 12) +
                (currentCycleDate.Month - recurringStartDate.Month);

            if (months < 0)
                return false;

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
                sourceItemsForServiceMapping: null,
                defaultTaxRatePercent: invoice.ApplyVat ? FixedTaxRatePercentage : 0m);

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

            _logger.LogInformation(
                "Lexware invoice refreshed. LocalId={LocalId} LexId={LexId} Status={Status} Total={Total}",
                invoice.Id,
                invoice.LexwareInvoiceId,
                invoice.LexwareVoucherStatus,
                invoice.Totals?.Total);
        }

        private void ApplyLexwareSnapshotToLocalInvoice(
            Invoice invoice,
            JsonDocument invDoc,
            string fallbackCurrency,
            DateTimeOffset fallbackIssueAt,
            int defaultPaymentTermDays,
            List<InvoiceSourceItemData>? sourceItemsForServiceMapping,
            decimal defaultTaxRatePercent)
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

            invoice.Currency = string.IsNullOrWhiteSpace(invoice.Currency)
                ? fallbackCurrency
                : invoice.Currency;

            invoice.IssuedAt = voucherDateUtc;
            //invoice.IssueDate = DateOnly.FromDateTime(voucherDateUtc.UtcDateTime);
            invoice.IssueDate = DateOnly.FromDateTime(voucherDateUtc.Date);
            var dueDate = TryGetDateOnly(root, "dueDate");
            if (dueDate is null)
            {
                var termDays =
                    TryGetInt(root, "paymentConditions", "paymentTermDuration") ??
                    defaultPaymentTermDays;

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

            var newItems = BuildInvoiceItemsFromLexware(root, sourceItemsForServiceMapping);

            foreach (var it in newItems)
            {
                it.Invoice = invoice;
                invoice.Items.Add(it);
            }

            var totals = BuildTotalsFromLexware(root, invoice.Items, voucherStatus, defaultTaxRatePercent);
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

        private List<InvoiceItem> BuildInvoiceItemsFromLexware(
            JsonElement root,
            List<InvoiceSourceItemData>? sourceItems)
        {
            var list = new List<InvoiceItem>();
            var orderedSourceItems = sourceItems?.OrderBy(a => a.Position).ToList();

            if (root.TryGetProperty("lineItems", out var li) && li.ValueKind == JsonValueKind.Array)
            {
                int pos = 1;

                foreach (var x in li.EnumerateArray())
                {
                    var qty = TryGetDecimal(x, "quantity") ?? 1m;

                    decimal unitNet =
                        TryGetDecimal(x, "unitPrice", "netAmount") ??
                        TryGetDecimal(x, "unitPrice", "grossAmount") ??
                        0m;

                    var discountPct = TryGetDecimal(x, "discountPercentage");
                    var lexwareName = TryGetString(x, "name") ?? $"Position {pos}";
                    var lexwareUnitName = TryGetString(x, "unitName");
                    var lexwareDescription = TryGetString(x, "description");
                    var si = orderedSourceItems?.ElementAtOrDefault(pos - 1);

                    var item = new InvoiceItem
                    {
                        Title = si != null ? si.Title : lexwareName,
                        Description = si != null
                            ? si.Description ?? string.Empty
                            : (lexwareDescription ?? string.Empty),
                        Quantity = qty,
                        UnitPrice = unitNet,
                        Position = pos,
                        ServiceId = si?.ServiceId,
                        Config = si?.Config ?? JsonDocument.Parse("{}"),
                        BillingCycle = si?.BillingCycle ?? BillingCycle.OneTime,
                        DiscountType = si?.DiscountType,
                        DiscountValue = si?.DiscountValue,
                        UnitType = si != null ? si.UnitType : ServiceUnitType.Custom,
                        UnitName = si != null
                            ? si.UnitName
                            : (!string.IsNullOrWhiteSpace(lexwareUnitName)
                                ? lexwareUnitName!
                                : GetDefaultUnitName(ServiceUnitType.Custom))
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

            if (orderedSourceItems != null && orderedSourceItems.Count > 0)
            {
                foreach (var si in orderedSourceItems)
                {
                    list.Add(new InvoiceItem
                    {
                        Title = si.Title,
                        Description = si.Description ?? string.Empty,
                        Quantity = si.Quantity <= 0 ? 1m : si.Quantity,
                        UnitPrice = si.UnitPrice,
                        Position = si.Position,
                        ServiceId = si.ServiceId,
                        Config = si.Config ?? JsonDocument.Parse("{}"),
                        BillingCycle = si.BillingCycle,
                        DiscountType = si.DiscountType,
                        DiscountValue = si.DiscountValue,
                        UnitType = si.UnitType,
                        UnitName = si.UnitName
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
            decimal subtotal =
                TryGetDecimal(root, "totalPrice", "totalNetAmount") ??
                items.Sum(i => i.Quantity * i.UnitPrice);

            decimal tax =
                TryGetDecimal(root, "totalPrice", "totalTaxAmount") ??
                Math.Round(subtotal * (defaultTaxRatePercent / 100m), 2);

            decimal total =
                TryGetDecimal(root, "totalPrice", "totalGrossAmount") ??
                (subtotal + tax);

            decimal balance =
                (voucherStatus ?? "").Equals("paid", StringComparison.OrdinalIgnoreCase)
                    ? 0m
                    : (TryGetDecimal(root, "openAmount") ??
                       TryGetDecimal(root, "openGrossAmount") ??
                       total);

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
            if (!string.IsNullOrWhiteSpace(invoice.LexwarePdfPath))
            {
                invoice.LexwarePdfPath = null;
                invoice.LexwareSyncedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }

        private string? ResolveStoredPdfPath(string? storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath))
                return null;

            var baseDir = Path.Combine(_env.ContentRootPath, "App_Data", "LexwareInvoices");

            if (Path.IsPathRooted(storedPath))
                return Path.GetFullPath(storedPath);

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
            if (!TryGetElement(root, out var el, path))
                return null;

            return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
        }

        private static int? TryGetInt(JsonElement root, params string[] path)
        {
            if (!TryGetElement(root, out var el, path))
                return null;

            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i))
                return i;

            if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var j))
                return j;

            return null;
        }

        private static decimal? TryGetDecimal(JsonElement root, params string[] path)
        {
            if (!TryGetElement(root, out var el, path))
                return null;

            if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d))
                return d;

            if (el.ValueKind == JsonValueKind.String &&
                decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var x))
            {
                return x;
            }

            return null;
        }

        private static DateTimeOffset? TryGetDateTimeOffset(JsonElement root, params string[] path)
        {
            if (!TryGetElement(root, out var el, path))
                return null;

            if (el.ValueKind != JsonValueKind.String)
                return null;

            if (DateTimeOffset.TryParse(
                el.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var dto))
            {
                return ToUtc(dto);
            }

            return null;
        }

        private static DateOnly? TryGetDateOnly(JsonElement root, params string[] path)
        {
            if (!TryGetElement(root, out var el, path))
                return null;

            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();

                if (DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    return d;

                if (DateTimeOffset.TryParse(
                    s,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var dto))
                {
                    //return DateOnly.FromDateTime(ToUtc(dto).UtcDateTime);
                    return DateOnly.FromDateTime(dto.Date);
                }
            }

            return null;
        }

        private static bool TryGetElement(JsonElement root, out JsonElement el, params string[] path)
        {
            el = root;

            foreach (var p in path)
            {
                if (el.ValueKind != JsonValueKind.Object)
                    return false;

                if (!el.TryGetProperty(p, out var next))
                    return false;

                el = next;
            }

            return true;
        }
        private static DateOnly? ToDateOnly(DateTimeOffset? value)
        {
            if (!value.HasValue)
                return null;

            return DateOnly.FromDateTime(value.Value.Date);
        }

        private static DateTimeOffset ToLexwareDate(DateOnly value)
            => new DateTimeOffset(value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }
}
