namespace WitcherHub.Pages.Models.UI
{
    /// <summary>
    /// How prominent an action is, and therefore how it is drawn.
    ///
    /// Not a colour name. A page says what an action <em>is</em> and the header
    /// decides how it looks, so "the main thing to do here" is drawn the same way
    /// on every screen. Pages previously picked Bootstrap button classes directly,
    /// which is how one screen ended up with three filled primary buttons
    /// competing for attention and another with none at all.
    /// </summary>
    public enum PageActionStyle
    {
        /// <summary>The one thing this page is for. At most one per header.</summary>
        Primary,

        /// <summary>Available, but not what the page is about.</summary>
        Secondary,

        /// <summary>Deletes or discards something. Drawn as a warning.</summary>
        Danger
    }

    /// <summary>
    /// One button or link in a page header.
    ///
    /// Either a navigation (<see cref="Page"/> or <see cref="Href"/>) or a button
    /// (neither, plus an <see cref="Id"/> for script to bind to, or a
    /// <see cref="ModalTarget"/>).
    /// </summary>
    public sealed record PageAction
    {
        public required string Label { get; init; }

        /// <summary>A Remix icon class, e.g. "ri-add-line".</summary>
        public string? Icon { get; init; }

        public PageActionStyle Style { get; init; } = PageActionStyle.Secondary;

        /// <summary>A razor page path, e.g. "/Contracts/Edit".</summary>
        public string? Page { get; init; }

        /// <summary>Route values for <see cref="Page"/>.</summary>
        public IReadOnlyDictionary<string, string?>? RouteValues { get; init; }

        /// <summary>An absolute or app-relative URL, when <see cref="Page"/> does not apply.</summary>
        public string? Href { get; init; }

        /// <summary>The id of a modal this action opens, without the leading '#'.</summary>
        public string? ModalTarget { get; init; }

        /// <summary>An element id, for actions script binds to (print, export).</summary>
        public string? Id { get; init; }

        public bool Disabled { get; init; }

        /// <summary>
        /// Why the action is unavailable. Shown as a tooltip — a disabled button with
        /// no explanation is a dead end.
        /// </summary>
        public string? DisabledReason { get; init; }

        /// <summary>True when this action navigates rather than does something.</summary>
        public bool IsLink => Page is not null || Href is not null;
    }

    /// <summary>
    /// A link shown under a page title, naming the record this one belongs to.
    ///
    /// On a contract that is its customer and its project; the same pair appears on
    /// quotes and invoices. Modelled rather than passed as raw HTML so the labels —
    /// customer names, project titles, all user-entered — are encoded on the way
    /// out, and so every detail page states its context in the same shape.
    /// </summary>
    public sealed record PageLink
    {
        public required string Label { get; init; }
        public string? Page { get; init; }
        public string? Href { get; init; }
        public IReadOnlyDictionary<string, string?>? RouteValues { get; init; }

        /// <summary>Drawn in the accent colour: the primary thing this belongs to.</summary>
        public bool Emphasised { get; init; }
    }

    /// <summary>
    /// The top of a page: where you are, what this is, and what you can do here.
    ///
    /// Every page built its own, and they disagreed — an h3 on the registers and an
    /// h4 on the projects list, mb-24 on some and the spacing scale on others,
    /// actions in a bare flex div here and in .wh-actions there. Two pages had no
    /// header at all and opened straight into a table.
    /// </summary>
    public sealed class PageHeaderVm
    {
        public required string Title { get; init; }

        /// <summary>One line on what this page is for. Optional but usually wanted.</summary>
        public string? Subtitle { get; init; }

        /// <summary>
        /// A status shown beside the title, for pages about one record — a contract,
        /// a quote — rather than a list.
        /// </summary>
        public StatusPresentation? Status { get; init; }

        /// <summary>
        /// Extra badges beside the title: a count needing attention, a signed
        /// marker. Rendered through the shared badge renderer.
        /// </summary>
        public IReadOnlyList<(string Label, string Tone)> Badges { get; init; } = [];

        /// <summary>
        /// What this record belongs to — its customer, its project. Rendered under
        /// the title, separated by dots. Use instead of <see cref="Subtitle"/> on a
        /// page about one record.
        /// </summary>
        public IReadOnlyList<PageLink> Context { get; init; } = [];

        /// <summary>Where "back" goes. Omit on top-level pages.</summary>
        public string? BackLabel { get; init; }

        public string? BackPage { get; init; }

        public IReadOnlyDictionary<string, string?>? BackRouteValues { get; init; }

        public IReadOnlyList<PageAction> Actions { get; init; } = [];

        /// <summary>
        /// The Bootstrap classes for an action's style. One place, so a primary
        /// action is the same shape everywhere.
        /// </summary>
        public static string ButtonClass(PageActionStyle style) => style switch
        {
            PageActionStyle.Primary => "btn btn-primary",
            PageActionStyle.Danger => "btn btn-outline-danger",
            _ => "btn btn-outline-primary"
        };

        /// <summary>
        /// The size and shape every header action shares, appended to the above.
        /// </summary>
        public const string ButtonShape =
            "text-sm px-20 py-11 radius-8 d-inline-flex align-items-center gap-2";
    }
}
