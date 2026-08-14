using Microsoft.AspNetCore.Mvc;
using WitcherHub.Pages.Contracts.Items;

namespace WitcherHub.Tests;

/// <summary>
/// The contract-creation flow used to land on an editor that could only add
/// positions already present in the Service Catalog — its empty state read
/// "Click From Services to create the first one", and its Add Position form had
/// no price field at all, because the price came from the chosen service.
///
/// Both routes now forward to the builder, which accepts positions typed by
/// hand. These tests pin that down: the old URLs still resolve (bookmarks, the
/// project workspace, browser history) and they arrive at the builder.
/// </summary>
public class ContractBuilderRoutingTests
{
    private static readonly Guid AContract = Guid.Parse("6f5c1f2e-2f7a-4a1a-9f6b-2b1a3c4d5e6f");

    [Fact]
    public void The_old_position_manager_forwards_to_the_builder()
    {
        var page = new ManageModel { ContractId = AContract };

        var result = Assert.IsType<RedirectToPageResult>(page.OnGet());

        Assert.Equal("/Contracts/Positions", result.PageName);
        Assert.Equal(AContract, result.RouteValues?["contractId"]);
    }

    [Fact]
    public void The_old_add_position_form_forwards_to_the_builder()
    {
        var page = new CreateModel { ContractId = AContract };

        var result = Assert.IsType<RedirectToPageResult>(page.OnGet());

        Assert.Equal("/Contracts/Positions", result.PageName);
        Assert.Equal(AContract, result.RouteValues?["contractId"]);
    }

    [Theory]
    [InlineData(typeof(ManageModel))]
    [InlineData(typeof(CreateModel))]
    public void A_forward_without_a_contract_lands_on_the_register_rather_than_nowhere(Type pageType)
    {
        // These URLs are reachable with no id at all. Sending the reader to a
        // builder for a contract that does not exist would 404 on a page they did
        // not ask for; the register is somewhere they can act.
        var page = Activator.CreateInstance(pageType)!;

        var result = pageType == typeof(ManageModel)
            ? ((ManageModel)page).OnGet()
            : ((CreateModel)page).OnGet();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Contracts/Index", redirect.PageName);
    }
}
