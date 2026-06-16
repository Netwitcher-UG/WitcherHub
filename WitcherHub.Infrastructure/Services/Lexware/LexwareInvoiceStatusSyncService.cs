using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
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
    public sealed class InvoiceStatusChangeResult
    {
        public Guid InvoiceId { get; init; }
        public string LocalStatus { get; init; } = string.Empty;
        public string LexwareStatus { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }

    public sealed class LexwareInvoiceStatusSyncService
    {
        private readonly AppDbContext _db;
        private readonly LexwareClient _lexware;
        private readonly IAppCache _cache;
        private readonly ILogger<LexwareInvoiceStatusSyncService> _logger;

        public LexwareInvoiceStatusSyncService(
            AppDbContext db,
            LexwareClient lexware,
            IAppCache cache,
            ILogger<LexwareInvoiceStatusSyncService> logger)
        {
            _db = db;
            _lexware = lexware;
            _cache = cache;
            _logger = logger;
        }

        public async Task<InvoiceStatusChangeResult> ChangeStatusFromWebsiteAsync(
            Guid projectId,
            Guid invoiceId,
            DocumentStatus requestedStatus,
            CancellationToken ct = default)
        {
            var invoice = await _db.Invoices
                .Include(x => x.Totals)
                .FirstOrDefaultAsync(x => x.Id == invoiceId && x.ProjectId == projectId, ct);

            if (invoice is null)
                throw new InvalidOperationException("Invoice not found.");

            if (string.IsNullOrWhiteSpace(invoice.LexwareInvoiceId))
                throw new InvalidOperationException("This invoice is not linked to Lexware.");

            var remote = await LoadRemoteAsync(invoice.LexwareInvoiceId!, ct);
            var currentLexwareStatus = NormalizeStatus(
                FirstNonEmpty(
                    TryGetString(remote.Payment?.RootElement, "voucherStatus"),
                    TryGetString(remote.Invoice.RootElement, "voucherStatus")));

            if (Matches(requestedStatus, "Open", "Issued"))
            {
                if (currentLexwareStatus == "draft")
                {
                    await _lexware.FinalizeInvoiceAsync(invoice.LexwareInvoiceId!, ct);
                    return await SyncIntoExistingInvoiceAsync(
                        invoice,
                        "Invoice finalized in Lexware and synced back to the website.",
                        ct);
                }

                if (IsOpenLike(currentLexwareStatus))
                {
                    return await SyncIntoExistingInvoiceAsync(
                        invoice,
                        "Invoice is already open in Lexware. Local status was refreshed.",
                        ct);
                }

                throw new NotSupportedException(
                    $"Lexware returned the invoice as '{currentLexwareStatus}'. The invoices API cannot force it back to open.");
            }

            if (Matches(requestedStatus, "Overdue"))
            {
                var result = await SyncIntoExistingInvoiceAsync(
                    invoice,
                    "Invoice status was refreshed from Lexware.",
                    ct);

                if (Matches(invoice.Status, "Overdue"))
                    return result.WithMessage("Invoice is overdue in Lexware and was synced to the website.");

                throw new NotSupportedException(
                    "In Lexware, overdue is derived from the due date. It cannot be set directly through the invoices API.");
            }

            if (Matches(requestedStatus, "Paid"))
            {
                throw new NotSupportedException(
                    "Lexware does not allow changing an invoice to paid through the invoices API, and the payments endpoint is read-only. Mark it as paid in Lexware and the webhook will sync it back to your website.");
            }

            if (Matches(requestedStatus, "Cancelled", "Void"))
            {
                throw new NotSupportedException(
                    "Lexware does not allow changing an invoice to cancelled or voided through the invoices API. Cancel it in Lexware and the webhook will sync it back to your website.");
            }

            throw new NotSupportedException($"Unsupported invoice status '{requestedStatus}'.");
        }

        public async Task<InvoiceStatusChangeResult?> HandleWebhookAsync(
            LexwareWebhookPayload payload,
            CancellationToken ct = default)
        {
            var eventType = (payload.EventType ?? string.Empty).Trim().ToLowerInvariant();
            var resourceId = (payload.ResourceId ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(resourceId))
                return null;

            if (eventType is not ("invoice.changed" or "invoice.status.changed" or "payment.changed"))
                return null;

            var invoice = await _db.Invoices
                .Include(x => x.Totals)
                .FirstOrDefaultAsync(x => x.LexwareInvoiceId == resourceId, ct);

            if (invoice is null)
            {
                _logger.LogInformation(
                    "Ignoring Lexware webhook {EventType} because no local invoice is linked to Lexware id {LexwareInvoiceId}.",
                    eventType,
                    resourceId);

                return null;
            }

            return await SyncIntoExistingInvoiceAsync(
                invoice,
                $"Webhook '{eventType}' processed successfully.",
                ct);
        }

        public async Task<InvoiceStatusChangeResult> SyncByLexwareInvoiceIdAsync(
            string lexwareInvoiceId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(lexwareInvoiceId))
                throw new ArgumentException("Lexware invoice id is required.", nameof(lexwareInvoiceId));

            var invoice = await _db.Invoices
                .Include(x => x.Totals)
                .FirstOrDefaultAsync(x => x.LexwareInvoiceId == lexwareInvoiceId, ct);

            if (invoice is null)
                throw new InvalidOperationException("No local invoice is linked to the provided Lexware invoice id.");

            return await SyncIntoExistingInvoiceAsync(
                invoice,
                "Invoice status was refreshed from Lexware.",
                ct);
        }

        private async Task<InvoiceStatusChangeResult> SyncIntoExistingInvoiceAsync(
            Invoice invoice,
            string successMessage,
            CancellationToken ct)
        {
            var remote = await LoadRemoteAsync(invoice.LexwareInvoiceId!, ct);
            ApplyRemoteState(invoice, remote.Invoice, remote.Payment);

            await _db.SaveChangesAsync(ct);
            await InvalidateInvoiceCacheAsync(invoice.Id, ct);

            _logger.LogInformation(
                "Invoice status synced. LocalId={LocalId} LexwareId={LexwareId} LocalStatus={LocalStatus} LexwareStatus={LexwareStatus}",
                invoice.Id,
                invoice.LexwareInvoiceId,
                invoice.Status,
                invoice.LexwareVoucherStatus);

            return BuildResult(invoice, successMessage);
        }

        private async Task<(JsonDocument Invoice, JsonDocument? Payment)> LoadRemoteAsync(
            string lexwareInvoiceId,
            CancellationToken ct)
        {
            var invoiceDoc = await _lexware.GetInvoiceAsync(lexwareInvoiceId, ct);
            JsonDocument? paymentDoc = null;

            try
            {
                paymentDoc = await _lexware.GetPaymentAsync(lexwareInvoiceId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not load payment details from Lexware for invoice {LexwareInvoiceId}. Invoice data will still be used.",
                    lexwareInvoiceId);
            }

            return (invoiceDoc, paymentDoc);
        }

        private void ApplyRemoteState(
            Invoice invoice,
            JsonDocument invoiceDoc,
            JsonDocument? paymentDoc)
        {
            var invoiceRoot = invoiceDoc.RootElement;
            var paymentRoot = paymentDoc?.RootElement;

            var voucherNumber = FirstNonEmpty(
                TryGetString(invoiceRoot, "voucherNumber"),
                invoice.LexwareVoucherNumber,
                invoice.InvoiceNo);

            var lexwareStatus = NormalizeStatus(
                FirstNonEmpty(
                    TryGetString(paymentRoot, "voucherStatus"),
                    TryGetString(invoiceRoot, "voucherStatus"),
                    invoice.LexwareVoucherStatus));

            var issueAt = TryGetDateTimeOffset(invoiceRoot, "voucherDate");
            var dueDate = TryGetDateOnly(invoiceRoot, "dueDate") ?? invoice.DueDate;
            var paidAt = TryGetDateTimeOffset(paymentRoot, "paidDate");
            var version = TryGetInt(invoiceRoot, "version");

            decimal total =
                TryGetDecimal(invoiceRoot, "totalPrice", "totalGrossAmount") ??
                invoice.Totals?.Total ??
                0m;

            decimal subtotal =
                TryGetDecimal(invoiceRoot, "totalPrice", "totalNetAmount") ??
                invoice.Totals?.Subtotal ??
                total;

            decimal tax =
                TryGetDecimal(invoiceRoot, "totalPrice", "totalTaxAmount") ??
                invoice.Totals?.TaxTotal ??
                Math.Max(0m, total - subtotal);

            decimal balance =
                TryGetDecimal(paymentRoot, "openAmount") ??
                TryGetDecimal(invoiceRoot, "openAmount") ??
                (IsPaidLexwareStatus(lexwareStatus) ? 0m : invoice.Totals?.BalanceDue ?? total);

            decimal paid = Math.Max(0m, total - balance);

            invoice.LexwareSnapshot = CloneJson(invoiceDoc);
            invoice.LexwareSyncedAt = DateTimeOffset.UtcNow;
            invoice.LexwareVoucherStatus = lexwareStatus;

            if (version.HasValue)
                invoice.LexwareVersion = version.Value;

            if (!string.IsNullOrWhiteSpace(voucherNumber))
            {
                invoice.LexwareVoucherNumber = voucherNumber;
                invoice.InvoiceNo = voucherNumber;
            }

            if (issueAt.HasValue)
            {
                var utc = issueAt.Value.ToUniversalTime();
                invoice.IssuedAt = utc;
                invoice.IssueDate = DateOnly.FromDateTime(utc.UtcDateTime);
            }

            invoice.DueDate = dueDate;
            invoice.Status = ResolveLocalStatus(lexwareStatus, dueDate);

            if (IsPaidLexwareStatus(lexwareStatus))
            {
                invoice.PaidAt ??= paidAt ?? DateTimeOffset.UtcNow;
            }

            if (invoice.Totals == null)
            {
                invoice.Totals = new InvoiceTotal
                {
                    Invoice = invoice
                };
            }

            invoice.Totals.Subtotal = subtotal;
            invoice.Totals.DiscountTotal = invoice.Totals.DiscountTotal;
            invoice.Totals.TaxTotal = tax;
            invoice.Totals.Total = total;
            invoice.Totals.PaidTotal = paid;
            invoice.Totals.BalanceDue = balance;
            invoice.Totals.UpdatedAt = DateTimeOffset.UtcNow;
        }

        private async Task InvalidateInvoiceCacheAsync(Guid invoiceId, CancellationToken ct)
        {
            await _cache.RemoveAsync(InvoiceCacheKeys.Details(invoiceId), ct);
            await _cache.BumpVersionAsync(InvoiceCacheKeys.ListVersionKey, ct);
        }

        private static InvoiceStatusChangeResult BuildResult(Invoice invoice, string message)
            => new()
            {
                InvoiceId = invoice.Id,
                LocalStatus = invoice.Status.ToString(),
                LexwareStatus = invoice.LexwareVoucherStatus ?? string.Empty,
                Message = message
            };

        private static bool Matches(DocumentStatus value, params string[] names)
        {
            var actual = value.ToString();
            return names.Any(x => string.Equals(x, actual, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsOpenLike(string? lexwareStatus)
            => lexwareStatus is "open" or "transferred" or "sepadebit";

        private static bool IsPaidLexwareStatus(string? lexwareStatus)
            => lexwareStatus is "paid" or "paidoff";

        private static string NormalizeStatus(string? value)
            => (value ?? string.Empty).Trim().ToLowerInvariant();

        private static DocumentStatus ResolveLocalStatus(string? lexwareStatus, DateOnly? dueDate)
        {
            var normalized = NormalizeStatus(lexwareStatus);

            if (normalized == "draft")
                return ParseStatus("Draft");

            if (normalized is "paid" or "paidoff")
                return ParseStatus("Paid");

            if (normalized is "voided" or "void")
                return ParseStatus("Cancelled", "Void");

            if (normalized is "open" or "transferred" or "sepadebit")
            {
                var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
                if (dueDate.HasValue && dueDate.Value < todayUtc)
                    return ParseStatus("Overdue", "Open", "Issued");

                return ParseStatus("Open", "Issued");
            }

            return ParseStatus("Open", "Issued");
        }

        private static DocumentStatus ParseStatus(params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (Enum.TryParse<DocumentStatus>(candidate, ignoreCase: true, out var parsed))
                    return parsed;
            }

            throw new InvalidOperationException(
                $"Could not map any of these names to DocumentStatus: {string.Join(", ", candidates)}");
        }

        private static string? FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        private static JsonDocument CloneJson(JsonDocument doc)
            => JsonDocument.Parse(doc.RootElement.GetRawText());

        private static string? TryGetString(JsonElement? root, params string[] path)
        {
            if (!TryGetElement(root, out var el, path))
                return null;

            return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
        }

        private static int? TryGetInt(JsonElement? root, params string[] path)
        {
            if (!TryGetElement(root, out var el, path))
                return null;

            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i))
                return i;

            if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var j))
                return j;

            return null;
        }

        private static decimal? TryGetDecimal(JsonElement? root, params string[] path)
        {
            if (!TryGetElement(root, out var el, path))
                return null;

            if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d))
                return d;

            if (el.ValueKind == JsonValueKind.String &&
                decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return null;
        }

        private static DateTimeOffset? TryGetDateTimeOffset(JsonElement? root, params string[] path)
        {
            if (!TryGetElement(root, out var el, path) || el.ValueKind != JsonValueKind.String)
                return null;

            if (DateTimeOffset.TryParse(
                el.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var dto))
            {
                return dto;
            }

            return null;
        }

        private static DateOnly? TryGetDateOnly(JsonElement? root, params string[] path)
        {
            if (!TryGetElement(root, out var el, path) || el.ValueKind != JsonValueKind.String)
                return null;

            var raw = el.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
                return dateOnly;

            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                return DateOnly.FromDateTime(dto.Date);

            return null;
        }

        private static bool TryGetElement(JsonElement? root, out JsonElement el, params string[] path)
        {
            el = root ?? default;

            if (root is null)
                return false;

            foreach (var segment in path)
            {
                if (el.ValueKind != JsonValueKind.Object)
                    return false;

                if (!el.TryGetProperty(segment, out var next))
                    return false;

                el = next;
            }

            return true;
        }
    }

    internal static class InvoiceStatusChangeResultExtensions
    {
        public static InvoiceStatusChangeResult WithMessage(this InvoiceStatusChangeResult source, string message)
            => new()
            {
                InvoiceId = source.InvoiceId,
                LocalStatus = source.LocalStatus,
                LexwareStatus = source.LexwareStatus,
                Message = message
            };
    }
}
