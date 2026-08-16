using WitcherHub.Domain.Commercial;

namespace WitcherHub.Tests;

/// <summary>
/// Re-running analysis over work a person has already corrected, and the order
/// of authority between our records and a document somebody sent us.
///
/// The fixtures are synthetic and from unrelated trades on purpose: neither of
/// these rules has anything to do with what is being sold.
/// </summary>
public class TermMergeAndPrecedenceTests
{
    private static CommercialTerm Term(string key, string name, decimal? amount = null) => new()
    {
        Key = key,
        Name = name,
        PricingModel = PricingModelKind.FixedAmount,
        FixedAmount = amount is null ? null : new MoneyAmount(amount, "EUR", Commitment.Committed),
        BillingRecurrence = Recurrence.Once(),
        Commitment = Commitment.Committed
    };

    // ==================================================================== merge

    [Fact]
    public void A_second_analysis_never_silently_overwrites_reviewed_work()
    {
        var existing = new[]
        {
            Term("t1", "Kiln hire", 4_000m) with { IsHumanReviewed = true }
        };

        var proposed = new[] { Term("t1", "Kiln hire", 3_200m) };

        var merged = TermMerge.Merge(existing, proposed);

        // An afternoon of corrections is not undone by pressing analyse again.
        Assert.Equal(4_000m, merged.Terms.Single().FixedAmount!.Value);

        var change = Assert.Single(merged.Changes);
        Assert.Equal(TermChangeKind.KeptReviewed, change.Kind);
        Assert.Contains("FixedAmount", change.ChangedFields);
        Assert.Single(merged.RequiringDecision);
    }

    [Fact]
    public void The_difference_is_reported_field_by_field()
    {
        var existing = new[] { Term("t1", "Courier run", 500m) with { IsHumanReviewed = true } };

        var proposed = new[]
        {
            Term("t1", "Courier run", 650m) with
            {
                BillingRecurrence = Recurrence.Every(PeriodUnit.Week),
                Quantity = 3m
            }
        };

        var change = Assert.Single(TermMerge.Merge(existing, proposed).Changes);

        // The user is shown what would change, not asked to accept an opaque
        // replacement.
        Assert.Contains("FixedAmount", change.ChangedFields);
        Assert.Contains("BillingRecurrence", change.ChangedFields);
        Assert.Contains("Quantity", change.ChangedFields);
        Assert.DoesNotContain("Name", change.ChangedFields);
    }

    [Fact]
    public void An_unreviewed_term_is_updated_freely()
    {
        var existing = new[] { Term("t1", "Scaffold hire", 900m) };
        var proposed = new[] { Term("t1", "Scaffold hire", 1_100m) };

        var merged = TermMerge.Merge(existing, proposed);

        Assert.Equal(1_100m, merged.Terms.Single().FixedAmount!.Value);
        Assert.Equal(TermChangeKind.Changed, merged.Changes.Single().Kind);
        Assert.Empty(merged.RequiringDecision);
    }

    [Fact]
    public void A_reviewed_term_is_replaced_only_when_it_is_named()
    {
        var existing = new[] { Term("t1", "Permit fees", 300m) with { IsHumanReviewed = true } };
        var proposed = new[] { Term("t1", "Permit fees", 450m) };

        var merged = TermMerge.Merge(existing, proposed, fieldsToReplaceByKey: new[] { "t1" });

        Assert.Equal(450m, merged.Terms.Single().FixedAmount!.Value);

        // Named explicitly, so the review stands: the user chose this value.
        Assert.True(merged.Terms.Single().IsHumanReviewed);
    }

    [Fact]
    public void New_findings_are_added_alongside_what_is_already_there()
    {
        var existing = new[] { Term("t1", "Base fee", 1_000m) with { IsHumanReviewed = true } };

        var proposed = new[]
        {
            Term("t1", "Base fee", 1_000m),
            Term("t2", "Out-of-hours surcharge", 250m)
        };

        var merged = TermMerge.Merge(existing, proposed);

        Assert.Equal(2, merged.Terms.Count);
        Assert.Contains(merged.Changes, c => c.Kind == TermChangeKind.Added && c.Name == "Out-of-hours surcharge");
        Assert.Contains(merged.Changes, c => c.Kind == TermChangeKind.Unchanged);
    }

    [Fact]
    public void A_term_a_later_reading_misses_is_kept_not_deleted()
    {
        var existing = new[]
        {
            Term("t1", "Base fee", 1_000m),
            Term("t2", "Storage", 400m) with { IsHumanReviewed = true }
        };

        var proposed = new[] { Term("t1", "Base fee", 1_000m) };

        var merged = TermMerge.Merge(existing, proposed);

        // A second reading that misses something is a reason to look, not a
        // reason to delete an agreed charge.
        Assert.Equal(2, merged.Terms.Count);
        Assert.Contains(merged.Changes, c => c.Kind == TermChangeKind.Missing && c.Name == "Storage");
    }

