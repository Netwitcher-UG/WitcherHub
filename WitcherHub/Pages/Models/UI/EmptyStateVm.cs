namespace WitcherHub.Pages.Models.UI
{
    /// <summary>
    /// One thing the reader can do from an empty state.
    ///
    /// <see cref="IsButton"/> distinguishes a link to another page from a control
    /// the page's own script handles — an empty contract offers both: "upload a
    /// contract" opens a panel here, "go to projects" leaves.
    /// </summary>
    public sealed class EmptyStateAction
    {
        public required string Text { get; init; }

        /// <summary>A URL when <see cref="IsButton"/> is false, a data-action name when it is true.</summary>
        public required string Url { get; init; }

        public string Icon { get; init; } = "ri-add-line";
        public bool IsPrimary { get; init; }
        public bool IsButton { get; init; }
    }

    /// <summary>
    /// The message shown in place of an empty list.
    /// </summary>
    public sealed class EmptyStateVm
    {
        public string Title { get; init; } = "Nothing here yet";
        public string Message { get; init; } = "";
        public string Icon { get; init; } = "ri-inbox-line";

        public IReadOnlyList<EmptyStateAction> Actions { get; init; } = [];

        /// <summary>
        /// Convenience for the common case of a single link out.
        /// </summary>
        public string? ActionText
        {
            init
            {
                if (!string.IsNullOrWhiteSpace(value))
                    _singleActionText = value;
            }
        }

        public string? ActionUrl
        {
            init
            {
                if (!string.IsNullOrWhiteSpace(value))
                    _singleActionUrl = value;
            }
        }

        public string ActionIcon { init => _singleActionIcon = value; }

        private readonly string? _singleActionText;
        private readonly string? _singleActionUrl;
        private readonly string _singleActionIcon = "ri-add-line";

        /// <summary>
        /// The declared actions, plus the single-action shorthand if one was used.
        /// </summary>
        public IReadOnlyList<EmptyStateAction> AllActions =>
            Actions.Count > 0
                ? Actions
                : _singleActionText is not null && _singleActionUrl is not null
                    ? [new EmptyStateAction
                        {
                            Text = _singleActionText,
                            Url = _singleActionUrl,
                            Icon = _singleActionIcon,
                            IsPrimary = true
                        }]
                    : [];

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
