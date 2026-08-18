using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Services.Contracts;
using WitcherHub.Domain.Commercial;

namespace WitcherHub.Tests;

/// <summary>
/// The semantic pipeline now reads supplied documents, and its result is shown in
/// the review screen the old fixed-field extraction was written for.
///
/// The risk in that projection is quiet loss: the new shape has free-form keys and
/// the old one has a fixed set of fields, so anything unmatched could vanish
/// without a trace, and an amount the engine deliberately refused to total could
/// reappear as a confident figure. These tests are about what survives the
/// crossing and what must not be invented on the way.
/// </summary>
public class SemanticExtractionProjectionTests
{
    private static SemanticAnalysisResult Analysed(
        SemanticExtractionDto extraction,
        IReadOnlyList<CommercialTerm>? terms = null,
        int? contractMonths = null,
        IReadOnlyList<TermIssue>? issues = null,
        IReadOnlyList<string>? discarded = null)
    {
        var actualTerms = terms ?? Array.Empty<CommercialTerm>();

        return new SemanticAnalysisResult
        {
            Succeeded = true,
            Extraction = extraction,
            Terms = actualTerms,
            Issues = issues ?? Array.Empty<TermIssue>(),
            DiscardedReasons = discarded ?? Array.Empty<string>(),

            // The real engine, so the projection is tested against the figures the
            // application will actually produce rather than against invented ones.
            Financials = ContractFinancialEngine.Calculate(actualTerms, "EUR", contractMonths)
        };
    }

    private static CommercialTerm Fixed(string name, decimal amount) => new()
    {
        Name = name,
        PricingModel = PricingModelKind.FixedAmount,
        FixedAmount = new MoneyAmount(amount, "EUR", Commitment.Committed),
        Commitment = Commitment.Committed
    };

    // ---- nothing invented ---------------------------------------------------

    [Fact]
    public void An_empty_reading_produces_empty_fields_and_no_total()
    {
        var dto = SemanticExtractionProjection.ToLegacyExtraction(
            Analysed(new SemanticExtractionDto()));

        Assert.False(dto.Title.HasValue);
        Assert.False(dto.CustomerName.HasValue);
        Assert.False(dto.TotalPrice.HasValue);
        Assert.Empty(dto.Positions);

        // The absence of a price is a finding, recorded as one.
        Assert.True(dto.PriceMissing);
    }

    [Fact]
    public void Nothing_arrives_already_confirmed()
    {
        var extraction = new SemanticExtractionDto
        {
            DocumentTitle = "Wartungsvertrag",
            DetectedParties = new() { ["customerName"] = "Muster GmbH" }
        };

        var dto = SemanticExtractionProjection.ToLegacyExtraction(
            Analysed(extraction, [Fixed("Wartung", 1200m)]));

        // A reading is a proposal. Confirmation is a person's act, and until it
        // happens none of this may reach the contract.
        Assert.False(dto.Title.Confirmed);
        Assert.True(dto.Title.NeedsConfirmation);
        Assert.False(dto.CustomerName.Confirmed);
        Assert.False(dto.TotalPrice.Confirmed);
        Assert.All(dto.Positions, p => Assert.False(p.Accepted));
    }

    // ---- tolerant key matching ---------------------------------------------

    [Theory]
    [InlineData("customerName")]
    [InlineData("CustomerName")]
    [InlineData("customer_name")]
    [InlineData("Customer Name")]
    [InlineData("client")]
    [InlineData("Auftraggeber")]
    public void A_party_is_recognised_however_the_analyser_spells_the_key(string key)
    {
        // The schema asks for free-form keys on purpose, so that a document
        // describing something unanticipated is still recorded. That only works if
        // the reader is tolerant about how a key is written — including in German,
        // since the documents are.
        var extraction = new SemanticExtractionDto
        {
            DetectedParties = new() { [key] = "Muster GmbH" }
        };

        var dto = SemanticExtractionProjection.ToLegacyExtraction(Analysed(extraction));

        Assert.Equal("Muster GmbH", dto.CustomerName.Value);
    }

    [Fact]
    public void Contract_terms_land_in_their_own_fields()
    {
        var extraction = new SemanticExtractionDto
        {
            DetectedContractTerms = new()
            {
                ["startDate"] = "2026-01-01",
                ["Kündigungsfrist"] = "3 Monate",
                ["billing cycle"] = "monatlich"
            }
        };

        var dto = SemanticExtractionProjection.ToLegacyExtraction(Analysed(extraction));

        Assert.Equal("2026-01-01", dto.StartDate.Value);
        Assert.Equal("3 Monate", dto.TerminationNotice.Value);
        Assert.Equal("monatlich", dto.BillingCycle.Value);
    }

