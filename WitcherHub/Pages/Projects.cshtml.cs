using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Project;
using WitcherHub.Application.Models.View.Project;
using WitcherHub.Application.Models.View.Quotes;
using WitcherHub.Pages.Models.UI;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages
{
    public class ProjectsModel : PageModel
    {
        private readonly IProject _projects;
        private readonly ICustomer _customers;
        private readonly IQuote _quotes;
        private readonly IValidator<CreateProjectDto> _createValidator;
        private readonly IValidator<UpdateProjectDto> _updateValidator;

        public ProjectsModel(
            IProject projects,
            ICustomer customers,
            IQuote quotes,
            IValidator<CreateProjectDto> createValidator,
            IValidator<UpdateProjectDto> updateValidator)
        {
            _projects = projects;
            _customers = customers;
            _quotes = quotes;
            _createValidator = createValidator;
            _updateValidator = updateValidator;
        }

        // query-string
        [BindProperty(SupportsGet = true, Name = "p")] public new int Page { get; set; } = 1;
        [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 10;
        [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }

        [BindProperty(SupportsGet = true)] public Guid OpenProjectId { get; set; }

        [BindProperty(SupportsGet = true)] public string? OpenTab { get; set; }

        // extra filters
        [BindProperty(SupportsGet = true)] public string? CustomerName { get; set; }
        [BindProperty(SupportsGet = true)] public ProjectStatus? Status { get; set; }

        public TableCardVm ProjectsTable { get; private set; } = new();

        // Create form fields
        [BindProperty] public CreateProjectDto Project { get; set; } = new();

        public ModalVm CreateProjectModal { get; private set; } = new();
        public List<SelectListItem> CustomerOptions { get; private set; } = new();

        public async Task OnGetAsync(CancellationToken ct)
        {
            EnsureDefaults();

            ViewData["p"] = Page;
            ViewData["pageSize"] = PageSize;
            ViewData["q"] = Search;
            await LoadCustomersAsync(ct);
            await LoadTableAsync(ct);
            BuildCreateProjectModal(autoOpen: false);
        }

        private object Toast(string type, string title, string message)
            => new { toast = new { type, title, message } };

        private IActionResult ToastBadRequest(string title, string message)
            => BadRequest(Toast("error", title, message));

        private IActionResult ToastNotFound(string title, string message)
            => NotFound(Toast("error", title, message));

        private IActionResult ToastServerError()
            => StatusCode(500, Toast("error", "Server error", "Something went wrong."));

        private void EnsureDefaults()
        {
            Project.Title ??= "";
            if (Project.CustomerId == Guid.Empty) { /* leave empty until user fills */ }

            OpenTab ??= "overview";
            if (OpenTab != "overview" && OpenTab != "quotes")
                OpenTab = "overview";
        }

        // =========================
        // POST: Create
        // =========================
        public async Task<IActionResult> OnPostAsync(CancellationToken ct)
        {
            EnsureDefaults();
            await LoadCustomersAsync(ct);
            await LoadTableAsync(ct);

            var vr = await _createValidator.ValidateAsync(Project, ct);
            if (!vr.IsValid)
            {
                foreach (var err in vr.Errors)
                {
                    var key = err.PropertyName;

                    // لأن الفورم مربوط على Project.X
                    if (!string.IsNullOrWhiteSpace(key) && !key.StartsWith("Project."))
                        key = "Project." + key;

                    ModelState.AddModelError(key, err.ErrorMessage);
                }

                BuildCreateProjectModal(autoOpen: true);

                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Validation";
                TempData["Toast.Message"] = "Please fix the highlighted fields.";

                return Page();
            }

            await _projects.CreateAsync(Project, createdById: null, ct);

            TempData["Toast.Type"] = "success";
            TempData["Toast.Title"] = "Success";
            TempData["Toast.Message"] = "Project created successfully.";

            return RedirectToPage("./Projects", new
            {
                p = Page,
                pageSize = PageSize,
                q = Search,
                customerName = CustomerName,
                status = Status
            });
        }

        // =========================
        // POST: Delete (normal form)
        // =========================
        public async Task<IActionResult> OnPostDeleteProjectAsync(Guid projectId, CancellationToken ct)
        {
            try
            {
                if (projectId == Guid.Empty)
                    throw new BadRequestAppException("Invalid project id.");

                await _projects.DeleteAsync(projectId, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Deleted";
                TempData["Toast.Message"] = "Project deleted successfully.";
            }
            catch (BadRequestAppException ex)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Not allowed";
                TempData["Toast.Message"] = ex.Message;
            }

            return RedirectToPage("./Projects", new
            {
                p = Page,
                pageSize = PageSize,
                q = Search,
                customerName = CustomerName,
                status = Status
            });
        }

        // =========================
        // GET: Details (Ajax JSON)
        // =========================
        public async Task<IActionResult> OnGetProjectAsync(Guid id, CancellationToken ct)
        {
            if (id == Guid.Empty)
                throw new BadRequestAppException("Invalid project id.");

            var project = await _projects.GetProjectAsync(id, ct);
            if (project is null)
                throw new NotFoundAppException("Project not found.");

            return new JsonResult(project);
        }

        // =========================
        // GET: Project Quotes (Ajax JSON)
        // =========================
        public async Task<IActionResult> OnGetProjectQuotesAsync(
            Guid projectId,
            int p = 1,
            int pageSize = 10,
            string? q = null,
            CancellationToken ct = default)
        {
            try
            {
                if (projectId == Guid.Empty)
                    return ToastBadRequest("Error", "ProjectId is empty.");

                // (اختياري) تأكد المشروع موجود
                var prj = await _projects.GetProjectAsync(projectId, ct);
                if (prj is null)
                    return ToastNotFound("Not found", "Project not found.");

                var res = await _quotes.GetQuotesByProjectAsync(projectId, p, pageSize, q, ct);

                return new JsonResult(new
                {
                    ok = true,
                    data = res
                });
            }
            catch (BadRequestAppException ex)
            {
                return ToastBadRequest("Not allowed", ex.Message);
            }
            catch (NotFoundAppException ex)
            {
                return ToastNotFound("Not found", ex.Message);
            }
            catch (Exception)
            {
                return ToastServerError();
            }
        }

        // =========================
        // POST: Update Basic (Ajax JSON)
        // =========================
        public class UpdateProjectBasicRequest
        {
            public Guid ProjectId { get; set; }
            public UpdateProjectDto Project { get; set; } = new();
        }

        public async Task<IActionResult> OnPostUpdateBasicAsync([FromBody] UpdateProjectBasicRequest? req, CancellationToken ct)
        {
            try
            {
                if (req is null)
                    return ToastBadRequest("Error", "Body is null.");

                if (req.ProjectId == Guid.Empty)
                    return ToastBadRequest("Error", "ProjectId is empty.");

                var vr = await _updateValidator.ValidateAsync(req.Project, ct);
                if (!vr.IsValid)
                {
                    return BadRequest(new
                    {
                        toast = new { type = "error", title = "Validation", message = "Please fix the highlighted fields." },
                        message = "Validation failed",
                        errors = vr.Errors.Select(e => new { field = e.PropertyName, error = e.ErrorMessage })
                    });
                }

                await _projects.UpdateAsync(req.ProjectId, req.Project, ct);

                var updated = await _projects.GetProjectAsync(req.ProjectId, ct);

                return new JsonResult(new
                {
                    ok = true,
                    data = updated,
                    toast = new { type = "success", title = "Success", message = "Saved successfully." }
                });
            }
            catch (BadRequestAppException ex)
            {
                return ToastBadRequest("Not allowed", ex.Message);
            }
            catch (NotFoundAppException ex)
            {
                return ToastNotFound("Not found", ex.Message);
            }
            catch (Exception)
            {
                return ToastServerError();
            }
        }

        // =========================
        // POST: Change Status (Ajax JSON)
        // =========================
        public class ChangeStatusRequest
        {
            public Guid ProjectId { get; set; }
            public ProjectStatus Status { get; set; }
        }

        public async Task<IActionResult> OnPostChangeStatusAsync([FromBody] ChangeStatusRequest? req, CancellationToken ct)
        {
            try
            {
                if (req is null)
                    return BadRequest(new { toast = new { type = "error", title = "Error", message = "Body is null." } });

                if (req.ProjectId == Guid.Empty)
                    return BadRequest(new { toast = new { type = "error", title = "Error", message = "ProjectId is empty." } });

                await _projects.ChangeStatusAsync(req.ProjectId, req.Status, ct);

                var updated = await _projects.GetProjectAsync(req.ProjectId, ct);

                return new JsonResult(new
                {
                    ok = true,
                    data = updated,
                    toast = new { type = "success", title = "Success", message = "Status updated successfully." }
                });
            }
            catch (BadRequestAppException ex)
            {
                return BadRequest(new { toast = new { type = "error", title = "Not allowed", message = ex.Message } });
            }
            catch (NotFoundAppException ex)
            {
                return NotFound(new { toast = new { type = "error", title = "Not found", message = ex.Message } });
            }
            catch (Exception)
            {
                return StatusCode(500, new { toast = new { type = "error", title = "Server error", message = "Something went wrong." } });
            }
        }

        // =========================
        // Table loading
        // =========================
        private async Task LoadTableAsync(CancellationToken ct)
        {
            var res = await _projects.GetProjectsAsync(Page, PageSize, Search, CustomerName, Status, ct);

            ProjectsTable = new TableCardVm
            {
                Title = "Projects",
                PrimaryButtonText = "Add Project",
                PrimaryButtonTarget = "#FormModal",
                SearchPlaceholder = "Search projects...",
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
                    new() { Header="Title",       Width="24%", HeaderClass="ps-4", CellClass="ps-4 fw-semibold" },
                    new() { Header="Customer",    Width="18%" },
                    new() { Header="Email",       Width="16%" },
                    new() { Header="Status",      Width="10%" },
                    new() { Header="Dates",       Width="14%" },
                    new() { Header="Counts",      Width="10%" },
                    new() { Header="Actions",     Width="8%", HeaderClass="text-end pe-4", CellClass="text-end pe-4" },
                }
            };

            foreach (var p in res.Items)
            {
                ProjectsTable.Rows.Add(new TableRowVm
                {
                    Cells =
                    {
                        Html(p.Title),
                        Html(p.CustomerName),
                        Html(p.CustomerEmail ?? "—"),
                        StatusBadge(p.Status),
                        Html($"{FmtDate(p.StartDate)} → {FmtDate(p.EndDate)}"),
                        CountsBadge(p.QuotesCount, p.InvoicesCount),
                        ActionsButtons(p.Id.ToString())
                    }
                });
            }
        }

        private void BuildCreateProjectModal(bool autoOpen)
        {
            CreateProjectModal = new ModalVm
            {
                Id = "FormModal",
                Title = "Add Project",
                SizeClass = "modal-lg",
                SubmitText = "Save",
                CancelText = "Cancel",
                Handler = null, // OnPostAsync
                BodyPartialPath = "~/Pages/Shared/Modals/_CreateProjectFields.cshtml",
                BodyModel = this,
                AutoOpen = autoOpen
            };
        }

        // ========= helpers =========
        private static string FmtDate(DateOnly? d) => d?.ToString("yyyy-MM-dd") ?? "—";

        private static Microsoft.AspNetCore.Html.IHtmlContent Html(string? text)
            => new Microsoft.AspNetCore.Html.HtmlString(System.Text.Encodings.Web.HtmlEncoder.Default.Encode(text ?? ""));

        private static Microsoft.AspNetCore.Html.IHtmlContent StatusBadge(ProjectStatus st)
        {
            var (cls, txt) = st switch
            {
                ProjectStatus.Active => ("badge bg-success bg-opacity-10 text-success", "Active"),
                ProjectStatus.Closed => ("badge bg-secondary bg-opacity-10 text-secondary", "Closed"),
                _ => ("badge bg-warning bg-opacity-10 text-warning", "Draft")
            };
            return new Microsoft.AspNetCore.Html.HtmlString($"<span class='{cls}'>{txt}</span>");
        }

        private static Microsoft.AspNetCore.Html.IHtmlContent CountsBadge(int quotes, int invoices)
        {
            var html = $"<span class='badge bg-primary bg-opacity-10 text-primary me-1'>Q:{quotes}</span>" +
                       $"<span class='badge bg-info bg-opacity-10 text-info'>I:{invoices}</span>";
            return new Microsoft.AspNetCore.Html.HtmlString(html);
        }

        private static string Enc(string? v) => System.Text.Encodings.Web.HtmlEncoder.Default.Encode(v ?? "");

        private static Microsoft.AspNetCore.Html.IHtmlContent ActionsButtons(string projectId)
        {
            return new Microsoft.AspNetCore.Html.HtmlString($$"""
<div class="vc-actions-wrap">
  <button type="button"
          class="btn vc-icon-btn text-secondary"
          title="View"
          data-bs-toggle="modal"
          data-bs-target="#ViewProjectModal"
          data-project-id="{{Enc(projectId)}}"
          data-open-tab="overview">
      <i class="material-icons-outlined">visibility</i>
  </button>

  <button type="button"
          class="btn vc-icon-btn text-primary"
          title="Quotes"
          data-bs-toggle="modal"
          data-bs-target="#ViewProjectModal"
          data-project-id="{{Enc(projectId)}}"
          data-open-tab="quotes">
      <i class="material-icons-outlined">request_quote</i>
  </button>

  <button type="button"
          class="btn vc-icon-btn text-danger"
          title="Delete"
          data-vc-action="table-delete-project"
          data-project-id="{{Enc(projectId)}}">
      <i class="material-icons-outlined">delete</i>
  </button>
</div>
""");
        }

        private async Task LoadCustomersAsync(CancellationToken ct)
        {
            var res = await _customers.GetCustomersAsync(page: 1, pageSize: 200, search: null, ct: ct);

            CustomerOptions = res.Items
                .OrderBy(x => x.Name)
                .Select(x => new SelectListItem
                {
                    Value = x.Id.ToString(),
                    Text = x.Name
                })
                .ToList();

            CustomerOptions.Insert(0, new SelectListItem
            {
                Value = "00000000-0000-0000-0000-000000000000",
                Text = "-- Select customer --"
            });
        }
    }
}
