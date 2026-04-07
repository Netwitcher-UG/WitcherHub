using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Globalization;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.ManageData;

namespace WitcherHub.Pages.Clients;

public class DetailsModel : PageModel
{
    private readonly ICustomer _customers;

    public DetailsModel(ICustomer customers)
    {
        _customers = customers;
    }

    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = "";

    public List<SelectListItem> CountryOptions { get; set; } = new();

    private static readonly Lazy<IReadOnlyList<(string Code, string Name)>> _allCountries =
        new(() =>
        {
            var list = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
                .Where(ci => !ci.CultureTypes.HasFlag(CultureTypes.UserCustomCulture))
                .Select(ci =>
                {
                    try
                    {
                        var r = new RegionInfo(ci.Name);
                        return (Code: r.TwoLetterISORegionName, Name: r.EnglishName);
                    }
                    catch
                    {
                        return (Code: "", Name: "");
                    }
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Code) && !string.IsNullOrWhiteSpace(x.Name))
                .Distinct()
                .OrderBy(x => x.Name)
                .ToList();

            return list;
        });

    private void EnsureCountryOptions()
    {
        CountryOptions = _allCountries.Value
            .Select(c => new SelectListItem
            {
                Value = c.Code,
                Text = c.Name
            })
            .ToList();
    }

    public void OnGet()
    {
        EnsureCountryOptions();

        if (string.IsNullOrWhiteSpace(Id))
        {
            Id = "";
        }
    }

    public async Task<IActionResult> OnGetClientAsync(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
            return BadRequest(new { message = "Invalid client id." });

        var client = await _customers.GetCustomerAsync(id, ct);
        if (client is null)
            return NotFound(new { message = "Customer not found." });

        var projects = await _customers.GetCustomerProjectsAsync(id, ct);

        return new JsonResult(new
        {
            customer = client,
            projects
        });
    }
}