    [Fact]
    public void A_key_that_matches_nothing_is_kept_rather_than_dropped()
    {
        // The whole point of the generic schema is that it can report something the
        // fixed shape never anticipated. Discarding it here would undo that, and
        // the user would never learn the analyser had read it.
        var extraction = new SemanticExtractionDto
        {
            DetectedContractTerms = new()
            {
                ["escalationProcedure"] = "Eskalation an die Geschäftsführung binnen 5 Tagen"
            }
        };

        var dto = SemanticExtractionProjection.ToLegacyExtraction(Analysed(extraction));

        Assert.Contains("escalationProcedure", dto.OtherTerms.Value);
        Assert.Contains("Eskalation", dto.OtherTerms.Value);
    }

    // ---- money --------------------------------------------------------------

    [Fact]
    public void The_committed_total_comes_from_the_engine_and_parses_back()
    {
        var dto = SemanticExtractionProjection.ToLegacyExtraction(
            Analysed(new SemanticExtractionDto(), [Fixed("Setup", 2500m), Fixed("Schulung", 750m)]));

        Assert.True(dto.TotalPrice.HasValue);
        Assert.False(dto.PriceMissing);

        // The confirmation flow parses this string back into a decimal, so the
        // shape written here has to survive that round trip exactly.
        Assert.True(TryParseLikeConfirmation(dto.TotalPrice.Value, out var parsed));
        Assert.Equal(3250m, parsed);
    }

    [Fact]
    public void A_rate_with_no_agreed_quantity_produces_no_line_total_and_no_contract_value()
    {
        // An hourly rate with no committed hours is a real charge and an unknowable
        // amount. Showing the rate as a total is how variable work came to look
        // like committed money.
        var hourly = new CommercialTerm
        {
            Name = "Support",
            PricingModel = PricingModelKind.TimeAndMaterials,
            UnitRate = new MoneyAmount(95m, "EUR", Commitment.Variable),
            QuantityUnit = "Stunde",
            Commitment = Commitment.Variable
        };

        var dto = SemanticExtractionProjection.ToLegacyExtraction(
            Analysed(new SemanticExtractionDto(), [hourly]));

        var position = Assert.Single(dto.Positions);

        Assert.Equal(95m, position.UnitPrice);
        Assert.Null(position.LineTotal);
        Assert.Null(position.Quantity);

        // Nothing is committed, so there is no contract value to state.
        Assert.False(dto.TotalPrice.HasValue);
        Assert.True(dto.PriceMissing);
    }

    [Fact]
    public void An_amount_the_engine_could_not_resolve_is_reported_rather_than_hidden()
    {
        var unresolvable = new CommercialTerm
        {
            Name = "Verbrauch",
            PricingModel = PricingModelKind.UsageBased,
            UnitRate = new MoneyAmount(0.12m, "EUR", Commitment.Committed),
            Commitment = Commitment.Committed
        };

        var result = Analysed(new SemanticExtractionDto(), [Fixed("Grundgebühr", 500m), unresolvable]);

        Assert.True(result.Financials!.IsPartial);

        var dto = SemanticExtractionProjection.ToLegacyExtraction(result);

        // The user is told the total is a floor.
        Assert.Contains(dto.Warnings, w => w.Contains("only the amounts that could be calculated"));

        // The engine's reason for not totalling it is not repeated as a flat
        // warning — it belongs in the financial breakdown, where the term is named
        // beside it.
        Assert.DoesNotContain(dto.Warnings, w => w.Contains("cannot be totalled"));
        Assert.Contains(result.Financials!.Unresolved, u => u.TermName == "Verbrauch");

        // It is named here for a different reason: it cannot become a priced line.
        Assert.Contains(dto.Warnings, w => w.Contains("Verbrauch") && w.Contains("cannot be added"));
    }

