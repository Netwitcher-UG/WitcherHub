using WitcherHub.Pages.Models.UI;

namespace WitcherHub.Tests;

/// <summary>
/// One header, and one main action per screen.
///
/// Every page used to build its own header: an h3 on the registers, an h4 on the
/// projects list, mb-24 on some and the spacing scale on others, actions in a bare
/// flex div here and in .wh-actions there. Two pages had no header at all — the
/// services list opened straight into a table, with nothing on screen saying what
/// the table was — because the title was being rendered from inside the table
/// component, which was quietly a second header implementation.
/// </summary>
public class PageHeaderTests
{
    private static DirectoryInfo? RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "WitcherHub", "Pages")))
            directory = directory.Parent;

        return directory;
    }

    private static string PagesPath() =>
        Path.Combine(RepositoryRoot()!.FullName, "WitcherHub", "Pages");

    // ---- the action hierarchy ----------------------------------------------

    [Fact]
    public void Each_action_style_is_drawn_differently_and_only_primary_is_filled()
    {
        var primary = PageHeaderVm.ButtonClass(PageActionStyle.Primary);
        var secondary = PageHeaderVm.ButtonClass(PageActionStyle.Secondary);
        var danger = PageHeaderVm.ButtonClass(PageActionStyle.Danger);

        Assert.Equal(3, new[] { primary, secondary, danger }.Distinct().Count());

        // A filled button is the loudest thing on the page, so exactly one style
        // gets to be one. The others outline.
        Assert.Contains("btn-primary", primary);
        Assert.DoesNotContain("outline", primary);

        Assert.Contains("outline", secondary);
        Assert.Contains("outline", danger);
        Assert.Contains("danger", danger);
    }

    [Fact]
    public void No_page_declares_two_main_actions()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(PagesPath(), "*.cshtml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);

            var primaries = System.Text.RegularExpressions.Regex
                .Matches(text, @"PageActionStyle\.Primary")
                .Count;

            if (primaries > 1)
                offenders.Add($"{Path.GetFileName(file)} ({primaries})");
        }

        Assert.True(
            offenders.Count == 0,
            "A page with two filled primary buttons has told the user nothing about which one to " +
            $"press. Demote all but one to Secondary: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void A_disabled_action_can_say_why()
    {
        // A greyed-out button with no explanation is a dead end: the user cannot
        // tell whether they lack a permission, or a step is missing, or it is a bug.
        var action = new PageAction
        {
            Label = "Send",
            Disabled = true,
            DisabledReason = "Add at least one position first."
        };

        Assert.True(action.Disabled);
        Assert.False(string.IsNullOrWhiteSpace(action.DisabledReason));
    }

    [Fact]
    public void An_action_knows_whether_it_navigates_or_acts()
    {
        Assert.True(new PageAction { Label = "Edit", Page = "/Contracts/Edit" }.IsLink);
        Assert.True(new PageAction { Label = "Docs", Href = "https://example.test" }.IsLink);

        // No destination: it is a button, and script or a modal handles it.
        Assert.False(new PageAction { Label = "Print", Id = "btnPrint" }.IsLink);
        Assert.False(new PageAction { Label = "New", ModalTarget = "FormModal" }.IsLink);
    }

    // ---- the header is actually used ---------------------------------------

    [Fact]
    public void The_table_card_no_longer_renders_a_page_title_or_a_primary_action()
    {
        var tableCard = Path.Combine(PagesPath(), "Shared", "_TableCard.cshtml");

        var text = File.ReadAllText(tableCard);

        // These are what made it a second header implementation.
        Assert.DoesNotContain("<h3", text);
        Assert.DoesNotContain("PrimaryButtonText", text);
        Assert.DoesNotContain("PrimaryButtonTarget", text);
    }

    [Fact]
    public void The_table_card_model_no_longer_carries_header_concerns()
    {
        var vm = Path.Combine(PagesPath(), "Models", "UI", "TableCardVm.cs");

        var text = File.ReadAllText(vm);

        Assert.DoesNotContain("PrimaryButtonText", text);
        Assert.DoesNotContain("PrimaryButtonTarget", text);
    }

    [Fact]
    public void The_list_pages_all_use_the_shared_header()
    {
        // These are the screens a user moves between most, so a difference in
        // heading size or button placement between them is felt immediately.
        var expected = new[]
        {
            "Projects.cshtml",
            "Services.cshtml",
            "Index.cshtml",                       // clients
            Path.Combine("Quotes", "Index.cshtml"),
            Path.Combine("Contracts", "Index.cshtml"),
            Path.Combine("Invoices", "Index.cshtml"),
            Path.Combine("Contracts", "Details.cshtml"),
        };

        var missing = expected
            .Where(relative => !File.ReadAllText(Path.Combine(PagesPath(), relative))
                .Contains("_PageHeader"))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"These pages build their own header instead of using _PageHeader: {string.Join(", ", missing)}");
    }

    [Fact]
    public void No_page_hand_rolls_the_old_header_markup()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(PagesPath(), "*.cshtml", SearchOption.AllDirectories))
        {
            // The layouts and the header partial itself are allowed structural markup.
            var name = Path.GetFileName(file);
            if (name.StartsWith('_')) continue;

            var text = File.ReadAllText(file);

            // The old idiom: a page title as an h3 with the register spacing on it.
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"<h3 class=""mb-4 fw-semibold"""))
                offenders.Add(name);
        }

        Assert.True(
            offenders.Count == 0,
            "These pages still write a page title by hand, so their heading size and spacing drift " +
            $"from every other page: {string.Join(", ", offenders.Distinct())}");
    }

    [Fact]
    public void A_header_needs_nothing_but_a_title()
    {
        // The commonest case has to be the shortest to write, or pages will keep
        // building their own.
        var header = new PageHeaderVm { Title = "Quotes" };

        Assert.Empty(header.Actions);
        Assert.Empty(header.Badges);
        Assert.Empty(header.Context);
        Assert.Null(header.Status);
        Assert.Null(header.Subtitle);
        Assert.Null(header.BackLabel);
    }
}
