using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Models.View.Overview
{
    /// <summary>
    /// The state of the business in one object: what is owed, what is waiting on a
    /// customer, and what needs attention today.
    /// </summary>
    public sealed class BusinessOverview
    {
        public required MoneySummary Money { get; init; }
        public required PipelineSummary Pipeline { get; init; }

        public IReadOnlyList<AttentionItem> QuotesAwaitingDecision { get; init; } = [];
        public IReadOnlyList<AttentionItem> ContractsAwaitingSignature { get; init; } = [];
        public IReadOnlyList<AttentionItem> ContractsEndingSoon { get; init; } = [];
        public IReadOnlyList<AttentionItem> OverdueInvoices { get; init; } = [];

        public IReadOnlyList<MonthlyRevenuePoint> RevenueByMonth { get; init; } = [];

        /// <summary>
        /// True when the business has no documents at all, so the dashboard can
        /// invite the first one instead of showing a wall of zeros.
        /// </summary>
        public bool IsEmpty =>
            Pipeline.QuoteCount == 0 && Pipeline.ContractCount == 0 && Pipeline.InvoiceCount == 0;
    }

    public sealed class MoneySummary
    {
        /// <summary>Invoiced, not yet paid, whatever the due date.</summary>
        public decimal Outstanding { get; init; }

        /// <summary>The part of <see cref="Outstanding"/> that is past its due date.</summary>
        public decimal Overdue { get; init; }

        public int OutstandingInvoiceCount { get; init; }
        public int OverdueInvoiceCount { get; init; }

        /// <summary>Payments received since the first of the current month.</summary>
        public decimal CollectedThisMonth { get; init; }

        /// <summary>Payments received in the previous calendar month, for comparison.</summary>
        public decimal CollectedLastMonth { get; init; }

        /// <summary>Everything ever received. The denominator for "how are we doing".</summary>
        public decimal CollectedAllTime { get; init; }

        public string Currency { get; init; } = "EUR";

        /// <summary>
        /// Percentage change against last month, or null when last month was zero —
        /// dividing by it would produce an infinity dressed up as a statistic.
        /// </summary>
        public decimal? CollectedChangePercent =>
            CollectedLastMonth == 0m
                ? null
                : Math.Round((CollectedThisMonth - CollectedLastMonth) / CollectedLastMonth * 100m, 1);
    }

    public sealed class PipelineSummary
    {
        public int QuoteCount { get; init; }
        public int QuotesAwaitingDecisionCount { get; init; }
        public decimal QuotesAwaitingDecisionValue { get; init; }

        public int ContractCount { get; init; }
        public int LiveContractCount { get; init; }
        public decimal LiveContractValue { get; init; }

        // The attention panels list only the first few rows, so they need the real
        // totals to label themselves honestly rather than counting what fits.
        public int ContractsAwaitingSignatureCount { get; init; }
        public int ContractsEndingSoonCount { get; init; }

        public int InvoiceCount { get; init; }

        public int CustomerCount { get; init; }
        public int ActiveProjectCount { get; init; }

        /// <summary>
        /// Quotes accepted or signed, as a share of quotes that reached a customer.
        /// Null while nothing has been sent, rather than 0%, which would read as a
        /// business that never wins anything.
        /// </summary>
        public decimal? WinRatePercent { get; init; }
    }

    /// <summary>
    /// One line in a "needs attention" list. Deliberately flat: these lists exist
    /// to be scanned and clicked, not read in detail.
    /// </summary>
    public sealed class AttentionItem
    {
        public Guid Id { get; init; }
        public Guid ProjectId { get; init; }

        public string Reference { get; init; } = "";
        public string CustomerName { get; init; } = "";
        public DocumentStatus Status { get; init; }

        public decimal Amount { get; init; }
        public string Currency { get; init; } = "EUR";

        /// <summary>The date this item is measured against: sent, due, or ending.</summary>
        public DateOnly? Date { get; init; }

        /// <summary>
        /// Days since <see cref="Date"/> for something late, or until it for
        /// something upcoming. Negative means still in the future.
        /// </summary>
        public int? DaysElapsed { get; init; }
    }

    public sealed record MonthlyRevenuePoint(int Year, int Month, decimal Invoiced, decimal Collected)
    {
        public string Label => new DateOnly(Year, Month, 1).ToString("MMM yy",
            System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
    }
}
