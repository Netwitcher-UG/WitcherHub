using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Invoices;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Invoices
{
    [Authorize]
    public class CreateModel : PageModel
    {
        private readonly IInvoice _invoices;
        private readonly IValidator<InvoiceDTOs> _validator;

        public CreateModel(IInvoice invoices, IValidator<InvoiceDTOs> validator)
        {
            _invoices = invoices;
            _validator = validator;
        }

        [BindProperty(SupportsGet = true)]
        public Guid ProjectId { get; set; }

        [BindProperty]
        public InvoiceDTOs Form { get; set; } = new()
        {
            Invoice = new InvoiceDto
            {
                Currency = "EUR",
                Status = DocumentStatus.Draft,
                IssuedAt = DateTimeOffset.UtcNow
            },
            Items = null
        };

        public IActionResult OnGet()
        {
            if (ProjectId == Guid.Empty) return RedirectToPage("/Projects");

            Form.Invoice.ProjectId = ProjectId;

            // Defaults if empty
            Form.Invoice.IssuedAt ??= DateTimeOffset.UtcNow;
            Form.Invoice.Status = Form.Invoice.Status == 0 ? DocumentStatus.Draft : Form.Invoice.Status;

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(CancellationToken ct)
        {
            try
            {
                if (ProjectId == Guid.Empty) throw new BadRequestAppException("Invalid project id.");

                Form.Invoice.ProjectId = ProjectId;
                Form.Items = null;

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

                var id = await _invoices.CreateAsync(Form, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Created";
                TempData["Toast.Message"] = "Invoice created.";

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
