using System.Text.RegularExpressions;

namespace WitcherHub.Tests;

/// <summary>
/// Checks that the markup and the files on disk agree about the theme.
///
/// Written after two separate incidents. In the first, every stylesheet on the
/// site 404'd for a week and nothing failed except the way the pages looked. In
/// the second, a theme swap left half the pages referencing an icon font that had
/// been deleted. Neither is visible to a unit test of any page model, and both are
/// caught by reading the markup and checking the paths resolve.
/// </summary>
public class ThemeAssetIntegrityTests
{
    private static readonly string WebRoot = LocateWebProject();

    private static string LocateWebProject()
    {
        // Walk up from the test binaries to the repository root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WitcherHub.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "WitcherHub");
    }

    private static IEnumerable<string> RazorFiles() =>
        Directory.EnumerateFiles(Path.Combine(WebRoot, "Pages"), "*.cshtml", SearchOption.AllDirectories);

    private static IEnumerable<string> ScriptFiles() =>
        Directory.EnumerateFiles(Path.Combine(WebRoot, "wwwroot", "js"), "*.js", SearchOption.AllDirectories);

    /// <summary>
    /// Pulls every <c>~/…</c> asset path out of href/src attributes.
    /// </summary>
    private static readonly Regex AppRelativeAsset =
        new(@"(?:href|src)\s*=\s*""(?<path>~/[^""]+)""", RegexOptions.Compiled);

    [Fact]
    public void Every_asset_referenced_by_a_page_exists_on_disk()
    {
        var missing = new List<string>();

        foreach (var file in RazorFiles())
        {
            foreach (var match in AppRelativeAsset.Matches(File.ReadAllText(file)).Cast<Match>())
            {
                var reference = match.Groups["path"].Value;

                // Razor expressions and cache-busting query strings are not paths.
                if (reference.Contains('@') || reference.Contains("${"))
                    continue;

                var relative = reference[2..].Split('?')[0];
                var onDisk = Path.Combine(WebRoot, "wwwroot", relative.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(onDisk))
                    missing.Add($"{Path.GetFileName(file)} → {reference}");
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void The_previous_theme_is_gone_completely()
    {
        // A half-removed theme is the worst of both: the old files still ship, and
        // the pages that still point at them look different from the rest.
        Assert.False(
            Directory.Exists(Path.Combine(WebRoot, "wwwroot", "theme")),
            "wwwroot/theme still exists; the replaced theme's assets are still being shipped.");

        var leftovers = new List<string>();

        foreach (var file in RazorFiles().Concat(ScriptFiles()))
        {
            var text = File.ReadAllText(file);
            var name = Path.GetFileName(file);

            if (text.Contains("~/theme/", StringComparison.OrdinalIgnoreCase))
                leftovers.Add($"{name}: references the removed ~/theme/ folder");

            if (text.Contains("material-icons", StringComparison.OrdinalIgnoreCase))
                leftovers.Add($"{name}: uses Material Icons, which is no longer loaded");

            if (Regex.IsMatch(text, @"\bbtn-grd\b|\bbtn-grd-\w+"))
                leftovers.Add($"{name}: uses btn-grd, a class the current theme does not define");

            if (Regex.IsMatch(text, @"\bbg-grd-\w+"))
                leftovers.Add($"{name}: uses bg-grd-*, a class the current theme does not define");

            if (text.Contains("data-bs-theme", StringComparison.OrdinalIgnoreCase))
                leftovers.Add($"{name}: switches themes with data-bs-theme; this theme uses data-theme");
        }

        Assert.Empty(leftovers);
    }

    [Fact]
    public void No_page_relies_on_bootstraps_near_white_text_class()
    {
        // text-light was readable only because the replaced theme was dark. On a
        // light background it is invisible, which is how a whole form of labels
        // disappeared during the swap.
        var offenders = RazorFiles()
            .Concat(ScriptFiles())
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\btext-light\b"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Every_remix_icon_used_in_markup_is_defined_by_the_icon_font()
    {
        var css = File.ReadAllText(
            Path.Combine(WebRoot, "wwwroot", "wowdash", "css", "remixicon.css"));

        var defined = Regex.Matches(css, @"^\.(?<name>ri-[a-z0-9-]+):before", RegexOptions.Multiline)
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(defined);

        var unknown = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in RazorFiles().Concat(ScriptFiles()))
        {
            foreach (var match in Regex.Matches(File.ReadAllText(file), @"\bri-[a-z0-9-]+\b").Cast<Match>())
            {
                var name = match.Value;

                // Utility classes the theme defines itself, not glyphs.
                if (name is "ri-circle-fill")
                    continue;

                if (!defined.Contains(name))
                    unknown.Add($"{Path.GetFileName(file)}: {name}");
            }
        }

        Assert.Empty(unknown);
    }

    [Fact]
    public void Every_layout_loads_the_theme_through_the_shared_partials()
    {
        // Three layouts wired their own stylesheet lists before, and had already
        // drifted apart. Anything that renders a page must go through the partials.
        var layouts = new[] { "_Layout.cshtml", "_AuthLayout.cshtml", "_ContractsLayout.cshtml" };

        foreach (var layout in layouts)
        {
            var text = File.ReadAllText(Path.Combine(WebRoot, "Pages", "Shared", layout));

            Assert.Contains("_ThemeHead", text);
            Assert.Contains("_ThemeScripts", text);
        }
    }
}
