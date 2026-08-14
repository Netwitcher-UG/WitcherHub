using WitcherHub.Application.Models.View.Overview;
using WitcherHub.Application.Models.View.Registers;
using WitcherHub.Pages.Models.UI;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Tests;

/// <summary>
/// The rules behind what the dashboard and the registers put on the screen.
///
/// These are pure and need no database, which matters because they encode
/// judgements that are easy to get subtly wrong: a percentage divided by zero, a
/// day count worded in the wrong direction, an amount formatted with the wrong
/// separators for a German invoice.
/// </summary>
public class BusinessPresentationTests
{
    /// <summary>The icon the unmapped-status arm returns.</summary>
    private const string FallbackIcon = "ri-file-list-3-line";

    // =====================================================================
    // Money and dates
    // =====================================================================

    [Fact]
    public void Money_uses_german_separators_regardless_of_the_thread_culture()
    {
        // These are German business documents; the figure on the screen has to
        // match the figure on the invoice whatever culture the request ran under.
        var previous = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");

            Assert.Equal("1.234,50 €", Format.Money(1234.5m));
            Assert.Equal("0,00 €", Format.Money(0m));
            Assert.Equal("-99,99 €", Format.Money(-99.99m));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Compact_money_drops_the_cents_for_headline_figures()
    {
        Assert.Equal("1.235 €", Format.MoneyCompact(1234.56m));
        Assert.Equal("0 €", Format.MoneyCompact(0m));
    }

    [Theory]
    [InlineData("EUR", "€")]
    [InlineData("eur", "€")]
    [InlineData("USD", "$")]
    [InlineData("GBP", "£")]
    [InlineData("CHF", "CHF")]
    [InlineData("SEK", "SEK")]
    public void Currency_symbols_fall_back_to_the_code(string currency, string expected)
    {
        Assert.Equal(expected, Format.Symbol(currency));
    }

    [Fact]
    public void A_missing_date_reads_as_a_dash_rather_than_a_default_date()
    {
        // DateOnly's default renders as 01.01.0001, which looks like real data.
        Assert.Equal("—", Format.Date((DateOnly?)null));
        Assert.Equal("—", Format.Date((DateTimeOffset?)null));
        Assert.Equal("—", Format.Relative((DateOnly?)null));
    }

    [Fact]
    public void Relative_dates_read_in_the_right_direction()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.Equal("today", Format.Relative(today));
        Assert.Equal("tomorrow", Format.Relative(today.AddDays(1)));
        Assert.Equal("yesterday", Format.Relative(today.AddDays(-1)));
        Assert.Equal("in 9 days", Format.Relative(today.AddDays(9)));
        Assert.Equal("9 days ago", Format.Relative(today.AddDays(-9)));
    }

