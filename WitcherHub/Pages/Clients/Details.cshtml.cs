using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Globalization;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Pages.Models.UI;

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

            // The status arrives already worded, from the one vocabulary the whole
            // application uses.
            //
            // The browser used to translate the enum itself, with a map of
            // {0:Draft, 1:Active, 2:Closed, 3:Canceled} — which missed OnHold=4
            // entirely, so a paused project rendered as the bare number "4", and
            // spelled Cancelled with one L so that status found no colour either.
            // Two copies of an enum is one copy too many; this page now shows what
            // the Projects list and the workspace show because it is told.
            projects = projects.Select(p => new
            {
                p.Id,
                p.Title,
                p.StartDate,
                p.EndDate,
                // The view type has this nullable although a project always has a
                // status, so a missing one is reported as unknown rather than
                // quietly shown as Draft — which would be a different project.
                Status = p.Status?.ToString(),
                StatusLabel = p.Status is { } s
                    ? DocumentStatusPresentation.ForProject(s).Label
                    : "Unknown",
                StatusTone = p.Status is { } t
                    ? DocumentStatusPresentation.ForProject(t).Tone
                    : "secondary"
            })
        });
    }
}