    [Fact]
    public void Terms_are_paired_by_identity_and_name_and_never_by_price()
    {
        // Fresh analysis: new keys, same names, different amounts.
        var existing = new[]
        {
            Term("old-1", "Mobilisation", 5_000m) with { IsHumanReviewed = true },
            Term("old-2", "Demobilisation", 3_000m)
        };

        var proposed = new[]
        {
            Term("new-a", "Demobilisation", 3_500m),
            Term("new-b", "Mobilisation", 6_000m)
        };

        var merged = TermMerge.Merge(existing, proposed);

        // Pairing on price would match the wrong rows precisely when the price is
        // what changed.
        Assert.Equal(2, merged.Terms.Count);
        Assert.Equal(5_000m, merged.Terms.Single(t => t.Name == "Mobilisation").FixedAmount!.Value);
        Assert.Equal(3_500m, merged.Terms.Single(t => t.Name == "Demobilisation").FixedAmount!.Value);
    }

    [Fact]
    public void Merging_into_nothing_is_simply_the_proposal()
    {
        var merged = TermMerge.Merge(
            Array.Empty<CommercialTerm>(),
            new[] { Term("t1", "Anything", 10m) });

        Assert.Single(merged.Terms);
        Assert.Equal(TermChangeKind.Added, merged.Changes.Single().Kind);
    }

    // =============================================================== precedence

    [Fact]
    public void A_users_own_entry_beats_everything_else()
    {
        var winner = MasterDataPrecedence.Resolve(new[]
        {
            new FieldCandidate("CustomerName", "From the document", DataOrigin.RawSourceText),
            new FieldCandidate("CustomerName", "From analysis", DataOrigin.AiSuggestion),
            new FieldCandidate("CustomerName", "From the customer record", DataOrigin.MasterData),
            new FieldCandidate("CustomerName", "What the user typed", DataOrigin.UserConfirmed)
        });

        Assert.Equal("What the user typed", winner!.Value);
    }

    [Fact]
    public void Master_data_beats_anything_read_out_of_a_document()
    {
        var winner = MasterDataPrecedence.Resolve(new[]
        {
            new FieldCandidate("CustomerAddress", "Address in the pasted text", DataOrigin.RawSourceText),
            new FieldCandidate("CustomerAddress", "Address analysis proposed", DataOrigin.AiSuggestion),
            new FieldCandidate("CustomerAddress", "Address on the customer record", DataOrigin.MasterData)
        });

        // A document is evidence about that document. It is not a record.
        Assert.Equal("Address on the customer record", winner!.Value);
    }

    [Fact]
    public void An_empty_candidate_does_not_win_by_being_authoritative()
    {
        var winner = MasterDataPrecedence.Resolve(new[]
        {
            new FieldCandidate("Representative", "   ", DataOrigin.MasterData),
            new FieldCandidate("Representative", "Named in the document", DataOrigin.AiSuggestion)
        });

        // A blank master field is a gap, not an assertion that the field is empty.
        Assert.Equal("Named in the document", winner!.Value);
    }

    [Fact]
    public void A_document_that_disagrees_with_our_records_raises_a_question()
    {
        var master = new Dictionary<string, string?>
        {
            ["CustomerName"] = "Northwind Freight Ltd",
            ["CustomerAddress"] = "1 Dock Road, Hull"
        };

        var detected = new Dictionary<string, string?>
        {
            ["CustomerName"] = "Northwind Freight Limited",
            ["CustomerAddress"] = "1 Dock Road, Hull"
        };

        var conflicts = MasterDataPrecedence.FindConflicts(master, detected);

        // The address matches; the name differs and is raised. Neither is applied.
        var conflict = Assert.Single(conflicts);
        Assert.Equal("CustomerName", conflict.Field);
        Assert.Equal("Northwind Freight Ltd", conflict.MasterValue);
        Assert.Equal("Northwind Freight Limited", conflict.DetectedValue);
        Assert.True(conflict.RequiresMasterDataUpdate);
    }

    [Fact]
    public void Our_own_company_details_are_never_offered_for_update_from_a_document()
    {
        var master = new Dictionary<string, string?> { ["CompanyName"] = "Our Company Ltd" };
        var detected = new Dictionary<string, string?> { ["CompanyName"] = "Our Company GmbH" };

        var conflict = Assert.Single(MasterDataPrecedence.FindConflicts(
            master, detected, isOwnCompanyData: field => field.StartsWith("Company")));

        // Shown, because it usually means the document was written against an
        // older version of us. Never applied: analysing a document somebody sent
        // us is not a reason to change who we are.
        Assert.False(conflict.RequiresMasterDataUpdate);
    }

    [Theory]
    [InlineData("Acme  Ltd", "acme ltd")]
    [InlineData("Acme Ltd.", "Acme Ltd")]
    [InlineData(" Acme Ltd ", "Acme Ltd")]
    public void Spacing_casing_and_trailing_punctuation_are_not_disagreements(string a, string b)
    {
        Assert.True(MasterDataPrecedence.Equivalent(a, b));
    }

    [Fact]
    public void A_genuinely_different_value_is_a_disagreement()
    {
        Assert.False(MasterDataPrecedence.Equivalent("1 Dock Road, Hull", "2 Dock Road, Hull"));
    }
}
