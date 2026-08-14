using WitcherHub.Domain.Contracts;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Tests;

/// <summary>
/// The rule that decides whether a contract can be turned into a document.
///
/// It used to be "at least one position", written out separately in the page,
/// the browser, the draft service, the position store, the contract details page
/// and the signing page. A contract whose wording is a document the customer
/// supplied has no positions and never will, so every one of those copies
/// refused it — and the user was told to "save your positions first" on a
/// contract that had nothing to save.
/// </summary>
public class ContractSourceRuleTests
{
    [Fact]
    public void Neither_positions_nor_text_blocks_generation()
    {
        var source = ContractSource.From(positionCount: 0, hasSuppliedText: false);

        Assert.False(source.CanGenerate);
        Assert.NotNull(source.BlockingReason);

        // The message has to say what to do, and both routes have to be in it.
        Assert.Contains("position", source.BlockingReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("contract text", source.BlockingReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Supplied_text_alone_allows_generation()
    {
        var source = ContractSource.From(positionCount: 0, hasSuppliedText: true);

        Assert.True(source.CanGenerate);
        Assert.Null(source.BlockingReason);
        Assert.Equal(ContractSourceMode.SuppliedText, source.Mode);
    }

    [Fact]
    public void An_approved_supplied_version_with_no_positions_is_accepted()
    {
        // Exactly the state the reported contract was in: an approved supplied
        // version, zero positions, and generation refused.
        var source = ContractSource.From(positionCount: 0, hasSuppliedText: false, hasApprovedText: true);

        Assert.True(source.CanGenerate);
        Assert.True(source.HasApprovedText);
        Assert.Equal(ContractSourceMode.SuppliedText, source.Mode);
    }

    [Fact]
    public void Positions_alone_allow_generation()
    {
        var source = ContractSource.From(positionCount: 3, hasSuppliedText: false);

        Assert.True(source.CanGenerate);
        Assert.Equal(ContractSourceMode.Positions, source.Mode);
    }

    [Fact]
    public void Positions_and_text_together_are_hybrid()
    {
        var source = ContractSource.From(positionCount: 2, hasSuppliedText: true);

        Assert.True(source.CanGenerate);
        Assert.Equal(ContractSourceMode.Hybrid, source.Mode);
    }

    [Fact]
    public void The_primary_action_says_what_it_will_do()
    {
        Assert.Equal("Generate from positions",
            ContractSource.From(2, false).PrimaryActionLabel);

        Assert.Equal("Prepare supplied contract",
            ContractSource.From(0, true).PrimaryActionLabel);

        Assert.Equal("Generate from text and positions",
            ContractSource.From(2, true).PrimaryActionLabel);
    }

    [Fact]
    public void An_empty_position_list_reads_differently_once_there_is_text()
    {
        var withoutText = ContractSource.From(0, false).EmptyPositionsMessage;
        var withText = ContractSource.From(0, true).EmptyPositionsMessage;

        Assert.NotEqual(withoutText, withText);

        // With supplied text an empty list is finished, not unfinished, and the
        // screen must not ask for a position that is not required.
        Assert.Contains("optionally", withText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Save your positions first", withText, StringComparison.OrdinalIgnoreCase);
    }
}
