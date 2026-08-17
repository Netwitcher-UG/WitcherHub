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
