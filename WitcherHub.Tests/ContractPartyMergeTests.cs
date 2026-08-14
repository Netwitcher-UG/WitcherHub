using WitcherHub.Infrastructure.Services.Contracts;

namespace WitcherHub.Tests;

/// <summary>
/// Putting the current parties into a supplied contract.
///
/// Two jobs, deliberately kept apart. A placeholder means exactly one thing and
/// is filled in. Text that merely resembles an old company name is a guess, and
/// rewriting who a contract is between on a guess is not acceptable — those are
/// proposals a person accepts.
/// </summary>
public class ContractPartyMergeTests
{
    private static PartyDetails Ours() => new(
        CompanyName: "Netwitcher UG (haftungsbeschränkt)",
        CompanyAddress: "Kochhannstraße 6\n10249 Berlin",
        CustomerName: "Musterfirma GmbH",
        CustomerAddress: "Hauptstraße 1\n80331 München",
        ContractDate: new DateOnly(2026, 8, 14));

    [Fact]
    public void Placeholders_are_filled_in_without_asking()
    {
        const string document = """
            Vertrag zwischen [COMPANY_NAME], [COMPANY_ADDRESS]
            und [CUSTOMER_NAME], [CUSTOMER_ADDRESS].
            Datum: [CONTRACT_DATE]
            """;

        var result = ContractPartyMerge.Merge(document, Ours());

        Assert.Contains("Netwitcher UG", result.Document);
        Assert.Contains("Musterfirma GmbH", result.Document);
        Assert.Contains("14.08.2026", result.Document);
        Assert.DoesNotContain("[COMPANY_NAME]", result.Document);
        Assert.DoesNotContain("[CUSTOMER_ADDRESS]", result.Document);

        Assert.All(result.Applied, r => Assert.True(r.IsPlaceholder));
    }

    [Fact]
    public void Curly_and_angle_placeholder_spellings_are_recognised_too()
    {
        const string document = "Zwischen {{COMPANY_NAME}} und <CUSTOMER_NAME>.";

        var result = ContractPartyMerge.Merge(document, Ours());

        Assert.Contains("Netwitcher UG", result.Document);
        Assert.Contains("Musterfirma GmbH", result.Document);
    }

    [Fact]
    public void Old_party_details_are_proposed_and_never_applied_on_their_own()
    {
        const string document = "Vertrag zwischen Alte Firma AG und Musterfirma GmbH.";

        var proposals = ContractPartyMerge.ProposeReplacements(
            document, Ours(),
            documentCompanyName: "Alte Firma AG",
            documentCompanyAddress: null,
            documentCustomerName: "Musterfirma GmbH",
            documentCustomerAddress: null);

        // The stale company name is proposed; the customer name already matches
        // and produces nothing.
        var proposal = Assert.Single(proposals);
        Assert.Equal("Company name", proposal.Field);
        Assert.Equal("Alte Firma AG", proposal.OldValue);
        Assert.Equal("Netwitcher UG (haftungsbeschränkt)", proposal.ProposedValue);
        Assert.True(proposal.RequiresConfirmation);

        // Merging without confirming leaves the document exactly as supplied.
        var merged = ContractPartyMerge.Merge(document, Ours());
        Assert.Contains("Alte Firma AG", merged.Document);
    }

    [Fact]
    public void A_confirmed_replacement_is_applied()
    {
        const string document = "Vertrag zwischen Alte Firma AG und Musterfirma GmbH.";

        var confirmed = new[]
        {
            new PartyReplacement("Company name", "Alte Firma AG", "Netwitcher UG (haftungsbeschränkt)", false)
        };

        var result = ContractPartyMerge.Merge(document, Ours(), confirmed);

        Assert.Contains("Netwitcher UG", result.Document);
        Assert.DoesNotContain("Alte Firma AG", result.Document);
    }

    [Fact]
    public void Missing_party_information_is_reported_by_name()
    {
        var incomplete = new PartyDetails("Netwitcher UG", null, null, null, null);

        var result = ContractPartyMerge.Merge("[COMPANY_NAME] und [CUSTOMER_NAME]", incomplete);

        // Telling the user "something is missing" is useless; telling them which
        // fields is actionable.
        Assert.Contains("Company address", result.MissingFields);
        Assert.Contains("Customer name", result.MissingFields);
        Assert.DoesNotContain("Company name", result.MissingFields);
    }

    [Fact]
    public void The_supplied_document_is_never_modified_in_place()
    {
        const string document = "Zwischen [COMPANY_NAME] und [CUSTOMER_NAME].";

        var result = ContractPartyMerge.Merge(document, Ours());

        // The input string is the stored source. It has to come back untouched.
        Assert.Equal("Zwischen [COMPANY_NAME] und [CUSTOMER_NAME].", document);
        Assert.NotEqual(document, result.Document);
    }

    [Fact]
    public void Paragraphs_and_line_breaks_survive_the_merge()
    {
        const string document = "§1 Gegenstand\n\n- Punkt eins\n- Punkt zwei\n\nZwischen [CUSTOMER_NAME].";

        var result = ContractPartyMerge.Merge(document, Ours());

        Assert.Contains("§1 Gegenstand\n\n- Punkt eins\n- Punkt zwei", result.Document);
    }
}
