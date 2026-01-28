using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Contracts;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Contracts
{
    [Authorize]
    public class CreateModel : PageModel
    {
        private readonly IContract _contracts;
        private readonly IValidator<ContractDTOs> _validator;

        public CreateModel(IContract contracts, IValidator<ContractDTOs> validator)
        {
            _contracts = contracts;
            _validator = validator;
        }

        [BindProperty(SupportsGet = true)]
        public Guid ProjectId { get; set; }

        [BindProperty]
        public ContractDTOs Form { get; set; } = new()
        {
            Contract = new ContractDto
            {
                Currency = "EUR",
                Status = DocumentStatus.Draft,

                // Defaults for period
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),

                // Optional: if you want a "signed/created" timestamp
                SignedAt = DateTimeOffset.UtcNow
            },
            Items = new()
        };

        public IActionResult OnGet()
        {
            if (ProjectId == Guid.Empty) return RedirectToPage("/Projects");

            Form.Contract.ProjectId = ProjectId;

            // ensure defaults exist (in case model binder overwrote them)
            Form.Contract.StartDate ??= DateOnly.FromDateTime(DateTime.UtcNow);

            // EndDate is non-nullable in your DTO, so just make sure it's reasonable
            if (Form.Contract.EndDate == default)
                Form.Contract.EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1));

            if (Form.Contract.Status == 0)
                Form.Contract.Status = DocumentStatus.Draft;

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(CancellationToken ct)
        {
            try
            {
                if (ProjectId == Guid.Empty) throw new BadRequestAppException("Invalid project id.");

                Form.Contract.ProjectId = ProjectId;

                // If you're not using items yet, keep it empty list instead of null (safer)
                Form.Items ??= new();

                var vr = await _validator.ValidateAsync(Form, ct);
                if (!vr.IsValid)
                {
                    foreach (var err in vr.Errors)
                        ModelState.AddModelError("Form." + err.PropertyName, err.ErrorMessage);

                    TempData["Toast.Type"] = "error";
                    TempData["Toast.Title"] = "Validation";
                    TempData["Toast.Message"] = "Please fix the highlighted fields.";
                    return Page();
                }

                var id = await _contracts.CreateAsync(Form, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Created";
                TempData["Toast.Message"] = "Contract created.";

                return RedirectToPage("./Edit", new { id });
            }
            catch (Exception ex) when (ex is BadRequestAppException or NotFoundAppException)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Error";
                TempData["Toast.Message"] = ex.Message;
                return Page();
            }
        }
    }
}
