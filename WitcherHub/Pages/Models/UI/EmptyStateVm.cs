namespace WitcherHub.Pages.Models.UI
{
    /// <summary>
    /// The message shown in place of an empty list.
    /// </summary>
    public sealed class EmptyStateVm
    {
        public string Title { get; init; } = "Nothing here yet";
        public string Message { get; init; } = "";
        public string Icon { get; init; } = "ri-inbox-line";

        public string? ActionText { get; init; }
        public string? ActionUrl { get; init; }
        public string ActionIcon { get; init; } = "ri-add-line";

        /// <summary>
        /// The distinction that matters to the reader: an empty register and a
        /// filter that matched nothing look identical, but only one of them means
        /// "clear the filter".
        /// </summary>
        public static EmptyStateVm NoMatches(string what) => new()
        {
            Title = "No matches",
            Message = $"No {what} match the current search or filter. Clear them to see everything.",
            Icon = "ri-search-eye-line"
        };
    }

    /// <summary>
    /// A headline figure on the dashboard.
    /// </summary>
    public sealed class StatCardVm
    {
        public required string Label { get; init; }
        public required string Value { get; init; }

        /// <summary>Smaller line under the value: what the figure is made of.</summary>
        public string? Detail { get; init; }

        public string Icon { get; init; } = "ri-bar-chart-line";

        /// <summary>success | danger | warning | info | primary</summary>
        public string Tone { get; init; } = "primary";

        public string? LinkUrl { get; init; }
        public string? LinkText { get; init; }

        public string IconWrapClass => Tone switch
        {
            "success" => "bg-success-focus text-success-main",
            "danger" => "bg-danger-focus text-danger-main",
            "warning" => "bg-warning-focus text-warning-main",
            "info" => "bg-info-focus text-info-main",
            _ => "bg-primary-50 text-primary-600"
        };
    }
}
