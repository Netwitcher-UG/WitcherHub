using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Quotes;
using WitcherHub.Application.Models.View.Quotes;
using WitcherHub.Infrastructure.Data.Context;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Quotes
{
    [Authorize]
    public class EditModel : PageModel
    {
        private readonly IQuote _quotes;
        private readonly IValidator<UpdateQuoteDto> _updateValidator;
        private readonly IValidator<CreateQuoteItemDto> _createItemValidator;
        private readonly IValidator<UpdateQuoteItemDto> _updateItemValidator;

        // Taxes lookup (temporary, like Create page)
        private readonly AppDbContext _db;

        public EditModel(
            IQuote quotes,
            IValidator<UpdateQuoteDto> updateValidator,
            IValidator<CreateQuoteItemDto> createItemValidator,
            IValidator<UpdateQuoteItemDto> updateItemValidator,
            AppDbContext db)
        {
            _quotes = quotes;
            _updateValidator = updateValidator;
            _createItemValidator = createItemValidator;
            _updateItemValidator = updateItemValidator;
            _db = db;
        }

        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; } // QuoteId

        public QuoteViews.QuoteDetailsView? Quote { get; private set; }

        public List<SelectListItem> TaxRateOptions { get; private set; } = new();

        // Header edit form
        [BindProperty]
        public UpdateQuoteDto Header { get; set; } = new()
        {
         
            Quote = new QuoteDto
            {
                Currency = "EUR",
                Status = DocumentStatus.Draft,
                AfterCustomerSignAction = QuoteAfterSignAction.Contract,
                InvoiceSendMode = InvoiceSendMode.Automatic
            },
            Items = null // ✅ important
        };

        // Add item form
        [BindProperty]
        public CreateQuoteItemDto NewItem { get; set; } = new();

        [BindProperty]
        public string NewItemConfigJson { get; set; } = "{}";

        // Update item form (modal posts here)
        [BindProperty]
        public UpdateQuoteItemDto EditItem { get; set; } = new();

        [BindProperty]
        public string EditItemConfigJson { get; set; } = "{}";

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (Id == Guid.Empty) return NotFound();

            Quote = await _quotes.GetQuoteAsync(Id, ct);
            if (Quote is null) return NotFound();

            // fill header form from details
            Header.Quote.ProjectId = Quote.ProjectId;
            Header.Quote.Currency = Quote.Currency;
            Header.Quote.Notes = Quote.Notes;
            Header.Quote.IssuedAt = Quote.IssuedAt;
            Header.Quote.ExpiresAt = Quote.ExpiresAt;
            Header.Quote.Status = Quote.Status;
            Header.Quote.ApplyVat = Quote.ApplyVat;
            Header.Quote.AfterCustomerSignAction = Quote.AfterCustomerSignAction;
            Header.Quote.InvoiceSendMode = Quote.InvoiceSendMode;
            // prepare new item defaults
            NewItem.QuoteId = Quote.Id;
            NewItem.Item.Position = (Quote.Items?.Count ?? 0) + 1;
            NewItem.Item.Quantity = 1;
            NewItem.Item.UnitPrice = 0;
            NewItem.Item.Title = "";
            NewItem.Item.DiscountType = null;
            NewItem.Item.DiscountValue = null;
            NewItemConfigJson = "{}";

            // modal defaults (empty; UI will fill using JS when clicking Edit)
            EditItem.QuoteId = Quote.Id;
            EditItemConfigJson = "{}";

            return Page();
        }

        // =========================
        // POST: Update Header
        // =========================
        public async Task<IActionResult> OnPostUpdateHeaderAsync(CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty) throw new BadRequestAppException("Invalid quote id.");

                Header.Items = null; // ✅ do not replace items
                if (Header.Quote.AfterCustomerSignAction != QuoteAfterSignAction.Invoice)
                    Header.Quote.InvoiceSendMode = InvoiceSendMode.Automatic;
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

                await _quotes.UpdateAsync(Id, Header, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Saved";
                TempData["Toast.Message"] = "Quote updated.";

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

        // =========================
        // POST: Add Item
        // =========================
        public async Task<IActionResult> OnPostAddItemAsync(CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty) throw new BadRequestAppException("Invalid quote id.");

                Quote = await _quotes.GetQuoteAsync(Id, ct);
                if (Quote is null) throw new NotFoundAppException("Quote not found.");
                if (Quote.Status != DocumentStatus.Draft)
                    throw new BadRequestAppException("Items are locked because quote is not Draft.");

                NewItem.QuoteId = Id;

                // Parse NewItem config JSON
                JsonDocument config;
                try
                {
                    config = JsonDocument.Parse(string.IsNullOrWhiteSpace(NewItemConfigJson) ? "{}" : NewItemConfigJson);
                }
                catch
                {
                    ModelState.AddModelError(nameof(NewItemConfigJson), "Invalid JSON.");
                    await OnGetAsync(ct);
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

                await _quotes.CreateItemAsync(NewItem, ct);

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

        // =========================
        // POST: Update Item
        // =========================
        public async Task<IActionResult> OnPostUpdateItemAsync(CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty) throw new BadRequestAppException("Invalid quote id.");

                Quote = await _quotes.GetQuoteAsync(Id, ct);
                if (Quote is null) throw new NotFoundAppException("Quote not found.");
                if (Quote.Status != DocumentStatus.Draft)
                    throw new BadRequestAppException("Items are locked because quote is not Draft.");

                EditItem.QuoteId = Id;

                // Parse Edit config JSON
                JsonDocument config;
                try
                {
                    config = JsonDocument.Parse(string.IsNullOrWhiteSpace(EditItemConfigJson) ? "{}" : EditItemConfigJson);
                }
                catch
                {
                    ModelState.AddModelError(nameof(EditItemConfigJson), "Invalid JSON.");
                    await OnGetAsync(ct);
                    return Page();
                }

                // ✅ SAFETY
                EditItem.Item ??= new();
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

                await _quotes.UpdateItemAsync(EditItem, ct);

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
                return Page();
            }
        }

        // =========================
        // POST: Delete Item
        // =========================
        public async Task<IActionResult> OnPostDeleteItemAsync(Guid itemId, CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty) throw new BadRequestAppException("Invalid quote id.");
                if (itemId == Guid.Empty) throw new BadRequestAppException("Invalid item id.");

                Quote = await _quotes.GetQuoteAsync(Id, ct);
                if (Quote is null) throw new NotFoundAppException("Quote not found.");
                if (Quote.Status != DocumentStatus.Draft)
                    throw new BadRequestAppException("Items are locked because quote is not Draft.");

                await _quotes.DeleteItemAsync(new DeleteQuoteItemDto { QuoteId = Id, ItemId = itemId }, ct);

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

        // =========================
        // POST: Delete Quote
        // =========================
        public async Task<IActionResult> OnPostDeleteQuoteAsync(CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty) throw new BadRequestAppException("Invalid quote id.");

                // get projectId before delete
                var q = await _quotes.GetQuoteAsync(Id, ct);
                if (q is null) throw new NotFoundAppException("Quote not found.");

                var projectId = q.ProjectId;

                await _quotes.DeleteAsync(Id, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Deleted";
                TempData["Toast.Message"] = "Quote deleted.";

                return RedirectToPage("/Projects", new { openProjectId = projectId, openTab = "quotes" });
            }
            catch (Exception ex) when (ex is BadRequestAppException or NotFoundAppException)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Error";
                TempData["Toast.Message"] = ex.Message;
                return RedirectToPage("./Edit", new { id = Id });
            }
        }
    }
}
