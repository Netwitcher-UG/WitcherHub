using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Models.UI
{
    /// <summary>
    /// The filter bar above a register, and the state it round-trips through the
    /// query string so a filtered list can be bookmarked and shared.
    /// </summary>
    public sealed class RegisterFilterVm
    {
        public required string Search { get; init; }
        public required DocumentStatus? Status { get; init; }
        public required Guid? CustomerId { get; init; }

        /// <summary>Statuses worth offering for this document kind, already labelled.</summary>
        public required IReadOnlyList<(DocumentStatus Value, string Label)> StatusOptions { get; init; }

        public required IReadOnlyList<(Guid Id, string Name)> Customers { get; init; }

        public string SearchPlaceholder { get; init; } = "Search…";

        /// <summary>Invoices only.</summary>
        public bool ShowMoneyFilters { get; init; }
        public bool OutstandingOnly { get; init; }
        public bool OverdueOnly { get; init; }

        public bool AnyApplied =>
            !string.IsNullOrWhiteSpace(Search) ||
            Status is not null ||
            CustomerId is not null ||
            OutstandingOnly ||
            OverdueOnly;
    }
}
