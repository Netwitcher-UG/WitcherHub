using FluentValidation;
using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Models.DTO.Project;
using WitcherHub.Application.Models.View.Contracts;
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
        private readonly IInvoice _invoices;
        private readonly IContract _contracts;
        private readonly IContractDocumentGenerator _contractDocumentGenerator;
        private readonly IValidator<CreateProjectDto> _createValidator;
        private readonly IValidator<UpdateProjectDto> _updateValidator;
        private readonly ILogger<ProjectsModel> _logger;
        public ProjectsModel(
  IProject projects,
  ICustomer customers,
  IQuote quotes,
  IInvoice invoices,
  IContract contracts,
  IContractDocumentGenerator contractDocumentGenerator,
  IValidator<CreateProjectDto> createValidator,
  IValidator<UpdateProjectDto> updateValidator,
  ILogger<ProjectsModel> logger)
        {
            _projects = projects;
            _customers = customers;
            _quotes = quotes;
            _invoices = invoices;
            _contracts = contracts;
            _contractDocumentGenerator = contractDocumentGenerator;
            _createValidator = createValidator;
            _updateValidator = updateValidator;
            _logger = logger;
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
            if (OpenTab != "overview" && OpenTab != "quotes" && OpenTab != "invoices" && OpenTab != "contracts")
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

        public async Task<IActionResult> OnGetProjectInvoicesAsync(
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

                var prj = await _projects.GetProjectAsync(projectId, ct);
                if (prj is null)
                    return ToastNotFound("Not found", "Project not found.");

                var res = await _invoices.GetInvoicesByProjectAsync(projectId, p, pageSize, q, ct);

                return new JsonResult(new { ok = true, data = res });
            }
            catch (BadRequestAppException ex) { return ToastBadRequest("Not allowed", ex.Message); }
            catch (NotFoundAppException ex) { return ToastNotFound("Not found", ex.Message); }
            catch { return ToastServerError(); }
        }


        public async Task<IActionResult> OnGetProjectContractsAsync(
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

                var prj = await _projects.GetProjectAsync(projectId, ct);
                if (prj is null)
                    return ToastNotFound("Not found", "Project not found.");

                var res = await _contracts.GetContractsByProjectAsync(projectId, p, pageSize, q, ct);

                return new JsonResult(new { ok = true, data = res });
            }
            catch (BadRequestAppException ex) { return ToastBadRequest("Not allowed", ex.Message); }
            catch (NotFoundAppException ex) { return ToastNotFound("Not found", ex.Message); }
            catch { return ToastServerError(); }
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
        public async Task<IActionResult> OnGetProjectContractSnapshotAsync(Guid projectId, CancellationToken ct = default)
        {
            try
            {
                if (projectId == Guid.Empty)
                    return BadRequest(new { toast = new { type = "error", title = "Error", message = "Invalid project." } });

                var prj = await _projects.GetProjectAsync(projectId, ct);
                if (prj is null)
                    return NotFound(new { toast = new { type = "error", title = "Not found", message = "Project not found." } });

                // Latest contract only (single per project)
                var list = await _contracts.GetContractsByProjectAsync(projectId, page: 1, pageSize: 1, search: null, ct);
                var latest = list.Items?.FirstOrDefault();

                if (latest is null)
                {
                    return new JsonResult(new
                    {
                        ok = true,
                        data = new { exists = false }
                    });
                }

                var details = await _contracts.GetContractAsync(latest.Id, ct);
                if (details is null)
                {
                    return new JsonResult(new
                    {
                        ok = true,
                        data = new { exists = false }
                    });
                }

                var previewHtml = MarkdownToSafeHtml(details.Terms ?? "");

                var isSigned = details.SignedAt is not null || details.Status == DocumentStatus.Signed;
                var canUpdate = !isSigned;

                var itemsCount = details.Items?.Count ?? 0;

                return new JsonResult(new
                {
                    ok = true,
                    data = new
                    {
                        exists = true,
                        contractId = details.Id,
                        contractNo = details.ContractNo,
                        status = details.Status.ToString(),
                        signedAt = details.SignedAt,
                        canUpdate,
                        itemsCount,
                        previewHtml,
                        editUrl = $"/Contracts/Edit?id={details.Id}",
                        detailsUrl = $"/Contracts/Details?id={details.Id}"
                    }
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProjectContractSnapshot failed. projectId={ProjectId}", projectId);
                return StatusCode(500, new { toast = new { type = "error", title = "Server error", message = "Something went wrong." } });
            }
        }

        public async Task<IActionResult> OnPostGenerateProjectContractAsync(Guid projectId, CancellationToken ct = default)
        {
            try
            {
                if (projectId == Guid.Empty)
                    return ToastBadRequest("Error", "Invalid project.");

                var prj = await _projects.GetProjectAsync(projectId, ct);
                if (prj is null)
                    return ToastNotFound("Not found", "Project not found.");

                // latest contract only (ONE contract per project)
                var list = await _contracts.GetContractsByProjectAsync(projectId, page: 1, pageSize: 1, search: null, ct);
                var latest = list.Items?.FirstOrDefault();

                Guid contractId;

                // 1) No contract yet -> create header only and redirect to edit
                if (latest is null)
                {
                    var create = new ContractDTOs
                    {
                        Contract = new ContractDto
                        {
                            ProjectId = projectId,
                            Currency = "EUR",
                            Status = DocumentStatus.Draft,
                            StartDate = prj.StartDate,
                            EndDate = prj.EndDate,
                            Terms = null
                        },
                        Items = new List<ContractItemDto>()
                    };

                    contractId = await _contracts.CreateAsync(create, ct);

                    return new JsonResult(new
                    {
                        ok = true,
                        data = new
                        {
                            contractId,
                            next = "edit",
                            editUrl = $"/Contracts/Edit?id={contractId}"
                        },
                        toast = new
                        {
                            type = "info",
                            title = "Contract created",
                            message = "Add at least one service (line item), then click Update Contract to generate the contract terms."
                        }
                    });
                }

                contractId = latest.Id;

                var details = await _contracts.GetContractAsync(contractId, ct);
                if (details is null)
                    return ToastNotFound("غير موجود", "العقد غير موجود.");

                
                

                // rule: if signed => cannot regenerate
                if (details.SignedAt is not null || details.Status == DocumentStatus.Signed)
                    return ToastBadRequest("Not allowed", "This contract is already signed. You cannot generate a new one.");

                // ✅ NEW: Must have at least one line item before generating
                var itemsCount = details.Items?.Count ?? 0;
                if (itemsCount == 0)
                {
                    // رسالة للمستخدم النهائي (مو للمبرمج)
                    return BadRequest(new
                    {
                        toast = new
                        {
                            type = "warning",
                            title = "Missing services",
                            message = "Please add at least one service to this contract before generating it."
                        },
                        action = "openEdit",
                        editUrl = $"/Contracts/Edit?id={details.Id}",
                        contractId = details.Id
                    });
                }

                // بعد ذلك فقط نولّد العقد
                var req = BuildGenerateRequest(prj, details);
                var doc = await _contractDocumentGenerator.GenerateAsync(req, ct);


                var update = new UpdateContractDto
                {
                    Contract = new ContractDto
                    {
                        ProjectId = projectId,
                        Currency = details.Currency,
                        Status = DocumentStatus.Draft,
                        StartDate = details.StartDate,
                        EndDate = details.EndDate,
                        Terms = doc.FullDocument,
                        SignedAt = null
                    },
                    Items = null // IMPORTANT: don't clear items
                };

                await _contracts.UpdateAsync(contractId, update, ct);

                return new JsonResult(new
                {
                    ok = true,
                    data = new
                    {
                        contractId,
                        next = "snapshot"
                    },
                    toast = new
                    {
                        type = "success",
                        title = "Updated",
                        message = "Contract terms have been generated/updated successfully."

                    }
                });
            }
            catch (BadRequestAppException ex)
            {
                return ToastBadRequest("Warning", ex.Message);
            }
            catch (NotFoundAppException ex)
            {
                return ToastNotFound("Not found", ex.Message);
            }
            catch
            {
                return ToastServerError();
            }
        }

        private GenerateContractDocumentRequest BuildGenerateRequest(
            WitcherHub.Application.Models.View.Project.ProjectViews.ProjectDetailsView prj,
            ContractViews.ContractDetailsView contract)
        {
            var customerName = prj.Customer?.Name ?? "";
            var customerEmail = prj.Customer?.Email ?? "";

            var customerBlock =
                $"Name/Firma: {customerName}\n" +
                (string.IsNullOrWhiteSpace(customerEmail) ? "" : $"E-Mail: {customerEmail}\n");

            var lines = (contract.Items ?? new List<ContractViews.ContractItemItemView>())
                .OrderBy(x => x.Position)
                .Select(x => new ContractServiceLineDto
                {
                    Position = x.Position,
                    Title = x.Title,
                    ServiceName = x.ServiceName,
                    AgreedPrice = x.AgreedPrice,
                    Config = x.Config is null
                        ? new Dictionary<string, object>()
                        : JsonSerializer.Deserialize<Dictionary<string, object>>(x.Config.RootElement.GetRawText()) ?? new()
                })
                .ToList();

            return new GenerateContractDocumentRequest
            {
                ContractNo = contract.ContractNo,
                ProjectTitle = prj.Title ?? "Project",
                Currency = contract.Currency ?? "EUR",
                StartDate = contract.StartDate ?? prj.StartDate,
                EndDate = contract.EndDate ?? prj.EndDate,

                SignerName = "",
                SignerEmail = customerEmail,

                LeaveCustomerFieldsBlank = false,
                IncludePricesInServicesSection = true,
                CustomerBlockOverride = customerBlock,

                Services = lines
            };
        }

        private static string MarkdownToSafeHtml(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return "";

            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            var html = Markdown.ToHtml(markdown.Replace("\r\n", "\n"), pipeline);

            var sanitizer = new HtmlSanitizer();
            sanitizer.AllowedSchemes.Add("mailto");
            return sanitizer.Sanitize(html);
        }
        public async Task<IActionResult> OnPostCreateProjectContractAsync(Guid projectId, CancellationToken ct = default)
        {
            try
            {
                if (projectId == Guid.Empty)
                    return BadRequest(new { toast = new { type = "error", title = "Error", message = "Invalid project." } });


                var prj = await _projects.GetProjectAsync(projectId, ct);
                if (prj is null)
                    return NotFound(new { toast = new { type = "error", title = "Not found", message = "Project not found." } });


                // تأكد أنه لا يوجد عقد مسبقاً لهذا المشروع (عقد واحد فقط)
                var list = await _contracts.GetContractsByProjectAsync(projectId, page: 1, pageSize: 1, search: null, ct);
                var latest = list.Items?.FirstOrDefault();
                if (latest is not null)
                {
                    return new JsonResult(new
                    {
                        ok = true,
                        data = new { contractId = latest.Id, editUrl = $"/Contracts/Edit?id={latest.Id}" },
                        toast = new { type = "info", title = "Already exists", message = "A contract already exists for this project." }

                    });
                }

                // أنشئ Header فقط
                var create = new ContractDTOs
                {
                    Contract = new ContractDto
                    {
                        ProjectId = projectId,
                        Currency = "EUR",
                        Status = DocumentStatus.Draft,
                        StartDate = prj.StartDate,
                        EndDate = prj.EndDate,
                        Terms = null
                    },
                    Items = new List<ContractItemDto>()
                };

                var contractId = await _contracts.CreateAsync(create, ct);

                return new JsonResult(new
                {
                    ok = true,
                    data = new { contractId, editUrl = $"/Contracts/Edit?id={contractId}" },
                    toast = new
                    {
                        type = "info",
                        title = "Contract created",
                        message = "Add at least one service (line item), then come back and click Update Contract to generate the terms."
                    }
                });
            }
            catch
            {
                return StatusCode(500, new { toast = new { type = "error", title = "Server error", message = "حدث خطأ غير متوقع." } });
            }
        }

    }
}