    [Fact]
    public void Days_overdue_is_null_until_the_date_has_actually_passed()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.Null(Format.DaysOverdue(null));
        Assert.Null(Format.DaysOverdue(today));            // due today is not late
        Assert.Null(Format.DaysOverdue(today.AddDays(3)));
        Assert.Equal(4, Format.DaysOverdue(today.AddDays(-4)));
    }

    // =====================================================================
    // Status vocabulary
    // =====================================================================

    [Fact]
    public void Every_status_a_quote_can_hold_is_worded_deliberately()
    {
        // A status that falls through to the raw enum name is a status nobody
        // wrote wording for. These are the ones a quote can actually reach.
        DocumentStatus[] reachable =
        [
            DocumentStatus.Draft, DocumentStatus.Sent, DocumentStatus.Accepted,
            DocumentStatus.Signed, DocumentStatus.Rejected, DocumentStatus.Cancelled,
            DocumentStatus.Void
        ];

        foreach (var status in reachable)
        {
            var presentation = DocumentStatusPresentation.ForQuote(status);

            // The fallback arm hands back the bare enum name and a generic
            // document icon. No status a quote can reach should land there — and
            // the icon is what identifies the fallback, because several
            // deliberate labels legitimately read the same as the enum name.
            Assert.NotEqual(FallbackIcon, presentation.Icon);
            Assert.StartsWith("ri-", presentation.Icon);
            Assert.False(string.IsNullOrWhiteSpace(presentation.Label));
            Assert.False(string.IsNullOrWhiteSpace(presentation.BadgeClass));
        }
    }

    [Fact]
    public void Every_status_an_invoice_can_hold_is_worded_deliberately()
    {
        DocumentStatus[] reachable =
        [
            DocumentStatus.Draft, DocumentStatus.Issued, DocumentStatus.Sent,
            DocumentStatus.Open, DocumentStatus.Overdue, DocumentStatus.Paid,
            DocumentStatus.Cancelled, DocumentStatus.Void
        ];

        foreach (var status in reachable)
        {
            var presentation = DocumentStatusPresentation.ForInvoice(status);

            Assert.NotEqual(FallbackIcon, presentation.Icon);
            Assert.StartsWith("ri-", presentation.Icon);
            Assert.False(string.IsNullOrWhiteSpace(presentation.Label));
        }
    }

    [Fact]
    public void The_same_status_is_worded_for_the_document_it_is_on()
    {
        // DocumentStatus is shared, but "Sent" on a quote is waiting on a
        // decision while on a contract it is waiting on a signature. Rendering
        // one word for both is how a screen ends up saying nothing useful.
        Assert.NotEqual(
            DocumentStatusPresentation.ForQuote(DocumentStatus.Sent).Label,
            DocumentStatusPresentation.ForContract(DocumentStatus.Sent).Label);
    }

    [Fact]
    public void An_unmapped_status_still_renders_something_readable()
    {
        // Terminated is not reachable on a quote, but must not blow up if it
        // somehow appears in the data.
        var presentation = DocumentStatusPresentation.ForQuote(DocumentStatus.Terminated);

        Assert.Equal(FallbackIcon, presentation.Icon);
        Assert.Equal("Terminated", presentation.Label);
        Assert.False(string.IsNullOrWhiteSpace(presentation.BadgeClass));
    }

    // =====================================================================
    // Dashboard arithmetic
    // =====================================================================

    [Fact]
    public void A_month_on_month_change_against_zero_is_no_change_at_all()
    {
        // Dividing by a zero baseline produces either an exception or a number
        // like 10000%, both of which are lies. Null means "nothing to compare".
        var summary = new MoneySummary { CollectedThisMonth = 500m, CollectedLastMonth = 0m };

        Assert.Null(summary.CollectedChangePercent);
    }

    [Theory]
    [InlineData(1500, 1000, 50)]
    [InlineData(500, 1000, -50)]
    [InlineData(1000, 1000, 0)]
    public void A_month_on_month_change_is_a_percentage_of_last_month(
        decimal thisMonth, decimal lastMonth, decimal expected)
    {
        var summary = new MoneySummary
        {
            CollectedThisMonth = thisMonth,
            CollectedLastMonth = lastMonth
        };

        Assert.Equal(expected, summary.CollectedChangePercent);
    }

    [Fact]
    public void A_business_with_no_documents_is_reported_as_empty()
    {
        var overview = new BusinessOverview
        {
            Money = new MoneySummary(),
            Pipeline = new PipelineSummary()
        };

        Assert.True(overview.IsEmpty);
    }

    [Fact]
    public void A_business_with_one_quote_is_not_empty()
    {
        var overview = new BusinessOverview
        {
            Money = new MoneySummary(),
            Pipeline = new PipelineSummary { QuoteCount = 1 }
        };

        Assert.False(overview.IsEmpty);
    }

    // =====================================================================
    // Attention list wording
    // =====================================================================

    private static AttentionListVm ListWith(string dateNoun, int daysElapsed) => new()
    {
        Title = "Test",
        Kind = AttentionKind.Invoice,
        LinkPage = "/Invoices/Details",
        DateNoun = dateNoun,
        Items =
        [
            new AttentionItem
            {
                Id = Guid.NewGuid(),
                Date = DateOnly.FromDateTime(DateTime.UtcNow),
                DaysElapsed = daysElapsed
            }
        ]
    };

    [Fact]
    public void The_same_day_count_is_worded_opposite_ways_for_late_and_upcoming()
    {
        // 12 days is "12 days late" for an overdue invoice and "in 12 days" for a
        // contract about to end. The number alone carries no direction.
        var late = ListWith("late", 12);
        var ending = ListWith("ends", -12);

        Assert.Equal("12 days late", late.DescribeAge(late.Items[0]));
        Assert.Equal("in 12 days", ending.DescribeAge(ending.Items[0]));
    }

    [Fact]
    public void Due_today_is_not_described_as_late()
    {
        var list = ListWith("late", 0);
        Assert.Equal("due today", list.DescribeAge(list.Items[0]));
    }

    [Fact]
    public void A_quote_sent_yesterday_is_not_pluralised()
    {
        var list = ListWith("sent", 1);
        Assert.Equal("sent yesterday", list.DescribeAge(list.Items[0]));
    }

    [Fact]
    public void Only_things_left_a_fortnight_are_flagged_as_urgent()
    {
        Assert.False(ListWith("late", 3).IsUrgent(ListWith("late", 3).Items[0]));
        Assert.True(ListWith("late", 14).IsUrgent(ListWith("late", 14).Items[0]));

        // Something in the future is never urgent, however far out it is.
        var ending = ListWith("ends", 40);
        Assert.False(ending.IsUrgent(ending.Items[0]));
    }

    [Fact]
    public void An_item_with_no_date_is_not_described_with_a_day_count()
    {
        var list = new AttentionListVm
        {
            Title = "Test",
            Kind = AttentionKind.Quote,
            LinkPage = "/Quotes/Details",
            DateNoun = "sent",
            Items = [new AttentionItem { Id = Guid.NewGuid() }]
        };

        Assert.Equal("—", list.DescribeAge(list.Items[0]));
    }

    // =====================================================================
    // Register filter state
    // =====================================================================

    [Fact]
    public void An_untouched_filter_reports_itself_as_unapplied()
    {
        // The distinction drives which empty state is shown: "nothing here yet"
        // versus "nothing matches, clear the filter".
        Assert.False(new RegisterFilter().HasAnyFilter);
    }

    [Fact]
    public void A_blank_search_string_does_not_count_as_a_filter()
    {
        Assert.False(new RegisterFilter { Search = "   " }.HasAnyFilter);
    }

    [Fact]
    public void Each_filter_field_on_its_own_counts_as_applied()
    {
        Assert.True(new RegisterFilter { Search = "abc" }.HasAnyFilter);
        Assert.True(new RegisterFilter { Status = DocumentStatus.Paid }.HasAnyFilter);
        Assert.True(new RegisterFilter { CustomerId = Guid.NewGuid() }.HasAnyFilter);
        Assert.True(new RegisterFilter { OutstandingOnly = true }.HasAnyFilter);
        Assert.True(new RegisterFilter { OverdueOnly = true }.HasAnyFilter);
        Assert.True(new RegisterFilter { From = DateOnly.FromDateTime(DateTime.UtcNow) }.HasAnyFilter);
    }

    // =====================================================================
    // Paging
    // =====================================================================

    private static PagerVm Pager(int page, long totalItems, int pageSize = 20) => new()
    {
        Page = page,
        PageSize = pageSize,
        TotalItems = totalItems,
        BasePath = "/Invoices",
        QueryWithoutPage = new Dictionary<string, string?> { ["status"] = "7", ["search"] = "acme gmbh" }
    };

    [Fact]
    public void Paging_links_keep_the_current_filter()
    {
        // Paging used to drop the search term, which made a filtered list
        // unusable past its first page.
        var url = Pager(1, 100).UrlForPage(3);

        Assert.Contains("status=7", url);
        Assert.Contains("search=acme", url);
        Assert.Contains("page=3", url);
    }

    [Fact]
    public void A_page_number_is_never_duplicated_in_the_query_string()
    {
        var url = Pager(2, 100).UrlForPage(5);

        Assert.Single(url.Split("page="), s => s.StartsWith("5"));
    }

    [Fact]
    public void Long_page_lists_are_collapsed_around_the_current_page()
    {
        // Twenty page numbers in a row is wallpaper, not navigation.
        var numbers = Pager(10, 400).PageNumbers().ToList();

        Assert.Equal(1, numbers.First());
        Assert.Equal(20, numbers.Last());
        Assert.Contains(10, numbers);
        Assert.Contains(null, numbers);              // a gap was inserted
        Assert.True(numbers.Count < 20);
    }

    [Fact]
    public void Short_page_lists_are_shown_in_full_with_no_gaps()
    {
        var numbers = Pager(1, 60).PageNumbers().ToList();

        Assert.Equal([1, 2, 3], numbers);
    }

    [Fact]
    public void The_shown_range_counts_from_one_and_stops_at_the_total()
    {
        var pager = Pager(3, 45);

        Assert.Equal(41, pager.FromItem);
        Assert.Equal(45, pager.ToItem);
        Assert.Equal(3, pager.TotalPages);
        Assert.False(pager.HasNext);
        Assert.True(pager.HasPrevious);
    }

    [Fact]
    public void An_empty_register_has_no_pages_and_no_range()
    {
        var pager = Pager(1, 0);

        Assert.Equal(0, pager.TotalPages);
        Assert.Equal(0, pager.FromItem);
        Assert.False(pager.HasNext);
        Assert.False(pager.HasPrevious);
    }
}
