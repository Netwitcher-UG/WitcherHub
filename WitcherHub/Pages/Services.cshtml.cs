using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Services;
using WitcherHub.Application.Models.View.Services;
using WitcherHub.Pages.Models.UI;

namespace WitcherHub.Pages
{
    public class ServicesModel : PageModel
    {
        private readonly IServiceCatalog _services;

        private readonly IValidator<ServiceCatalogDTOs> _createValidator;
        private readonly IValidator<UpdateServiceCatalogItemDto> _updateValidator;

        private readonly IValidator<CreatePricingRuleDto> _createRuleValidator;
        private readonly IValidator<UpdatePricingRuleDto> _updateRuleValidator;
        private readonly IValidator<DeletePricingRuleDto> _deleteRuleValidator;

        public ServicesModel(
            IServiceCatalog services,
            IValidator<ServiceCatalogDTOs> createValidator,
            IValidator<UpdateServiceCatalogItemDto> updateValidator,
            IValidator<CreatePricingRuleDto> createRuleValidator,
            IValidator<UpdatePricingRuleDto> updateRuleValidator,
            IValidator<DeletePricingRuleDto> deleteRuleValidator
        )
        {
            _services = services;

            _createValidator = createValidator;
            _updateValidator = updateValidator;

            _createRuleValidator = createRuleValidator;
            _updateRuleValidator = updateRuleValidator;
            _deleteRuleValidator = deleteRuleValidator;
        }

