using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Invoices;
using WitcherHub.Application.Models.View.Invoices;
using WitcherHub.Infrastructure.Data.Context;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Invoices
{
    [Authorize]
    public class EditModel : PageModel
    {
        private readonly IInvoice _invoices;
        private readonly IValidator<UpdateInvoiceDto> _updateValidator;
        private readonly IValidator<CreateInvoiceItemDto> _createItemValidator;
        private readonly IValidator<UpdateInvoiceItemDto> _updateItemValidator;

        // Taxes lookup (temporary, like Quotes)
        private readonly AppDbContext _db;

        public EditModel(
            IInvoice invoices,
            IValidator<UpdateInvoiceDto> updateValidator,
            IValidator<CreateInvoiceItemDto> createItemValidator,
            IValidator<UpdateInvoiceItemDto> updateItemValidator,
            AppDbContext db)
        {
            _invoices = invoices;
            _updateValidator = updateValidator;
            _createItemValidator = createItemValidator;
            _updateItemValidator = updateItemValidator;
            _db = db;
        }

        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; } // InvoiceId

        public InvoiceViews.InvoiceDetailsView? Invoice { get; private set; }

        public List<SelectListItem> TaxRateOptions { get; private set; } = new();

        [BindProperty]
        public UpdateInvoiceDto Header { get; set; } = new()
        {
            Invoice = new InvoiceDto { Currency = "EUR", Status = DocumentStatus.Draft },
            Items = null
        };

        [BindProperty]
        public CreateInvoiceItemDto NewItem { get; set; } = new();

        [BindProperty]
        public string NewItemConfigJson { get; set; } = "{}";

        [BindProperty]
        public UpdateInvoiceItemDto EditItem { get; set; } = new();

        [BindProperty]
        public string EditItemConfigJson { get; set; } = "{}";

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (Id == Guid.Empty) return NotFound();

            Invoice = await _invoices.GetInvoiceAsync(Id, ct);
            if (Invoice is null) return NotFound();

            await LoadLookupsAsync(ct);

            Header.Invoice.ProjectId = Invoice.ProjectId;
            Header.Invoice.Currency = Invoice.Currency;
            Header.Invoice.Notes = Invoice.Notes;
            Header.Invoice.IssuedAt = Invoice.IssuedAt;
            Header.Invoice.Status = Invoice.Status;

            NewItem.InvoiceId = Invoice.Id;
            NewItem.Item.Position = (Invoice.Items?.Count ?? 0) + 1;
            NewItem.Item.Quantity = 1;
            NewItem.Item.UnitPrice = 0;
            NewItem.Item.Title = "";
            NewItem.Item.TaxRateId = null;
            NewItem.Item.DiscountType = null;
            NewItem.Item.DiscountValue = null;
            NewItemConfigJson = "{}";

            EditItem.InvoiceId = Invoice.Id;
            EditItemConfigJson = "{}";

            return Page();
        }

        public async Task<IActionResult> OnPostUpdateHeaderAsync(CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty) throw new BadRequestAppException("Invalid invoice id.");

                Header.Items = null;

                var vr = await _updateValidator.ValidateAsync(Header, ct);
                if (!vr.IsValid)
                {
                    foreach (var err in vr.Errors)
                        ModelState.AddModelError("Header." + err.PropertyName, err.ErrorMessage);

                    await OnGetAsync(ct);
                    TempData["Toast.Type"] = "error";
                    TempData["Toast.Title"] = "Validation";
                    TempData["Toast.Message"] = "Please fix the highlighted fields.";
                    return Page();
                }

                await _invoices.UpdateAsync(Id, Header, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Saved";
                TempData["Toast.Message"] = "Invoice updated.";

                return RedirectToPage("./Edit", new { id = Id });
            }
            catch (BadRequestAppException ex)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Not allowed";
                TempData["Toast.Message"] = ex.Message;
                await OnGetAsync(ct);
                return Page();
            }
        }

        public async Task<IActionResult> OnPostAddItemAsync(CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty) throw new BadRequestAppException("Invalid invoice id.");

                Invoice = await _invoices.GetInvoiceAsync(Id, ct);
                if (Invoice is null) throw new NotFoundAppException("Invoice not found.");
                if (Invoice.Status != DocumentStatus.Draft)
                    throw new BadRequestAppException("Items are locked because invoice is not Draft.");

                NewItem.InvoiceId = Id;

                JsonDocument config;
                try
                {
                    config = JsonDocument.Parse(string.IsNullOrWhiteSpace(NewItemConfigJson) ? "{}" : NewItemConfigJson);
                }
                catch
                {
                    ModelState.AddModelError(nameof(NewItemConfigJson), "Invalid JSON.");
                    await OnGetAsync(ct);
                    await LoadLookupsAsync(ct);
                    return Page();
                }

                NewItem.Item.Config = config;

                var vr = await _createItemValidator.ValidateAsync(NewItem, ct);
                if (!vr.IsValid)
                {
                    foreach (var err in vr.Errors)
                        ModelState.AddModelError("NewItem." + err.PropertyName, err.ErrorMessage);

                    await OnGetAsync(ct);
                    TempData["Toast.Type"] = "error";
                    TempData["Toast.Title"] = "Validation";
                    TempData["Toast.Message"] = "Please fix the item fields.";
                    return Page();
                }

                await _invoices.CreateItemAsync(NewItem, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Added";
                TempData["Toast.Message"] = "Item added.";

                return RedirectToPage("./Edit", new { id = Id });
            }
            catch (Exception ex) when (ex is BadRequestAppException or NotFoundAppException)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Error";
                TempData["Toast.Message"] = ex.Message;

                await OnGetAsync(ct);
                return Page();
            }
        }

        public async Task<IActionResult> OnPostUpdateItemAsync(CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty) throw new BadRequestAppException("Invalid invoice id.");

                Invoice = await _invoices.GetInvoiceAsync(Id, ct);
                if (Invoice is null) throw new NotFoundAppException("Invoice not found.");
                if (Invoice.Status != DocumentStatus.Draft)
                    throw new BadRequestAppException("Items are locked because invoice is not Draft.");

                EditItem.InvoiceId = Id;

                JsonDocument config;
                try
                {
                    config = JsonDocument.Parse(string.IsNullOrWhiteSpace(EditItemConfigJson) ? "{}" : EditItemConfigJson);
                }
                catch
                {
                    ModelState.AddModelError(nameof(EditItemConfigJson), "Invalid JSON.");
                    await OnGetAsync(ct);
                    await LoadLookupsAsync(ct);
                    return Page();
                }

                EditItem.Item.Config = config;

                var vr = await _updateItemValidator.ValidateAsync(EditItem, ct);
                if (!vr.IsValid)
                {
                    foreach (var err in vr.Errors)
                        ModelState.AddModelError("EditItem." + err.PropertyName, err.ErrorMessage);

                    TempData["Toast.Type"] = "error";
                    TempData["Toast.Title"] = "Validation";
                    TempData["Toast.Message"] = "Please fix the item fields.";

                    await OnGetAsync(ct);
                    return Page();
                }

                await _invoices.UpdateItemAsync(EditItem, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Saved";
                TempData["Toast.Message"] = "Item updated.";

                return RedirectToPage("./Edit", new { id = Id });
            }
            catch (Exception ex) when (ex is BadRequestAppException or NotFoundAppException)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Error";
                TempData["Toast.Message"] = ex.Message;

                await OnGetAsync(ct);
                return RedirectToPage("./Edit", new { id = Id });
            }
        }

        public async Task<IActionResult> OnPostDeleteItemAsync(Guid itemId, CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty) throw new BadRequestAppException("Invalid invoice id.");
                if (itemId == Guid.Empty) throw new BadRequestAppException("Invalid item id.");

                Invoice = await _invoices.GetInvoiceAsync(Id, ct);
                if (Invoice is null) throw new NotFoundAppException("Invoice not found.");
                if (Invoice.Status != DocumentStatus.Draft)
                    throw new BadRequestAppException("Items are locked because invoice is not Draft.");

                await _invoices.DeleteItemAsync(new DeleteInvoiceItemDto { InvoiceId = Id, ItemId = itemId }, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Deleted";
                TempData["Toast.Message"] = "Item deleted.";

                return RedirectToPage("./Edit", new { id = Id });
            }
            catch (Exception ex) when (ex is BadRequestAppException or NotFoundAppException)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Error";
                TempData["Toast.Message"] = ex.Message;
                return RedirectToPage("./Edit", new { id = Id });
            }
        }

        public async Task<IActionResult> OnPostDeleteInvoiceAsync(CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty) throw new BadRequestAppException("Invalid invoice id.");

                // capture projectId for smart redirect
                var inv = await _invoices.GetInvoiceAsync(Id, ct);
                if (inv is null) throw new NotFoundAppException("Invoice not found.");

                await _invoices.DeleteAsync(Id, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Deleted";
                TempData["Toast.Message"] = "Invoice deleted.";

                return RedirectToPage("/Projects", new { openProjectId = inv.ProjectId, tab = "overview" });
            }
            catch (Exception ex) when (ex is BadRequestAppException or NotFoundAppException)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Error";
                TempData["Toast.Message"] = ex.Message;
                return RedirectToPage("./Edit", new { id = Id });
            }
        }

        private async Task LoadLookupsAsync(CancellationToken ct)
        {
            TaxRateOptions = await _db.TaxRates
                .Where(t => t.IsActive)
                .OrderBy(t => t.Name)
                .Select(t => new SelectListItem($"{t.Name} ({t.RatePercent}%)", t.Id.ToString()))
                .ToListAsync(ct);
        }
    }
}