    [Fact]
    public void A_terms_commitment_is_visible_on_the_position_when_it_is_not_firm()
    {
        var estimated = new CommercialTerm
        {
            Name = "Reisekosten",
            PricingModel = PricingModelKind.FixedAmount,
            FixedAmount = new MoneyAmount(400m, "EUR", Commitment.Estimated),
            Commitment = Commitment.Estimated
        };

        var dto = SemanticExtractionProjection.ToLegacyExtraction(
            Analysed(new SemanticExtractionDto(), [estimated]));

        var position = Assert.Single(dto.Positions);

        // A reviewer looking at 400 needs to know nobody has agreed to owe it.
        Assert.Contains("Estimated", position.Description);

        Assert.False(dto.TotalPrice.HasValue);
    }

    // ---- what a person must look at ----------------------------------------

    [Fact]
    public void Open_questions_and_warnings_from_the_reading_all_reach_the_reviewer()
    {
        var extraction = new SemanticExtractionDto
        {
            Warnings = ["Two totals in the document disagree."],
            OpenQuestions = ["Is the discount one-off or recurring?"]
        };

        var result = Analysed(
            extraction,
            [Fixed("Paket", 1000m)],
            discarded: ["A proposal with no name and no amount was discarded."]);

        var dto = SemanticExtractionProjection.ToLegacyExtraction(result);

        Assert.Contains(dto.Warnings, w => w.Contains("disagree"));
        Assert.Contains(dto.Warnings, w => w.Contains("discount"));
        Assert.Contains(dto.Warnings, w => w.Contains("discarded"));
    }

    [Fact]
    public void A_validation_issue_names_the_term_it_belongs_to()
    {
        var term = Fixed("Hosting", 240m);

        var result = Analysed(
            new SemanticExtractionDto(),
            [term],
            issues:
            [
                new TermIssue(term.Key, "BillingRecurrence", ValidationSeverity.Warning,
                    "The billing frequency could not be read.")
            ]);

        var dto = SemanticExtractionProjection.ToLegacyExtraction(result);

        // "Something is wrong somewhere" is not actionable; the line has to be
        // findable.
        Assert.Contains(dto.Warnings, w => w.Contains("Hosting") && w.Contains("BillingRecurrence"));
    }

    // ---- what can honestly become a priced line ----------------------------

    [Fact]
    public void A_charge_with_no_calculable_amount_cannot_become_a_position()
    {
        // A contract position carries a quantity and a line total and has no way to
        // say "not stated". Adopting an hourly rate as one used to default the
        // quantity to 1, which turned a rate into money on the contract.
        var hourly = new CommercialTerm
        {
            Name = "Support",
            PricingModel = PricingModelKind.TimeAndMaterials,
            UnitRate = new MoneyAmount(95m, "EUR", Commitment.Variable),
            QuantityUnit = "Stunde",
            Commitment = Commitment.Variable
        };

        var dto = SemanticExtractionProjection.ToLegacyExtraction(
            Analysed(new SemanticExtractionDto(), [hourly]));

        var position = Assert.Single(dto.Positions);

        Assert.False(position.CanBecomePosition);
        Assert.Contains("Stunde", position.BlockedReason);

        // And it is said out loud rather than merely being absent from the lines.
        Assert.Contains(dto.Warnings, w => w.Contains("Support") && w.Contains("cannot be added"));
    }

    [Fact]
    public void A_fixed_amount_can_become_a_position()
    {
        var dto = SemanticExtractionProjection.ToLegacyExtraction(
            Analysed(new SemanticExtractionDto(), [Fixed("Setup", 2500m)]));

        var position = Assert.Single(dto.Positions);

        Assert.True(position.CanBecomePosition);
        Assert.Null(position.BlockedReason);
        Assert.Equal(2500m, position.LineTotal);
    }

    [Fact]
    public void A_terms_commitment_travels_with_it()
    {
        var dto = SemanticExtractionProjection.ToLegacyExtraction(
            Analysed(new SemanticExtractionDto(), [Fixed("Setup", 2500m)]));

        Assert.Equal("Committed", Assert.Single(dto.Positions).Commitment);
    }

    [Fact]
    public void A_position_links_back_to_the_term_it_was_read_from()
    {
        var term = Fixed("Setup", 2500m);

        var dto = SemanticExtractionProjection.ToLegacyExtraction(
            Analysed(new SemanticExtractionDto(), [term]));

        // Without this the full structure — phases, caps, separate delivery and
        // billing frequencies — cannot be recovered when the position is saved.
        Assert.Equal(term.Key, Assert.Single(dto.Positions).TermKey);
    }

    // ---- billing frequency --------------------------------------------------