        // query-string (pagination/search)
        [BindProperty(SupportsGet = true, Name = "p")] public new int Page { get; set; } = 1;
        [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 10;
        [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }

        public TableCardVm ServicesTable { get; private set; } = new();

        // Create form fields
        [BindProperty] public ServiceCatalogItemDto Service { get; set; } = new();
        [BindProperty] public List<PricingRuleDto> PricingRules { get; set; } = new();

        public ModalVm CreateServiceModal { get; private set; } = new();

        public async Task OnGetAsync(CancellationToken ct)
        {
            EnsureDefaults();

            ViewData["p"] = Page;
            ViewData["pageSize"] = PageSize;
            ViewData["q"] = Search;

            await LoadTableAsync(ct);
            BuildCreateServiceModal(autoOpen: false);
        }

        private void EnsureDefaults()
        {
            if (string.IsNullOrWhiteSpace(Service.DefaultCurrency))
                Service.DefaultCurrency = "EUR";

            if (Service.BasePrice < 0) Service.BasePrice = 0;
        }

        // =========================
        // POST: Create (normal form)
        // =========================
        public async Task<IActionResult> OnPostAsync(CancellationToken ct)
        {
            EnsureDefaults();
            await LoadTableAsync(ct);

            var dto = new ServiceCatalogDTOs
            {
                Service = Service,
                PricingRules = PricingRules ?? new()
            };

            var result = await _createValidator.ValidateAsync(dto, ct);
            if (!result.IsValid)
            {
                foreach (var err in result.Errors)
                    ModelState.AddModelError(err.PropertyName, err.ErrorMessage);

                BuildCreateServiceModal(autoOpen: true);

                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Validation";
                TempData["Toast.Message"] = "Please fix the highlighted fields.";

                return Page();
            }

            await _services.CreateAsync(dto, ct);

            TempData["Toast.Type"] = "success";
            TempData["Toast.Title"] = "Success";
            TempData["Toast.Message"] = "Service added successfully.";

            return RedirectToPage("./Services", new { p = Page, pageSize = PageSize, q = Search });
        }

        // =========================
        // POST: Delete (normal form)
        // =========================
        public async Task<IActionResult> OnPostDeleteServiceAsync(Guid serviceId, CancellationToken ct)
        {
            if (serviceId == Guid.Empty)
                throw new BadRequestAppException("Invalid service id.");

            await _services.DeleteAsync(serviceId, ct);

            TempData["Toast.Type"] = "success";
            TempData["Toast.Title"] = "Deleted";
            TempData["Toast.Message"] = "Service deleted successfully.";

            return RedirectToPage("./Services", new { p = Page, pageSize = PageSize, q = Search });
        }

        // =========================
        // GET: Details (Ajax JSON)
        // =========================
        public async Task<IActionResult> OnGetServiceAsync(Guid id, CancellationToken ct)
        {
            if (id == Guid.Empty)
                throw new BadRequestAppException("Invalid service id.");

            var service = await _services.GetServiceAsync(id, ct);
            if (service is null)
                throw new NotFoundAppException("Service not found.");

            return new JsonResult(service);
        }

        // =========================
        // POST: Update Basic (Ajax JSON)
        // =========================
        public class UpdateServiceBasicRequest
        {
            public Guid ServiceId { get; set; }
            public ServiceCatalogItemDto Service { get; set; } = new();
        }

        public async Task<IActionResult> OnPostUpdateBasicAsync([FromBody] UpdateServiceBasicRequest? req, CancellationToken ct)
        {
            if (req is null)
                return BadRequest(new { message = "Body is null." });

            if (req.ServiceId == Guid.Empty)
                return BadRequest(new { message = "ServiceId is empty." });

            var updateDto = new UpdateServiceCatalogItemDto { Service = req.Service };

            var vr = await _updateValidator.ValidateAsync(updateDto, ct);
            if (!vr.IsValid)
                return BadRequest(new
                {
                    message = "Validation failed",
                    errors = vr.Errors.Select(e => new { field = e.PropertyName, error = e.ErrorMessage })
                });

            await _services.UpdateAsync(req.ServiceId, updateDto, ct);

            var updated = await _services.GetServiceAsync(req.ServiceId, ct);
            return new JsonResult(updated);
        }

        // =========================
        // Pricing Rules (Ajax JSON)
        // =========================
        public async Task<IActionResult> OnPostAddPricingRuleAsync([FromBody] CreatePricingRuleDto req, CancellationToken ct)
        {
            if (req is null)
                return BadRequest(new { message = "Body is null." });

            var vr = await _createRuleValidator.ValidateAsync(req, ct);
            if (!vr.IsValid)
                return BadRequest(new
                {
                    message = "Validation failed",
                    errors = vr.Errors.Select(e => new { field = e.PropertyName, error = e.ErrorMessage })
                });

            await _services.CreateRuleAsync(req, ct);

            var updated = await _services.GetServiceAsync(req.ServiceId, ct);
            return new JsonResult(updated);
        }

        public async Task<IActionResult> OnPostUpdatePricingRuleAsync([FromBody] UpdatePricingRuleDto req, CancellationToken ct)
        {
            if (req is null)
                return BadRequest(new { message = "Body is null." });

            var vr = await _updateRuleValidator.ValidateAsync(req, ct);
            if (!vr.IsValid)
                return BadRequest(new
                {
                    message = "Validation failed",
                    errors = vr.Errors.Select(e => new { field = e.PropertyName, error = e.ErrorMessage })
                });

            await _services.UpdateRuleAsync(req, ct);

            var updated = await _services.GetServiceAsync(req.ServiceId, ct);
            return new JsonResult(updated);
        }

        public async Task<IActionResult> OnPostDeletePricingRuleAsync([FromBody] DeletePricingRuleDto req, CancellationToken ct)
        {
            if (req is null)
                return BadRequest(new { message = "Body is null." });

            var vr = await _deleteRuleValidator.ValidateAsync(req, ct);
            if (!vr.IsValid)
                return BadRequest(new
                {
                    message = "Validation failed",
                    errors = vr.Errors.Select(e => new { field = e.PropertyName, error = e.ErrorMessage })
                });

            await _services.DeleteRuleAsync(req, ct);

            var updated = await _services.GetServiceAsync(req.ServiceId, ct);
            return new JsonResult(updated);
        }

        // =========================
        // Table loading
        // =========================
        private async Task LoadTableAsync(CancellationToken ct)
        {
            var res = await _services.GetServicesAsync(Page, PageSize, Search, ct);

            ServicesTable = new TableCardVm
            {
                Title = "Services",
                PrimaryButtonText = "Add Service",
                PrimaryButtonTarget = "#FormModal",
                SearchPlaceholder = "Search services...",
                Pagination = new PaginationVm
                {
                    Page = res.Page,
                    PageSize = res.PageSize,
                    TotalItems = res.TotalItems,
                    SearchQuery = Search,
                    SearchParam = "q",
                    PageParam = "p",
                    PageSizeParam = "pageSize"
                },
                Columns =
                {
                    new() { Header="Name",        Width="24%", HeaderClass="ps-4", CellClass="ps-4 fw-semibold" },
                    new() { Header="Type",        Width="12%" },
                    new() { Header="Pricing",     Width="12%" },
                    new() { Header="Base Price",  Width="12%" },
                    new() { Header="Rules",       Width="8%"  },
                    new() { Header="Currency",    Width="10%" },
                    new() { Header="Active",      Width="10%" },
                    new() { Header="Actions",     Width="12%", HeaderClass="text-end pe-4", CellClass="text-end pe-4" },
                }
            };

            foreach (var s in res.Items)
            {
                ServicesTable.Rows.Add(new TableRowVm
                {
                    Cells =
                    {
                        Html(s.Name),
                        TypeBadge(s.ServiceType.ToString()),
                        Html(s.PricingModel.ToString()),
                        Html(s.BasePrice.ToString("0.##")),
                        RulesBadge(s.RulesCount),
                        Html(s.DefaultCurrency),
                        ActiveBadge(s.IsActive),
                        ActionsButtons(s.Id.ToString())
                    }
                });
            }
        }

        private void BuildCreateServiceModal(bool autoOpen)
        {
            CreateServiceModal = new ModalVm
            {
                Id = "FormModal",
                Title = "Add Service",
                SizeClass = "modal-xl",
                SubmitText = "Save",
                CancelText = "Cancel",
                Handler = null, // OnPostAsync
                BodyPartialPath = "~/Pages/Shared/Modals/_CreateServiceFields.cshtml",
                BodyModel = this,
                AutoOpen = autoOpen
            };
        }

        // ========= helpers =========
        private static Microsoft.AspNetCore.Html.IHtmlContent Html(string? text)
            => new Microsoft.AspNetCore.Html.HtmlString(System.Text.Encodings.Web.HtmlEncoder.Default.Encode(text ?? ""));

        private static Microsoft.AspNetCore.Html.IHtmlContent TypeBadge(string type)
        {
            var cls = "badge bg-info bg-opacity-10 text-info";
            return new Microsoft.AspNetCore.Html.HtmlString($"<span class='{cls}'>{System.Text.Encodings.Web.HtmlEncoder.Default.Encode(type)}</span>");
        }

        private static Microsoft.AspNetCore.Html.IHtmlContent ActiveBadge(bool active)
        {
            var (cls, txt) = active
                ? ("badge bg-success bg-opacity-10 text-success", "Active")
                : ("badge bg-secondary bg-opacity-10 text-secondary", "Inactive");

            return new Microsoft.AspNetCore.Html.HtmlString($"<span class='{cls}'>{txt}</span>");
        }

        private static Microsoft.AspNetCore.Html.IHtmlContent RulesBadge(int count)
        {
            var cls = count > 0
                ? "badge bg-primary bg-opacity-10 text-primary"
                : "badge bg-secondary bg-opacity-10 text-secondary";

            return new Microsoft.AspNetCore.Html.HtmlString($"<span class='{cls}'>{count}</span>");
        }

        private static string Enc(string? v) => System.Text.Encodings.Web.HtmlEncoder.Default.Encode(v ?? "");

        // ✅ نفس اللي بالصورة عندك
        private static Microsoft.AspNetCore.Html.IHtmlContent ActionsButtons(string serviceId)
        {
            return new Microsoft.AspNetCore.Html.HtmlString($$"""
<div class="vc-actions-wrap">
  <button type="button"
          class="btn vc-icon-btn text-secondary"
          title="View"
          data-bs-toggle="modal"
          data-bs-target="#ViewServiceModal"
          data-service-id="{{Enc(serviceId)}}">
      <i class="material-icons-outlined">visibility</i>
  </button>

  <button type="button"
          class="btn vc-icon-btn text-danger"
          title="Delete"
          data-vc-action="table-delete-service"
          data-service-id="{{Enc(serviceId)}}">
      <i class="material-icons-outlined">delete</i>
  </button>
</div>
""");
        }
    }
}
