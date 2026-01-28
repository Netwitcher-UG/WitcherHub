using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.View.Quotes;

namespace WitcherHub.Pages.Quotes
{
    [Authorize] // إذا عندك Policy معين استبدله
    public class DetailsModel : PageModel
    {
        private readonly IQuote _quotes;

        public DetailsModel(IQuote quotes)
        {
            _quotes = quotes;
        }

        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; }

        public QuoteViews.QuoteDetailsView? Quote { get; private set; }

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty)
                    throw new BadRequestAppException("Invalid quote id.");

                Quote = await _quotes.GetQuoteAsync(Id, ct);
                if (Quote is null)
                    throw new NotFoundAppException("Quote not found.");

                return Page();
            }
            catch (BadRequestAppException ex)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Bad request";
                TempData["Toast.Message"] = ex.Message;
                return RedirectToPage("/Projects"); // عدّلها إذا عندك صفحة مختلفة
            }
            catch (NotFoundAppException ex)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Not found";
                TempData["Toast.Message"] = ex.Message;
                return RedirectToPage("/Projects");
            }
        }
    }
}
