using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Models.View.Registers
{
    /// <summary>
    /// Rows for the cross-project registers — every quote, contract or invoice in
    /// the business, rather than the ones belonging to one project.
    ///
    /// The existing list queries all take a project id, so a quote could only be
    /// found by first remembering which project it belonged to. These carry the
    /// customer and project with them so a row identifies itself.
    /// </summary>
    public sealed class QuoteRegisterRow
    {
        public Guid Id { get; init; }
        public Guid ProjectId { get; init; }
        public Guid CustomerId { get; init; }

        public string QuoteNo { get; init; } = "";
        public string CustomerName { get; init; } = "";
        public string ProjectTitle { get; init; } = "";

        public DocumentStatus Status { get; init; }
        public string Currency { get; init; } = "EUR";

        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? IssuedAt { get; init; }
        public DateTimeOffset? ExpiresAt { get; init; }
        public DateTimeOffset? SignedAt { get; init; }

        public int ItemCount { get; init; }

        /// <summary>
        /// Sum of quantity × unit price across the items. This is the line value
        /// before item discounts and VAT — the same figure the per-project quote
        /// list has always shown, computed the same way. Quotes have no stored
        /// totals table to read a final figure from.
        /// </summary>
        public decimal ItemsTotal { get; init; }
    }

    public sealed class ContractRegisterRow
    {
        public Guid Id { get; init; }
        public Guid ProjectId { get; init; }
        public Guid CustomerId { get; init; }

        public string ContractNo { get; init; } = "";
        public string CustomerName { get; init; } = "";
        public string ProjectTitle { get; init; } = "";

        public DocumentStatus Status { get; init; }
        public string Currency { get; init; } = "EUR";

        public DateOnly? StartDate { get; init; }
        public DateOnly? EndDate { get; init; }
        public DateTimeOffset? SignedAt { get; init; }
        public DateTimeOffset CreatedAt { get; init; }

        public bool RecurringEnabled { get; init; }
        public bool RecurringIsActive { get; init; }
        public DateOnly? NextRecurringInvoiceDate { get; init; }

        public int ItemCount { get; init; }

        /// <summary>Line value before discounts and VAT. See <see cref="QuoteRegisterRow.ItemsTotal"/>.</summary>
        public decimal ItemsTotal { get; init; }
    }

    public sealed class InvoiceRegisterRow
    {
        public Guid Id { get; init; }
        public Guid ProjectId { get; init; }
        public Guid CustomerId { get; init; }

        public string InvoiceNo { get; init; } = "";
        public string CustomerName { get; init; } = "";
        public string ProjectTitle { get; init; } = "";

        public DocumentStatus Status { get; init; }
        public string Currency { get; init; } = "EUR";

        public DateOnly? IssueDate { get; init; }
        public DateOnly? DueDate { get; init; }
        public DateTimeOffset CreatedAt { get; init; }

        /// <summary>Invoices do store computed totals, so these are the real figures.</summary>
        public decimal Total { get; init; }
        public decimal PaidTotal { get; init; }
        public decimal BalanceDue { get; init; }

        /// <summary>
        /// Past its due date with money still owed. Derived rather than stored,
        /// because an invoice becomes overdue by the passage of time and nothing
        /// runs at midnight to rewrite its status.
        /// </summary>
        public bool IsOverdue { get; init; }
    }

    /// <summary>
    /// Which documents a register should return. Every field is optional; the
    /// default is everything.
    /// </summary>
    public sealed class RegisterFilter
    {
        public string? Search { get; init; }
        public DocumentStatus? Status { get; init; }
        public Guid? CustomerId { get; init; }
        public Guid? ProjectId { get; init; }

        public DateOnly? From { get; init; }
        public DateOnly? To { get; init; }

        /// <summary>Invoices only: show just the ones with money still owed.</summary>
        public bool OutstandingOnly { get; init; }

        /// <summary>Invoices only: show just the ones past their due date.</summary>
        public bool OverdueOnly { get; init; }

        public int Page { get; init; } = 1;
        public int PageSize { get; init; } = 20;

        public bool HasAnyFilter =>
            !string.IsNullOrWhiteSpace(Search) ||
            Status is not null ||
            CustomerId is not null ||
            ProjectId is not null ||
            From is not null ||
            To is not null ||
            OutstandingOnly ||
            OverdueOnly;
    }
}
