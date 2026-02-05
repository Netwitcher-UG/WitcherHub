using FluentValidation;
using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Models.View.Contracts;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Contracts
{
    [Authorize]
    public class EditModel : PageModel
    {
        private readonly IContract _contracts;
        private readonly IValidator<UpdateContractDto> _updateValidator;
        private readonly IValidator<CreateContractItemDto> _createItemValidator;
        private readonly IValidator<UpdateContractItemDto> _updateItemValidator;

        public EditModel(
            IContract contracts,
            IValidator<UpdateContractDto> updateValidator,
            IValidator<CreateContractItemDto> createItemValidator,
            IValidator<UpdateContractItemDto> updateItemValidator)
        {
            _contracts = contracts;
            _updateValidator = updateValidator;
            _createItemValidator = createItemValidator;
            _updateItemValidator = updateItemValidator;
        }

        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; } // ContractId

        public string ContractHtml { get; private set; } = "";
        public string RawTerms { get; private set; } = "";
        public bool IsSigned { get; private set; }

        public ContractViews.ContractDetailsView? Contract { get; private set; }

        [BindProperty]
        public UpdateContractDto Header { get; set; } = new()
        {
            Contract = new ContractDto
            {
                Currency = "EUR",
                Status = DocumentStatus.Draft,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
                SignedAt = null,
                Terms = null
            },
            Items = null
        };

        [BindProperty]
        public CreateContractItemDto NewItem { get; set; } = new();

        [BindProperty]
        public string NewItemConfigJson { get; set; } = "{}";

        [BindProperty]
        public UpdateContractItemDto EditItem { get; set; } = new();

        [BindProperty]
        public string EditItemConfigJson { get; set; } = "{}";

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (Id == Guid.Empty) return NotFound();

            await LoadPageStateAsync(Id, ct, termsOverride: null);
            if (Contract is null) return NotFound();

            return Page();
        }

        private async Task LoadPageStateAsync(Guid contractId, CancellationToken ct, string? termsOverride)
        {
            Contract = await _contracts.GetContractAsync(contractId, ct);
            if (Contract is null) return;

            // Map View -> Header DTO
            Header.Contract.ProjectId = Contract.ProjectId;
            Header.Contract.Currency = Contract.Currency;
            Header.Contract.Status = Contract.Status;
            Header.Contract.StartDate = Contract.StartDate;
            Header.Contract.EndDate = Contract.EndDate;
            Header.Contract.SignedAt = Contract.SignedAt;

            // Terms: use override if provided (important for validation failure)
            Header.Contract.Terms = termsOverride ?? Contract.Terms;

            // Safety defaults
            Header.Contract.StartDate ??= DateOnly.FromDateTime(DateTime.UtcNow);
            Header.Contract.EndDate ??= DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1));

            // Signed badge
            IsSigned = Contract.Status == DocumentStatus.Signed || Contract.SignedAt is not null;

            // Raw terms used by inline editor
            RawTerms = Header.Contract.Terms ?? "";

            // Render Terms -> HTML (Markdown)
            ContractHtml = RenderMarkdownToSafeHtml(RawTerms);

            // Defaults for new item (kept as-is if you still use them later)
            NewItem.ContractId = Contract.Id;
            NewItem.Item.Position = (Contract.Items?.Count ?? 0) + 1;
            NewItem.Item.Title = "";
            NewItem.Item.ServiceId = null;
            NewItem.Item.AgreedPrice = null;
            NewItemConfigJson = "{}";

            EditItem.ContractId = Contract.Id;
            EditItemConfigJson = "{}";
        }

        private static string RenderMarkdownToSafeHtml(string markdown)
        {
            markdown ??= "";
            markdown = markdown.Replace("\r\n", "\n");

            var pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();

            var html = Markdown.ToHtml(markdown, pipeline);

            var sanitizer = new HtmlSanitizer();
            sanitizer.AllowedSchemes.Add("mailto");
            return sanitizer.Sanitize(html);
        }

        public async Task<IActionResult> OnPostUpdateHeaderAsync(CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty) throw new BadRequestAppException("Invalid contract id.");

                Header.Items = null;

                var vr = await _updateValidator.ValidateAsync(Header, ct);
                if (!vr.IsValid)
                {
                    foreach (var err in vr.Errors)
                        ModelState.AddModelError("Header." + err.PropertyName, err.ErrorMessage);

                    // keep user's edited terms
                    await LoadPageStateAsync(Id, ct, termsOverride: Header.Contract.Terms);

                    TempData["Toast.Type"] = "error";
                    TempData["Toast.Title"] = "Validation";
                    TempData["Toast.Message"] = "Please fix the highlighted fields.";
                    return Page();
                }

                await _contracts.UpdateAsync(Id, Header, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Saved";
                TempData["Toast.Message"] = "Contract updated.";

                return RedirectToPage("./Edit", new { id = Id });
            }
            catch (BadRequestAppException ex)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Not allowed";
                TempData["Toast.Message"] = ex.Message;

                await LoadPageStateAsync(Id, ct, termsOverride: Header.Contract.Terms);
                return Page();
            }
            catch (NotFoundAppException ex)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Not found";
                TempData["Toast.Message"] = ex.Message;

                return NotFound();
            }
        }

        public async Task<IActionResult> OnPostAddItemAsync(CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty) throw new BadRequestAppException("Invalid contract id.");

                Contract = await _contracts.GetContractAsync(Id, ct);
                if (Contract is null) throw new NotFoundAppException("Contract not found.");
                if (Contract.Status != DocumentStatus.Draft)
                    throw new BadRequestAppException("Items are locked because contract is not Draft.");

                NewItem.ContractId = Id;

                JsonDocument config;
                try
                {
                    config = JsonDocument.Parse(string.IsNullOrWhiteSpace(NewItemConfigJson) ? "{}" : NewItemConfigJson);
                }
                catch
                {
                    ModelState.AddModelError(nameof(NewItemConfigJson), "Invalid JSON.");
                    await LoadPageStateAsync(Id, ct, termsOverride: null);
                    return Page();
                }

                NewItem.Item.Config = config;

                var vr = await _createItemValidator.ValidateAsync(NewItem, ct);
                if (!vr.IsValid)
                {
                    foreach (var err in vr.Errors)
                        ModelState.AddModelError("NewItem." + err.PropertyName, err.ErrorMessage);

                    await LoadPageStateAsync(Id, ct, termsOverride: null);

                    TempData["Toast.Type"] = "error";
                    TempData["Toast.Title"] = "Validation";
                    TempData["Toast.Message"] = "Please fix the item fields.";
                    return Page();
                }

                await _contracts.CreateItemAsync(NewItem, ct);

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

                await LoadPageStateAsync(Id, ct, termsOverride: null);
                return Page();
            }
        }

        public async Task<IActionResult> OnPostUpdateItemAsync(CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty) throw new BadRequestAppException("Invalid contract id.");

                Contract = await _contracts.GetContractAsync(Id, ct);
                if (Contract is null) throw new NotFoundAppException("Contract not found.");
                if (Contract.Status != DocumentStatus.Draft)
                    throw new BadRequestAppException("Items are locked because contract is not Draft.");

                EditItem.ContractId = Id;

                JsonDocument config;
                try
                {
                    config = JsonDocument.Parse(string.IsNullOrWhiteSpace(EditItemConfigJson) ? "{}" : EditItemConfigJson);
                }
                catch
                {
                    ModelState.AddModelError(nameof(EditItemConfigJson), "Invalid JSON.");
                    await LoadPageStateAsync(Id, ct, termsOverride: null);
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

                    await LoadPageStateAsync(Id, ct, termsOverride: null);
                    return Page();
                }

                await _contracts.UpdateItemAsync(EditItem, ct);

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

                await LoadPageStateAsync(Id, ct, termsOverride: null);
                return RedirectToPage("./Edit", new { id = Id });
            }
        }

        public async Task<IActionResult> OnPostDeleteItemAsync(Guid itemId, CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty) throw new BadRequestAppException("Invalid contract id.");
                if (itemId == Guid.Empty) throw new BadRequestAppException("Invalid item id.");

                Contract = await _contracts.GetContractAsync(Id, ct);
                if (Contract is null) throw new NotFoundAppException("Contract not found.");
                if (Contract.Status != DocumentStatus.Draft)
                    throw new BadRequestAppException("Items are locked because contract is not Draft.");

                await _contracts.DeleteItemAsync(new DeleteContractItemDto { ContractId = Id, ItemId = itemId }, ct);

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

        public async Task<IActionResult> OnPostDeleteContractAsync(CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty) throw new BadRequestAppException("Invalid contract id.");

                var c = await _contracts.GetContractAsync(Id, ct);
                if (c is null) throw new NotFoundAppException("Contract not found.");

                await _contracts.DeleteAsync(Id, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Deleted";
                TempData["Toast.Message"] = "Contract deleted.";

                // ✅ ارجعه للـ Projects على تبويب contracts
                return RedirectToPage("/Projects", new { openProjectId = c.ProjectId, tab = "contracts" });
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
