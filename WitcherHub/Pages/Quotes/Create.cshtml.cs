using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Quotes;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Quotes
{
    [Authorize]
    public class CreateModel : PageModel
    {
        private readonly IQuote _quotes;
        private readonly IValidator<QuoteDTOs> _validator;

        public CreateModel(IQuote quotes, IValidator<QuoteDTOs> validator)
        {
            _quotes = quotes;
            _validator = validator;
        }

        [BindProperty(SupportsGet = true)]
        public Guid ProjectId { get; set; }

        [BindProperty]
        public QuoteDTOs Form { get; set; } = new();

        // NOTE:
        // We no longer show a standalone "Create" UI.
        // Hitting /Quotes/Create?projectId=... will immediately create a Draft quote
        // with fixed Currency=EUR and IssuedAt=today (server), then redirect to Edit.
        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            try
            {
                if (ProjectId == Guid.Empty)
                    throw new BadRequestAppException("ProjectId is required.");

                Form = new QuoteDTOs
                {
                    Quote = new QuoteDto
                    {
                        ProjectId = ProjectId,
                        Currency = "EUR",
                        Status = DocumentStatus.Draft,
                        // server default
                        IssuedAt = DateTimeOffset.Now
                    },
                    Items = new List<QuoteItemDto>() // header only
                };

                var vr = await _validator.ValidateAsync(Form, ct);
                if (!vr.IsValid)
                {
                    foreach (var err in vr.Errors)
                    {
                        var key = err.PropertyName;
                        if (!string.IsNullOrWhiteSpace(key) && !key.StartsWith("Form."))
                            key = "Form." + key;
                        ModelState.AddModelError(key, err.ErrorMessage);
                    }

                    TempData["Toast.Type"] = "error";
                    TempData["Toast.Title"] = "Validation";
                    TempData["Toast.Message"] = "Please fix the highlighted fields.";
                    return RedirectToPage("/Projects/Details", new { id = ProjectId });
                }

                var id = await _quotes.CreateAsync(Form, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Success";
                TempData["Toast.Message"] = "Quote created.";

                return RedirectToPage("./Edit", new { id });
            }
            catch (BadRequestAppException ex)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Not allowed";
                TempData["Toast.Message"] = ex.Message;
                return RedirectToPage("/Projects");
            }
            catch (NotFoundAppException ex)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Not found";
                TempData["Toast.Message"] = ex.Message;
                return RedirectToPage("/Projects");
            }
        }

        // Backward compatibility (if something still posts to this page).
        public Task<IActionResult> OnPostAsync(CancellationToken ct) => OnGetAsync(ct);
    }
}
