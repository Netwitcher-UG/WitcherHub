using System.Text.RegularExpressions;

namespace WitcherHub.Tests;

/// <summary>
/// The spacing system, checked against the stylesheet that defines it.
///
/// Spacing had been done per page with whatever Bootstrap utility looked right
/// at the time, so no two screens shared a rhythm and nothing could be corrected
/// centrally. These tests are cheap and they catch the two ways that comes back:
/// the scale losing a step, and a page re-inventing the frame.
/// </summary>
public class SpacingSystemTests
{
    private static readonly string WebRoot = LocateWebProject();

    private static string LocateWebProject()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WitcherHub.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "WitcherHub");
    }

    private static string SiteCss() =>
        File.ReadAllText(Path.Combine(WebRoot, "wwwroot", "css", "site.css"));

    private static IEnumerable<string> RazorFiles() =>
        Directory.EnumerateFiles(Path.Combine(WebRoot, "Pages"), "*.cshtml", SearchOption.AllDirectories);

    [Fact]
    public void The_spacing_scale_is_complete()
    {
        // 4, 8, 12, 16, 24, 32, 48. A missing step is a step a page will replace
        // with a hard-coded margin.
        var css = SiteCss();

        var expected = new[]
        {
            ("--wh-space-1", "0.25rem"),
            ("--wh-space-2", "0.5rem"),
            ("--wh-space-3", "0.75rem"),
            ("--wh-space-4", "1rem"),
            ("--wh-space-5", "1.5rem"),
            ("--wh-space-6", "2rem"),
            ("--wh-space-7", "3rem")
        };

        foreach (var (token, value) in expected)
            Assert.Contains($"{token}: {value}", css);
    }

    [Fact]
    public void Content_has_a_maximum_width()
    {
        // Without one, every form field stretched the full span of a wide monitor
        // and a page of four inputs read as controls marooned in grey.
        var css = SiteCss();

        Assert.Contains("--wh-content-max", css);
        Assert.Matches(@"\.dashboard-main-body\s*\{[^}]*max-width:\s*var\(--wh-content-max\)", css);
        Assert.Matches(@"\.dashboard-main-body\s*\{[^}]*margin-inline:\s*auto", css);
    }

    [Fact]
    public void The_page_frame_is_defined_once_in_the_stylesheet()
    {
        // A page that sets its own padding on .dashboard-main-body is a page that
        // will drift from the others the next time the frame changes.
        var offenders = RazorFiles()
            .Where(f => File.ReadAllText(f).Contains("dashboard-main-body"))
            .Select(Path.GetFileName)
            .Where(name => name != "_Layout.cshtml")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Field_width_helpers_exist_so_short_values_get_short_fields()
    {
        // A date input 900px wide tells the reader the date might be 900px long.
        var css = SiteCss();

        foreach (var helper in new[] { ".wh-field-xs", ".wh-field-sm", ".wh-field-md", ".wh-field-lg" })
            Assert.Contains(helper, css);
    }

    [Fact]
    public void The_form_grid_collapses_to_one_column_on_a_phone()
    {
        var css = SiteCss();

        // Every column class starts at span 12 and only narrows at a breakpoint,
        // so the mobile layout is the default rather than an afterthought.
        foreach (var column in new[] { "wh-col-half", "wh-col-third", "wh-col-quarter" })
            Assert.Matches($@"\.wh-form-grid > \.{column} \{{ grid-column: span 12; \}}", css);

        Assert.Contains("@media (min-width: 576px)", css);
        Assert.Contains("@media (min-width: 992px)", css);
    }

    [Fact]
    public void Nothing_in_the_application_sets_a_minimum_height_in_viewport_units()
    {
        // min-height: 100vh on a content element is how a short page acquires a
        // screenful of blank space below it.
        var css = SiteCss();

        var offenders = Regex.Matches(css, @"min-height:\s*\d+vh")
            .Select(m => m.Value)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Empty_states_do_not_reserve_a_screenful()
    {
        // py-48 on a card holding nothing produced a 300px void.
        var partial = File.ReadAllText(Path.Combine(WebRoot, "Pages", "Shared", "_EmptyState.cshtml"));

        Assert.Contains("wh-empty", partial);
        Assert.DoesNotContain("py-48", partial);
    }
}