    [Theory]
    [InlineData(PeriodUnit.Month, 1, "Monthly")]
    [InlineData(PeriodUnit.Month, 3, "Quarterly")]
    [InlineData(PeriodUnit.Month, 6, "SemiAnnual")]
    [InlineData(PeriodUnit.Month, 12, "Annual")]
    [InlineData(PeriodUnit.Quarter, 1, "Quarterly")]
    [InlineData(PeriodUnit.Year, 1, "Annual")]
    public void A_frequency_the_app_has_is_mapped_to_it(PeriodUnit unit, int interval, string expected)
    {
        var term = Fixed("Pauschale", 500m) with
        {
            BillingRecurrence = Recurrence.Every(unit, interval, "monatlich")
        };

        var dto = SemanticExtractionProjection.ToLegacyExtraction(
            Analysed(new SemanticExtractionDto(), [term]));

        Assert.Equal(expected, Assert.Single(dto.Positions).BillingCycle);
    }

    [Fact]
    public void A_frequency_the_app_does_not_have_is_reported_as_unmapped_not_as_one_off()
    {
        // This is the one that mattered: the analyser reports the frequency in the
        // document's own words, which matches no member of a five-value enum, and
        // the fallback used to be "OneTime". A weekly charge became a single charge.
        var weekly = Fixed("Wöchentliche Betreuung", 200m) with
        {
            BillingRecurrence = Recurrence.Every(PeriodUnit.Week, 1, "wöchentlich")
        };

        var dto = SemanticExtractionProjection.ToLegacyExtraction(
            Analysed(new SemanticExtractionDto(), [weekly]));

        var position = Assert.Single(dto.Positions);

        Assert.Null(position.BillingCycle);
        Assert.Equal("wöchentlich", position.BillingCyclePhrase);

        // And the user is told, rather than left with a silently wrong cycle.
        Assert.Contains(dto.Warnings, w => w.Contains("wöchentlich") && w.Contains("Choose one"));
    }

    [Fact]
    public void A_one_off_charge_is_mapped_to_one_off()
    {
        var once = Fixed("Setup", 900m) with { BillingRecurrence = Recurrence.Once("einmalig") };

        var dto = SemanticExtractionProjection.ToLegacyExtraction(
            Analysed(new SemanticExtractionDto(), [once]));

        Assert.Equal("OneTime", Assert.Single(dto.Positions).BillingCycle);
    }

    // ---- protections carried over from the analyser it replaced -------------

    [Fact]
    public void A_term_with_no_name_keeps_its_figures_and_gets_a_placeholder()
    {
        // Dropping the row would hide an amount somebody may owe; leaving it
        // untitled makes it unreviewable. It gets a name it can be corrected under.
        var nameless = new CommercialTerm
        {
            Name = "",
            PricingModel = PricingModelKind.FixedAmount,
            FixedAmount = new MoneyAmount(300m, "EUR", Commitment.Committed),
            Commitment = Commitment.Committed
        };

        var dto = SemanticExtractionProjection.ToLegacyExtraction(
            Analysed(new SemanticExtractionDto(), [nameless]));

        var position = Assert.Single(dto.Positions);

        Assert.Equal("(untitled position)", position.Title);
        Assert.Equal(300m, position.LineTotal);
    }

    [Fact]
    public void A_contract_with_no_price_says_so_in_words()
    {
        // The flag alone reads as an oversight. The sentence makes it a decision
        // the user is asked to confirm.
        var dto = SemanticExtractionProjection.ToLegacyExtraction(
            Analysed(new SemanticExtractionDto()));

        Assert.True(dto.PriceMissing);
        Assert.Contains(dto.Warnings, w => w.Contains("names no committed price"));
    }

    /// <summary>
    /// Mirrors ContractTextAnalyzer.TryParseAmount, which is what the confirmation
    /// flow uses to read the projected total back. Duplicated rather than exposed
    /// because the point is to prove the written shape survives that reader.
    /// </summary>
    private static bool TryParseLikeConfirmation(string? value, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var cleaned = new string(value.Where(c => char.IsDigit(c) || c is '.' or ',' or '-').ToArray());
        if (cleaned.Length == 0) return false;

        if (cleaned.Contains(',') && cleaned.LastIndexOf(',') > cleaned.LastIndexOf('.'))
            cleaned = cleaned.Replace(".", "").Replace(',', '.');
        else
            cleaned = cleaned.Replace(",", "");

        return decimal.TryParse(
            cleaned,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out amount);
    }
}
