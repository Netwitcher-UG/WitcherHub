using WitcherHub.Pages.Models.UI;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Tests;

/// <summary>
/// One design system, enforced rather than hoped for.
///
/// Two badge idioms had grown up side by side: the theme's
/// <c>bg-success-focus text-success-main border …</c> on newer pages and plain
/// Bootstrap <c>bg-success bg-opacity-10 text-success</c> on older ones, so the
/// same green was two different shades depending on which screen you were
/// looking at. Four separate status maps existed — one central, one on the
/// projects list, one on the contract editor, one on the quote details page —
/// and each could drift from the others without anything failing.
///
/// The scan below reads the razor and page-model sources, so a page that
/// reintroduces the old idiom fails here rather than being noticed in a
/// screenshot months later.
/// </summary>
public class DesignSystemConsistencyTests
{
    /// <summary>
    /// Walks up from the test assembly to the repository root. The tests run from
    /// bin/, so the sources are several levels above.
    /// </summary>
    private static DirectoryInfo? RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "WitcherHub", "Pages")))
            directory = directory.Parent;

        return directory;
    }

    private static IReadOnlyList<string> PageSources()
    {
        var root = RepositoryRoot();
        if (root is null) return Array.Empty<string>();

        var pages = Path.Combine(root.FullName, "WitcherHub", "Pages");

        return Directory.EnumerateFiles(pages, "*.cshtml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(pages, "*.cs", SearchOption.AllDirectories))
            .ToList();
    }

    /// <summary>
    /// The page scripts. Rows rendered in the browser are just as visible as rows
    /// rendered on the server, and the first version of this test only read the
    /// razor sources — which is how eighteen badges in the project workspace went
    /// on using the old idiom after the pages had been converted.
    /// </summary>
    private static IReadOnlyList<string> ScriptSources()
    {
        var root = RepositoryRoot();
        if (root is null) return Array.Empty<string>();

        var js = Path.Combine(root.FullName, "WitcherHub", "wwwroot", "js");
        if (!Directory.Exists(js)) return Array.Empty<string>();

        return Directory.EnumerateFiles(js, "*.js", SearchOption.AllDirectories).ToList();
    }

    [Fact]
    public void No_page_uses_the_plain_bootstrap_badge_idiom()
    {
        var sources = PageSources();
        if (sources.Count == 0) return;      // sources not reachable from here

        var offenders = new List<string>();

        foreach (var file in sources)
        {
            var text = File.ReadAllText(file);

            // "bg-<tone> bg-opacity-10" is the Bootstrap idiom. The theme's is
            // "bg-<tone>-focus text-<tone>-main", which this does not match.
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"badge bg-[a-z]+ bg-opacity-10"))
                offenders.Add(Path.GetFileName(file));
        }

        Assert.True(
            offenders.Count == 0,
            "These pages use Bootstrap's badge classes instead of the theme's, so the same status " +
            "appears in a different shade there than everywhere else. Route them through " +
            $"Badge.Html or DocumentStatusPresentation: {string.Join(", ", offenders.Distinct())}");
    }

    [Fact]
    public void No_page_defines_its_own_project_status_colours()
    {
        var sources = PageSources();
        if (sources.Count == 0) return;

        var offenders = new List<string>();

        foreach (var file in sources)
        {
            // The one place allowed to map a status to a colour.
            if (Path.GetFileName(file) == "StatusPresentation.cs") continue;

            var text = File.ReadAllText(file);

            // A page mapping ProjectStatus members straight to css classes is
            // building a second vocabulary.
            if (System.Text.RegularExpressions.Regex.IsMatch(
                    text, @"ProjectStatus\.\w+\s*=>\s*\(?""(badge|bg-)"))
                offenders.Add(Path.GetFileName(file));
        }

        Assert.True(
            offenders.Count == 0,
            "These pages map project statuses to their own colours rather than using " +
            $"DocumentStatusPresentation.ForProject: {string.Join(", ", offenders.Distinct())}");
    }

    [Fact]
    public void No_script_builds_its_own_badge_markup()
    {
        var sources = ScriptSources();
        if (sources.Count == 0) return;

        var offenders = new List<string>();

        foreach (var file in sources)
        {
            // ui-kit.js is the one place allowed to name badge classes: it is the
            // shared renderer the others call.
            if (Path.GetFileName(file) == "ui-kit.js") continue;

            var text = File.ReadAllText(file);

            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"badge bg-[a-z]+ bg-opacity-10")
                || System.Text.RegularExpressions.Regex.IsMatch(text, @"badge bg-[a-z]+-focus"))
                offenders.Add(Path.GetFileName(file));
        }

        Assert.True(
            offenders.Count == 0,
            "These scripts build badge markup by hand, so the rows they render can drift from the " +
            "rows the server renders in both colour and wording. Call UI.badge.status / UI.badge.html " +
            $"instead: {string.Join(", ", offenders.Distinct())}");
    }

    [Fact]
    public void No_page_keeps_a_private_stylesheet_inline()
    {
        var root = RepositoryRoot();
        if (root is null) return;

        var pages = Path.Combine(root.FullName, "WitcherHub", "Pages");

        var offenders = Directory
            .EnumerateFiles(pages, "*.cshtml", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("<style", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "A <style> block on a page is a design system of one: it cannot be reused, it overrides " +
            "the shared rules invisibly, and two pages that need the same thing end up with two " +
            "slightly different copies of it. Move the rules into css/site.css or a dedicated " +
            $"stylesheet: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void The_projects_list_no_longer_forces_horizontal_scrolling()
    {
        var root = RepositoryRoot();
        if (root is null) return;

        var css = Path.Combine(root.FullName, "WitcherHub", "wwwroot", "css", "site.css");
        if (!File.Exists(css)) return;

        var text = File.ReadAllText(css);

        // The projects table used to be declared wider than most desktop windows
        // before a single column had been read.
        Assert.DoesNotContain("--wh-table-min-width", text.Replace(" ", ""));
    }

    /// <summary>
    /// The client-side vocabulary must say the same thing as the server-side one.
    ///
    /// UI.badge exists because some rows are rendered in the browser, but a second
    /// copy of a map is a second chance to disagree — which is what the scripts it
    /// replaced actually did: they called a sent quote "Sent" while every
    /// server-rendered list called the same quote "Awaiting customer".
    /// </summary>
    [Fact]
    public void The_client_side_status_vocabulary_matches_the_server_side_one()
    {
        var root = RepositoryRoot();
        if (root is null) return;

        var uiKit = Path.Combine(root.FullName, "WitcherHub", "wwwroot", "js", "ui-kit.js");
        if (!File.Exists(uiKit)) return;

        var text = File.ReadAllText(uiKit);

        var kinds = new (string Kind, Func<string, StatusPresentation?> Lookup)[]
        {
            ("quote",    name => Parse<DocumentStatus>(name) is { } s ? DocumentStatusPresentation.ForQuote(s) : null),
            ("contract", name => Parse<DocumentStatus>(name) is { } s ? DocumentStatusPresentation.ForContract(s) : null),
            ("invoice",  name => Parse<DocumentStatus>(name) is { } s ? DocumentStatusPresentation.ForInvoice(s) : null),
            ("project",  name => Parse<ProjectStatus>(name) is { } s ? DocumentStatusPresentation.ForProject(s) : null),
        };

        var problems = new List<string>();
        var compared = 0;

        foreach (var (kind, lookup) in kinds)
        {
            var block = MapBlock(text, kind);

            Assert.False(
                string.IsNullOrWhiteSpace(block),
                $"UI.badge has no '{kind}' status map, so the browser cannot word a {kind} " +
                "the way the server does.");

            // status: ['Label', 'tone']
            var entries = System.Text.RegularExpressions.Regex.Matches(
                block!, @"(\w+):\s*\['([^']*)',\s*'(\w+)'\]");

            Assert.NotEmpty(entries);

            foreach (System.Text.RegularExpressions.Match entry in entries)
            {
                var statusName = entry.Groups[1].Value;
                var jsLabel = entry.Groups[2].Value;
                var jsTone = entry.Groups[3].Value;

                var server = lookup(statusName);

                if (server is null)
                {
                    problems.Add($"{kind}.{statusName} is not a status the backend has");
                    continue;
                }

                compared++;

                if (server.Label != jsLabel)
                    problems.Add($"{kind}.{statusName}: server says \"{server.Label}\", script says \"{jsLabel}\"");

                if (server.Tone != jsTone)
                    problems.Add($"{kind}.{statusName}: server tone '{server.Tone}', script tone '{jsTone}'");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(compared >= 20, $"only {compared} statuses compared — the map parse is probably broken");
    }

    /// <summary>Pulls one <c>kind: { … }</c> block out of the STATUS_MAPS literal.</summary>
    private static string? MapBlock(string text, string kind)
    {
        var marker = $"{kind}: {{";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;

        var end = text.IndexOf('}', start);
        return end < 0 ? null : text[start..end];
    }

    private static TEnum? Parse<TEnum>(string name) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(name, ignoreCase: true, out var value) ? value : null;

    // ---- the central map itself -------------------------------------------

    [Fact]
    public void Every_project_status_has_a_label_and_a_tone()
    {
        foreach (var status in Enum.GetValues<ProjectStatus>())
        {
            var presentation = DocumentStatusPresentation.ForProject(status);

            Assert.False(string.IsNullOrWhiteSpace(presentation.Label));
            Assert.False(string.IsNullOrWhiteSpace(presentation.Tone));
            Assert.False(string.IsNullOrWhiteSpace(presentation.BadgeClass));
        }
    }

    [Fact]
    public void The_retired_Waiting_status_is_gone_from_the_vocabulary()
    {
        // Waiting described a document, not a project, and using it as a project
        // status is what made two screens disagree and blocked deletion.
        Assert.DoesNotContain("Waiting", Enum.GetNames<ProjectStatus>());

        Assert.Contains("OnHold", Enum.GetNames<ProjectStatus>());
        Assert.Equal("On hold", DocumentStatusPresentation.ForProject(ProjectStatus.OnHold).Label);
    }

    [Fact]
    public void The_shared_badge_renderer_produces_the_themes_markup()
    {
        var html = Badge.Html("Active", "success").ToString()!;

        Assert.Contains("bg-success-focus", html);
        Assert.Contains("text-success-main", html);
        Assert.DoesNotContain("bg-opacity-10", html);
    }

    [Fact]
    public void The_shared_badge_renderer_escapes_its_label()
    {
        var html = Badge.Html("<script>alert(1)</script>").ToString()!;

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void An_unknown_tone_falls_back_to_neutral_rather_than_nothing()
    {
        var html = Badge.Html("Something", "chartreuse").ToString()!;

        Assert.Contains("bg-neutral-200", html);
    }
}
