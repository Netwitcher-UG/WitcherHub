using WitcherHub.Application.Models.View.Overview;

namespace WitcherHub.Pages.Models.UI
{
    public enum AttentionKind
    {
        Quote,
        Contract,
        Invoice
    }

    /// <summary>
    /// One of the dashboard's "needs attention" panels.
    /// </summary>
    public sealed class AttentionListVm
    {
        public required string Title { get; init; }
        public required IReadOnlyList<AttentionItem> Items { get; init; }
        public required AttentionKind Kind { get; init; }

        public string Icon { get; init; } = "ri-error-warning-line";

        /// <summary>danger | warning | info | success | primary</summary>
        public string Tone { get; init; } = "warning";

        public string EmptyMessage { get; init; } = "Nothing to do here.";

        /// <summary>
        /// How many there are in total. The panel shows only the first handful, so
        /// counting the rows on screen would under-report — a badge reading 8 above
        /// a list of 8 when 23 invoices are overdue is worse than no badge.
        /// </summary>
        public int? TotalCount { get; init; }

        public int BadgeCount => TotalCount ?? Items.Count;

        /// <summary>True when the panel is showing fewer rows than exist.</summary>
        public bool IsTruncated => TotalCount is not null && TotalCount > Items.Count;

        /// <summary>What the amount on each row represents: "outstanding", "value".</summary>
        public string AmountLabel { get; init; } = "value";

        /// <summary>
        /// How to word the date: "late" for something past due, "sent" or
        /// "waiting" for something aging, "ends" for something upcoming.
        /// </summary>
        public string DateNoun { get; init; } = "";

        public required string LinkPage { get; init; }
        public string? SeeAllUrl { get; init; }

        public string HeaderIconClass => Tone switch
        {
            "danger" => "bg-danger-focus text-danger-main",
            "warning" => "bg-warning-focus text-warning-main",
            "info" => "bg-info-focus text-info-main",
            "success" => "bg-success-focus text-success-main",
            _ => "bg-primary-50 text-primary-600"
        };

        /// <summary>
        /// The day count, worded for this panel. An item 12 days past due reads
        /// "12 days late"; one ending in 12 days reads "ends in 12 days". The same
        /// number means opposite things, so the wording has to carry the direction.
        /// </summary>
        public string DescribeAge(AttentionItem item)
        {
            if (item.DaysElapsed is null || item.Date is null)
                return "—";

            var days = item.DaysElapsed.Value;

            return DateNoun switch
            {
                "late" => days <= 0 ? "due today" : $"{days} days late",
                "ends" => days >= 0 ? "ends today" : $"in {-days} days",
                "sent" => days switch { <= 0 => "sent today", 1 => "sent yesterday", _ => $"sent {days} days ago" },
                "waiting" => days switch { <= 0 => "sent today", 1 => "waiting 1 day", _ => $"waiting {days} days" },
                _ => days switch { <= 0 => "today", 1 => "1 day", _ => $"{days} days" }
            };
        }

        /// <summary>
        /// Rows worth highlighting: something well past due, or a quote that has
        /// been sitting with a customer long enough to need chasing.
        /// </summary>
        public bool IsUrgent(AttentionItem item) =>
            item.DaysElapsed is not null &&
            DateNoun is "late" or "sent" or "waiting" &&
            item.DaysElapsed.Value >= 14;
    }
}
