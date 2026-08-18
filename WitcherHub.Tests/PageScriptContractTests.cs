using System.Text.RegularExpressions;

namespace WitcherHub.Tests;

/// <summary>
/// A page and its script have a contract: the ids the script writes to must exist
/// in the markup.
///
/// Nothing enforced it, and rewriting a page's header broke it. The project
/// workspace script wrote to two elements the redesigned header no longer had, so
/// rendering threw a TypeError partway through — and because the render shared a
/// try block with the load, the page reported "Failed to load project." for a
/// project that had loaded correctly. The panel never appeared, which looked from
/// the outside like projects and contracts could not be opened at all.
///
/// A compiler catches this class of mistake in C#. In a razor page and a separate
/// script file, nothing does — so this test does.
/// </summary>
public class PageScriptContractTests
{
    private static DirectoryInfo? RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "WitcherHub", "Pages")))
            directory = directory.Parent;

        return directory;
    }

    /// <summary>
    /// Page scripts paired with the pages that host them. Adding a pair here brings
    /// that page under the same guarantee.
    /// </summary>
    public static TheoryData<string, string> Pairings => new()
    {
        { Path.Combine("js", "pages", "projects", "workspace.js"), Path.Combine("Projects", "Workspace.cshtml") },
        { Path.Combine("js", "pages", "contracts", "positions-builder.js"), Path.Combine("Contracts", "Positions.cshtml") },
    };

    [Theory]
    [MemberData(nameof(Pairings))]
    public void A_script_never_writes_to_an_element_its_page_does_not_have(string script, string page)
    {
        var root = RepositoryRoot();
        if (root is null) return;

        var scriptPath = Path.Combine(root.FullName, "WitcherHub", "wwwroot", script);
        var pagePath = Path.Combine(root.FullName, "WitcherHub", "Pages", page);

        if (!File.Exists(scriptPath) || !File.Exists(pagePath)) return;

        var js = File.ReadAllText(scriptPath);
        var html = File.ReadAllText(pagePath);

        var present = Regex.Matches(html, @"id=""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        // Ids the page builds at runtime rather than declaring, e.g. "row-@i".
        var templated = Regex.Matches(html, @"id=""([^""]*)@")
            .Select(m => m.Groups[1].Value)
            .Where(prefix => prefix.Length > 0)
            .ToList();

        var offenders = new List<string>();

        // An unguarded write: $('x').textContent = …, .innerHTML = …, .value = …
        // Reads and optional-chained access degrade quietly and are fine.
        foreach (Match match in Regex.Matches(
                     js, @"\$\(\s*['""]([^'""]+)['""]\s*\)\s*\.\s*(textContent|innerHTML|value|href|checked)\s*="))
        {
            var id = match.Groups[1].Value;

            if (present.Contains(id)) continue;
            if (templated.Any(prefix => id.StartsWith(prefix, StringComparison.Ordinal))) continue;

            offenders.Add($"{id} (.{match.Groups[2].Value})");
        }

        Assert.True(
            offenders.Count == 0,
            $"{Path.GetFileName(script)} writes to elements that {Path.GetFileName(page)} does not contain, which " +
            "throws partway through rendering and takes the rest of the page with it. Either restore the element " +
            $"or guard the write: {string.Join(", ", offenders.Distinct())}");
    }

    /// <summary>
    /// The workspace panel's two failure modes must stay distinguishable. Sharing
    /// one message is what sent this bug's diagnosis in the wrong direction.
    /// </summary>
    [Fact]
    public void The_workspace_reports_a_failed_load_differently_from_a_failed_render()
    {
        var root = RepositoryRoot();
        if (root is null) return;

        var script = Path.Combine(
            root.FullName, "WitcherHub", "wwwroot", "js", "pages", "projects", "workspace.js");

        if (!File.Exists(script)) return;

        var js = File.ReadAllText(script);

        Assert.Contains("could not be loaded", js);
        Assert.Contains("could not be displayed", js);
    }
}
