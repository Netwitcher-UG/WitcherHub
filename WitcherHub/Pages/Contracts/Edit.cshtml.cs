using FluentValidation;
using WitcherHub.Rendering;
using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using System.Text.RegularExpressions;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces;
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

        private readonly IContractDraftService _drafts;

        public EditModel(
            IContract contracts,
            IContractDraftService drafts,
            IValidator<UpdateContractDto> updateValidator,
            IValidator<CreateContractItemDto> createItemValidator,
            IValidator<UpdateContractItemDto> updateItemValidator)
        {
            _contracts = contracts;
            _drafts = drafts;
            _updateValidator = updateValidator;
            _createItemValidator = createItemValidator;
            _updateItemValidator = updateItemValidator;
        }

        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; } // ContractId

        public string ContractHtml { get; private set; } = "";
        public string RawTerms { get; private set; } = "";        
        public string RawTermsEditable { get; private set; } = "";
        public bool IsSigned { get; private set; }

        public ContractViews.ContractDetailsView? Contract { get; private set; }

        /// <summary>
        /// Where the contract's versions stand: how many there are, which is
        /// approved, and whether the wording on screen is the approved one.
        ///
        /// The editor showed a contract number, a status and a currency. Which
        /// version was being edited, whether it had been approved, and what the
        /// contract was worth were all absent from a page whose whole job is
        /// editing that contract.
        /// </summary>
        public IReadOnlyList<ContractDraftSummary> Drafts { get; private set; }
            = Array.Empty<ContractDraftSummary>();

        public ContractDraftSummary? ApprovedVersion => Drafts.FirstOrDefault(d => d.IsApproved);

        public ContractMoneyDto? Money { get; private set; }

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

            // The version history and the contract's own money, so the editor can
            // say which version is on screen and what the contract is worth.
            Drafts = await _drafts.GetDraftsAsync(Id, ct);

            var state = await _drafts.GetStateAsync(Id, ct);
            Money = state.Money;

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

            // ✅ FULL terms (للعرض/الطباعة)
            RawTerms = (Header.Contract.Terms ?? "").Replace("\r\n", "\n");

            // ✅ EDITOR terms (بدون القسم الثابت)
            var parts = SplitLockedAgbForEditor(RawTerms);
            RawTermsEditable = parts.editable;

            // Render FULL Terms -> HTML (Markdown)
            ContractHtml = RenderMarkdownToSafeHtml(RawTerms);

            // Defaults for new item
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

            return ContractMarkdown.ToHtml(markdown);
        }

        public async Task<IActionResult> OnPostUpdateHeaderAsync(CancellationToken ct)
        {
            try
            {
                if (Id == Guid.Empty) throw new BadRequestAppException("Invalid contract id.");

                // احضر النسخة الحالية FULL حتى نثبت القسم الثابت
                var current = await _contracts.GetContractAsync(Id, ct);
                if (current is null) throw new NotFoundAppException("Contract not found.");

                if (current.Status != DocumentStatus.Draft)
                    throw new BadRequestAppException("Contract is locked because status is not Draft.");

                Header.Items = null;

                // النص القادم من الـ editor = فقط الجزء المتغير
                var editableFromForm = (Header.Contract.Terms ?? "").Replace("\r\n", "\n").TrimEnd();

                // استخرج القسم الثابت (Anlage B / AGB) من النسخة الحالية
                var currentFull = (current.Terms ?? "").Replace("\r\n", "\n");
                var (locked, _) = SplitLockedAgbForEditor(currentFull);

                // ✅ حافظ على ترتيبك: المتغير أولاً ثم الثابت في النهاية
                Header.Contract.Terms = string.IsNullOrWhiteSpace(locked)
                    ? editableFromForm
                    : (editableFromForm + "\n\n" + locked.TrimStart());

                // ثبت حقول الهيدر كما هي (حتى لا تتغير)
                Header.Contract.ProjectId = current.ProjectId;
                Header.Contract.Currency = current.Currency;
                Header.Contract.Status = current.Status;
                Header.Contract.StartDate = current.StartDate;
                Header.Contract.EndDate = current.EndDate;
                Header.Contract.SignedAt = current.SignedAt;

                var vr = await _updateValidator.ValidateAsync(Header, ct);
                if (!vr.IsValid)
                {
                    foreach (var err in vr.Errors)
                        ModelState.AddModelError("Header." + err.PropertyName, err.ErrorMessage);

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
        private static (string locked, string editable) SplitLockedAgbForEditor(string terms)
        {
            terms ??= "";
            terms = terms.Replace("\r\n", "\n");

            // 1) الأفضل: قص من عنوان Anlage B (Heading) إلى نهاية المستند
            // هذا بالضبط مكان الـ AGB عندك
            var m = Regex.Match(terms, @"(?im)^\s*##\s*Anlage\s*B\b.*$", RegexOptions.CultureInvariant);
            if (m.Success)
            {
                var editable = terms.Substring(0, m.Index).TrimEnd();
                var locked = terms.Substring(m.Index).TrimStart(); // يشمل heading + كل AGB
                return (locked, editable);
            }

            // 2) fallback: إذا ما لقى heading، قص من أول <h1><strong>Allgemeine Geschäftsbedingungen</strong> إلى نهاية المستند
            var idx = terms.IndexOf("<h1><strong>Allgemeine Geschäftsbedingungen", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var editable = terms.Substring(0, idx).TrimEnd();
                var locked = terms.Substring(idx).TrimStart();
                return (locked, editable);
            }

            // 3) لا يوجد AGB => كل شيء editable
            return ("", terms.TrimEnd());
        }

    }
}
