using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Interfaces.ManageData
{
    /// <summary>
    /// What was received against an invoice.
    /// </summary>
    public sealed class RecordPaymentDto
    {
        public Guid InvoiceId { get; init; }
        public decimal Amount { get; init; }
        public DateOnly ReceivedOn { get; init; }
        public PaymentMethod Method { get; init; } = PaymentMethod.Bank;

        /// <summary>Bank reference, transaction id, cheque number — whatever ties this to a bank statement.</summary>
        public string? Reference { get; init; }

        /// <summary>
        /// A payment that has left the customer but has not cleared yet is
        /// recorded as Pending and does not reduce the balance until it does.
        /// </summary>
        public PaymentStatus Status { get; init; } = PaymentStatus.Success;
    }

    public sealed class PaymentRow
    {
        public Guid Id { get; init; }
        public decimal Amount { get; init; }
        public string Currency { get; init; } = "EUR";
        public PaymentMethod Method { get; init; }
        public PaymentStatus Status { get; init; }
        public string? Reference { get; init; }
        public DateTimeOffset? PaidAt { get; init; }
        public DateTimeOffset RecordedAt { get; init; }
    }

    /// <summary>
    /// The state of an invoice after a payment was applied, so the caller can tell
    /// the user what changed without re-reading the invoice.
    /// </summary>
    public sealed record PaymentResult(
        Guid PaymentId,
        decimal InvoiceTotal,
        decimal PaidTotal,
        decimal BalanceDue,
        bool InvoiceIsNowPaid);

    /// <summary>
    /// Recording money received against an invoice.
    ///
    /// The <c>Payment</c> entity, <c>InvoiceTotal.PaidTotal</c> and
    /// <c>BalanceDue</c> have existed since the schema was written, and the invoice
    /// totals calculation already sums payments — but nothing in the application
    /// ever created a Payment row. The last step of the business flow, customer →
    /// project → quote → contract → invoice → payment, had no way to happen.
    /// </summary>
    public interface IPayments
    {
        Task<PaymentResult> RecordAsync(RecordPaymentDto dto, CancellationToken ct = default);

        /// <summary>Reverses a payment recorded in error and restores the balance.</summary>
        Task<PaymentResult> DeleteAsync(Guid paymentId, CancellationToken ct = default);

        Task<IReadOnlyList<PaymentRow>> GetForInvoiceAsync(Guid invoiceId, CancellationToken ct = default);
    }
}
