using System.Globalization;
using WitcherHub.Pages.Models.UI;

namespace WitcherHub.Tests;

/// <summary>
/// One shape for a figure, whatever the request culture happens to be.
///
/// The application serves "en" by default and can switch to "de", and a good
/// number of pages formatted their own amounts with <c>ToString("0.00")</c> —
/// which follows the current culture and has no group separator. So a German
/// invoice line rendered as "1234.50" while a total on the same screen, produced
/// by Format.Money, rendered as "1.234,50 €". Switching the interface language
/// changed the figures under the user.
///
/// These tests pin the output rather than the implementation: they set a hostile
/// culture first, because that is the condition under which the old code was
/// wrong.
/// </summary>
public class MoneyAndDateFormattingTests
{
    /// <summary>
    /// Runs an assertion with the ambient culture forced to something that formats
    /// numbers differently from German, restoring it afterwards.
    /// </summary>
    private static void InCulture(string name, Action assert)
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            assert();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("")]              // invariant
    public void Money_reads_the_same_whatever_culture_serves_the_request(string culture)
    {
        InCulture(culture, () =>
        {
            Assert.Equal("1.234,50 €", Format.Money(1234.50m));
            Assert.Equal("1.234,50", Format.Amount(1234.50m));
            // Half rounds up, not to even — see MoneyCompact.
            Assert.Equal("1.235 €", Format.MoneyCompact(1234.50m));
            Assert.Equal("1.236 €", Format.MoneyCompact(1235.50m));
        });
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void Quantities_and_dates_read_the_same_too(string culture)
    {
        InCulture(culture, () =>
        {
            Assert.Equal("2", Format.Quantity(2m));
            Assert.Equal("2,5", Format.Quantity(2.5m));
            Assert.Equal("31.12.2025", Format.Date(new DateOnly(2025, 12, 31)));
        });
    }

    [Fact]
    public void An_amount_carries_its_cents_even_when_they_are_zero()
    {
        // A price column that renders 1500 next to 1.234,50 is a column the eye
        // cannot add up.
        Assert.Equal("1.500,00", Format.Amount(1500m));
        Assert.Equal("0,00", Format.Amount(0m));
    }

    [Fact]
    public void A_missing_amount_is_a_dash_rather_than_a_zero()
    {
        // Nothing recorded and nothing owed are different facts, and showing
        // "0,00 €" for the first is how a contract with no agreed value came to
        // look like a contract worth nothing.
        Assert.Equal("—", Format.Amount(null));
        Assert.Equal("—", Format.Date((DateOnly?)null));
    }

    [Fact]
    public void The_currency_symbol_follows_the_currency()
    {
        Assert.Equal("€", Format.Symbol("EUR"));
        Assert.Equal("$", Format.Symbol("USD"));
        Assert.Equal("€", Format.Symbol(null));          // the house currency

        // An unknown code is shown as the code, not dropped: better a reader sees
        // "100,00 SEK" than an amount whose currency is a mystery.
        Assert.Equal("SEK", Format.Symbol("sek"));
    }

    /// <summary>
    /// A date input is the one place the ISO form is correct, and the one place the
    /// German form actively breaks: a browser given "31.12.2025" for
    /// <c>&lt;input type="date"&gt;</c> discards it and shows an empty field, so a
    /// saved date vanishes when the form is reopened.
    /// </summary>
    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void A_date_input_gets_the_ISO_form_and_an_empty_string_when_unset(string culture)
    {
        InCulture(culture, () =>
        {
            Assert.Equal("2025-12-31", Format.DateInput(new DateOnly(2025, 12, 31)));

            Assert.Equal(
                "2025-12-31",
                Format.DateInput(new DateTimeOffset(2025, 12, 31, 9, 30, 0, TimeSpan.Zero)));

            // Not "—": that is not a value a date input can hold.
            Assert.Equal("", Format.DateInput((DateOnly?)null));
            Assert.Equal("", Format.DateInput((DateTimeOffset?)null));
        });
    }

    [Fact]
    public void Overdue_is_only_reported_when_something_is_actually_late()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.Null(Format.DaysOverdue(null));
        Assert.Null(Format.DaysOverdue(today));
        Assert.Null(Format.DaysOverdue(today.AddDays(5)));
        Assert.Equal(3, Format.DaysOverdue(today.AddDays(-3)));
    }

    // ---- and no page goes around it ---------------------------------------

    private static DirectoryInfo? RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "WitcherHub", "Pages")))
            directory = directory.Parent;

        return directory;
    }

    [Fact]
    public void No_page_formats_money_or_quantities_by_hand()
    {
        var root = RepositoryRoot();
        if (root is null) return;

        var pages = Path.Combine(root.FullName, "WitcherHub", "Pages");

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(pages, "*.cshtml", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(pages, "*.cs", SearchOption.AllDirectories)))
        {
            // Format.cs describes the old idiom in its own comments.
            if (Path.GetFileName(file) == "Format.cs") continue;

            var text = File.ReadAllText(file);

            // ToString("0.00") / ("0.##") follow the ambient culture. An explicit
            // InvariantCulture argument is a different thing — that is a
            // machine-readable data- attribute, which is correct.
            var matches = System.Text.RegularExpressions.Regex.Matches(
                text, @"ToString\(""0\.(?:00|##)""\s*\)");

            if (matches.Count > 0)
                offenders.Add($"{Path.GetFileName(file)} ({matches.Count})");
        }

        Assert.True(
            offenders.Count == 0,
            "These files format amounts in the request culture rather than through Format, so the " +
            "same figure appears in two shapes depending on which page shows it and which language " +
            $"the user picked. Use Format.Money / Format.Amount / Format.Quantity: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void The_shared_helpers_are_available_to_every_page()
    {
        var root = RepositoryRoot();
        if (root is null) return;

        var viewImports = Path.Combine(root.FullName, "WitcherHub", "Pages", "_ViewImports.cshtml");
        if (!File.Exists(viewImports)) return;

        // Without this, a page has to remember to import the namespace before it can
        // use Format — and the pages that formatted amounts by hand were, without
        // exception, the pages that had not imported it.
        Assert.Contains("WitcherHub.Pages.Models.UI", File.ReadAllText(viewImports));
    }
}
