using FluentValidation;
using WitcherHub.Rendering;
using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Common.Pagination;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.Email;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Models.DTO.Project;
using WitcherHub.Application.Models.Email;
using WitcherHub.Application.Models.View.Contracts;
using WitcherHub.Application.Models.View.Project;
using WitcherHub.Application.Models.View.Quotes;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
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
        private readonly AppDbContext _db;
        private readonly IEmailTemplateRenderer _templates;
        private readonly IEmailSender _emailSender;
        public ProjectsModel(
  IProject projects,
  ICustomer customers,
  IQuote quotes,
  IInvoice invoices,
  IContract contracts,
  IContractDocumentGenerator contractDocumentGenerator,
  IValidator<CreateProjectDto> createValidator,
  IValidator<UpdateProjectDto> updateValidator,
  ILogger<ProjectsModel> logger,
  AppDbContext db, IEmailTemplateRenderer templates, IEmailSender emailSender)
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
            _db = db;
            _templates = templates;
            _emailSender = emailSender;
        }


        // query-string
        [BindProperty(SupportsGet = true, Name = "p")] public new int Page { get; set; } = 1;
        [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 10;
        [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }



        // extra filters
        [BindProperty(SupportsGet = true)] public string? CustomerName { get; set; }
        [BindProperty(SupportsGet = true)] public ProjectStatus? Status { get; set; }

        [BindProperty(SupportsGet = true)] public bool IncludeArchived { get; set; }

        /// <summary>
        /// The rows themselves, typed. The page used to receive pre-rendered HTML
        /// strings built in this class — which is why the status badge here had
        /// drifted to different colours from the rest of the application, and why
        /// the markup could not adapt to the window at all.
        /// </summary>
        public IReadOnlyList<ProjectViews.ProjectListItemView> Projects { get; private set; }
            = Array.Empty<ProjectViews.ProjectListItemView>();

        public PagerVm? Pager { get; private set; }

        public bool HasFilters =>
            !string.IsNullOrWhiteSpace(Search) || Status is not null || IncludeArchived;


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

            // The start date is deliberately not pre-filled.
            //
            // It used to default to today, which is a value nobody chose on a
            // field a project may not need at all — and a default that looks like
            // an answer gets saved as one. A project with no dates is a perfectly
            // ordinary project.
        }

        // =========================
        // POST: Create
        // =========================
        public async Task<IActionResult> OnPostAsync(CancellationToken ct)
        {
            EnsureDefaults();
            await LoadCustomersAsync(ct);
            await LoadTableAsync(ct);

            if (Project.CustomerId == Guid.Empty)
                ModelState.AddModelError("Project.CustomerId", "Customer is required.");

            var vr = await _createValidator.ValidateAsync(Project, ct);
            if (!vr.IsValid)
            {
                foreach (var err in vr.Errors)
                {
                    var key = err.PropertyName;

                    if (!string.IsNullOrWhiteSpace(key) && !key.StartsWith("Project."))
                        key = "Project." + key;

                    ModelState.AddModelError(key, err.ErrorMessage);
                }
            }

            if (!ModelState.IsValid)
            {
                BuildCreateProjectModal(autoOpen: true);

                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Validation";
                TempData["Toast.Message"] = "Please fix the highlighted fields.";

                return Page();
            }

            var projectId = await _projects.CreateAsync(Project, createdById: null, ct);

            TempData["Toast.Type"] = "success";
            TempData["Toast.Title"] = "Project created";
            TempData["Toast.Message"] = "Pick up where you left off below.";

            // Straight into the new project rather than back to the list.
            //
            // Creating a project is never the goal in itself — the next thing is
            // always to do something inside it, and returning to the list made
            // the user find the row they had just created before they could
            // start.
            return RedirectToPage("/Projects/Workspace", new { id = projectId });
        }

        /// <summary>
        /// What permanently deleting a project would destroy, asked before the
        /// confirmation is shown so a person sees it rather than discovering it.
        /// </summary>
        public async Task<IActionResult> OnGetDeletionImpactAsync(Guid projectId, CancellationToken ct)
        {
            if (projectId == Guid.Empty) return BadRequest();

            var impact = await _projects.GetDeletionImpactAsync(projectId, ct);

            return new JsonResult(new
            {
                blocked = impact.IsBlocked,
                reason = impact.BlockingReason,
                clean = impact.IsClean,
                willDelete = impact.WhatWillBeDeleted
            });
        }

        public async Task<IActionResult> OnPostArchiveProjectAsync(Guid projectId, CancellationToken ct)
        {
            try
            {
                await _projects.ArchiveAsync(projectId, archivedById: null, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Archived";
                TempData["Toast.Message"] =
                    "The project was archived. Nothing was deleted — tick \"Include archived\" to find it again.";
            }
            catch (AppException ex)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Could not archive";
                TempData["Toast.Message"] = ex.Message;
            }

            return RedirectToCurrentList();
        }

        public async Task<IActionResult> OnPostRestoreProjectAsync(Guid projectId, CancellationToken ct)
        {
            try
            {
                await _projects.RestoreAsync(projectId, ct);

                TempData["Toast.Type"] = "success";
                TempData["Toast.Title"] = "Restored";
                TempData["Toast.Message"] = "The project is back in the active list.";
            }
            catch (AppException ex)
            {
                TempData["Toast.Type"] = "error";
                TempData["Toast.Title"] = "Could not restore";
                TempData["Toast.Message"] = ex.Message;
            }

            return RedirectToCurrentList();
        }

        private IActionResult RedirectToCurrentList() =>
            RedirectToPage("./Projects", new
            {
                p = Page,
                pageSize = PageSize,
                q = Search,
                customerName = CustomerName,
                status = Status,
                includeArchived = IncludeArchived
            });

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
        /// <summary>
        /// The project behind the workspace panel.
        ///
        /// Every other handler here answers with a structured error; this one threw,
        /// so any fault became an opaque 500 and the browser could only report that
        /// something had gone wrong. The exception is logged with the project id so
        /// a failure can be traced to a row, and the caller is told what happened
        /// without being shown provider detail or a stack trace.
        /// </summary>
        public async Task<IActionResult> OnGetProjectAsync(Guid id, CancellationToken ct)
        {
            try
            {
                if (id == Guid.Empty)
                    return ToastBadRequest("Error", "Project id is missing.");

                var project = await _projects.GetProjectAsync(id, ct);

                if (project is null)
                    return ToastNotFound("Not found", "This project no longer exists.");

                return new JsonResult(project);
            }
            catch (BadRequestAppException ex)
            {
                return ToastBadRequest("Not allowed", ex.Message);
            }
            catch (NotFoundAppException ex)
            {
                return ToastNotFound("Not found", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Loading project {ProjectId} for the workspace failed.", id);
                return ToastServerError();
            }
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
            var res = await _projects.GetProjectsAsync(
                Page, PageSize, Search, CustomerName, Status, IncludeArchived, ct);

            Projects = res.Items;

            // Built from the request, so every filter currently applied survives
            // a page change.
            Pager = PagerVm.From(Request, res.Page, res.PageSize, res.TotalItems);
        }

        private void BuildCreateProjectModal(bool autoOpen)
        {
            CreateProjectModal = new ModalVm
            {
                Id = "FormModal",
                Title = "New project",

                // Two fields do not need a large dialog. The size was signalling
                // that this was a bigger job than it is.
                SizeClass = "",
                SubmitText = "Create project",
                CancelText = "Cancel",
                Handler = null, // OnPostAsync
                BodyPartialPath = "~/Pages/Shared/Modals/_CreateProjectFields.cshtml",
                BodyModel = this,
                AutoOpen = autoOpen
            };
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

                // ✅ لازم نحمّل Items وإلا itemsCount سيبقى 0
                var contract = await _db.Contracts
                    .AsNoTracking()
                    .Include(c => c.Items)
                    .Where(c => c.ProjectId == projectId)
                    .OrderByDescending(c => c.CreatedAt)
                    .FirstOrDefaultAsync(ct);

                if (contract is null)
                {
                    return new JsonResult(new
                    {
                        ok = true,
                        data = new { exists = false }
                    });
                }

                var previewHtml = MarkdownToSafeHtml(contract.Terms ?? "");
                var hasTerms = !string.IsNullOrWhiteSpace(contract.Terms);

                var isSigned = contract.SignedAt is not null || contract.Status == DocumentStatus.Signed;
                var canUpdate = !isSigned;
                var itemsCount = contract.Items?.Count ?? 0;

                return new JsonResult(new
                {
                    ok = true,
                    data = new
                    {
                        exists = true,
                        contractId = contract.Id,
                        contractNo = contract.ContractNo,
                        status = contract.Status.ToString(),
                        signedAt = contract.SignedAt,
                        canUpdate,
                        itemsCount,
                        hasTerms,
                        previewHtml,
                        editUrl = $"/Contracts/Edit?id={contract.Id}",
                        detailsUrl = $"/Contracts/Details?id={contract.Id}"
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

                if (latest is null)
                {
                    return BadRequest(new
                    {
                        toast = new
                        {
                            type = "warning",
                            title = "No contract",
                            message = "No contract exists yet. Click Add Contract when you're ready to create it."
                        }
                    });
                }

                var contractId = latest.Id;

                var details = await _contracts.GetContractAsync(contractId, ct);
                if (details is null)
                    return ToastNotFound("Not found", "Contract not found.");

                // rule: if signed => cannot regenerate
                if (details.SignedAt is not null || details.Status == DocumentStatus.Signed)
                    return ToastBadRequest("Not allowed", "This contract is already signed. You cannot generate a new one.");

                // Must have at least one line item before generating
                var itemsCount = details.Items?.Count ?? 0;
                if (itemsCount == 0)
                {
                    return BadRequest(new
                    {
                        toast = new
                        {
                            type = "warning",
                            title = "Positions required",
                            message = "You were redirected to add Positions first."
                        },
                        data = new
                        {
                            redirectUrl = $"/Contracts/Positions/{details.Id}",
                            contractId = details.Id
                        }
                    });
                }

                // generate contract terms
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
                        Terms = details.Terms,              
                        TermsStructured = doc.Structured,  
                        SignedAt = details.SignedAt
                    },
                    Items = null
                };

                await _contracts.UpdateAsync(contractId, update, ct);

                return new JsonResult(new
                {
                    ok = true,
                    data = new
                    {
                        redirectUrl = $"/Contracts/Override?id={contractId}"
                    },
                    toast = new
                    {
                        type = "info",
                        title = "Next step",
                        message = "Review and override Positions, then generate the contract."
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

            var customerBlockSb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(customerName))
                customerBlockSb.AppendLine(customerName);
            if (!string.IsNullOrWhiteSpace(customerEmail))
                customerBlockSb.AppendLine(customerEmail);

            var customerBlockText = customerBlockSb.ToString().TrimEnd();

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
                CustomerBlockOverride = customerBlockText,

                Services = lines
            };
        }

        private static string MarkdownToSafeHtml(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return "";

            return ContractMarkdown.ToHtml(markdown);
        }
        public async Task<IActionResult> OnPostCreateProjectContractAsync(Guid projectId, CancellationToken ct = default)
        {
            try
            {
                if (projectId == Guid.Empty)
                    return new JsonResult(new
                    {
                        ok = false,
                        toast = new { type = "error", title = "Error", message = "Invalid project." }
                    });

                var prj = await _projects.GetProjectAsync(projectId, ct);
                if (prj is null)
                    return new JsonResult(new
                    {
                        ok = false,
                        toast = new { type = "error", title = "Not found", message = "Project not found." }
                    });

                var list = await _contracts.GetContractsByProjectAsync(projectId, page: 1, pageSize: 1, search: null, ct);
                var latest = list.Items?.FirstOrDefault();

                // ✅ Contract exists
                if (latest is not null)
                {
                    var details = await _contracts.GetContractAsync(latest.Id, ct);
                    var itemsCount = details?.Items?.Count ?? 0;

                    // Has items -> go to details (no toast param)
                    if (itemsCount > 0)
                    {
                        return new JsonResult(new
                        {
                            ok = true,
                            data = new
                            {
                                contractId = latest.Id,
                                detailsUrl = $"/Contracts/Details?id={latest.Id}"
                            },
                            toast = new { type = "info", title = "Already exists", message = "Contract already exists." }
                        });
                    }

                    // Header only -> go manage items with toast flag
                    return new JsonResult(new
                    {
                        ok = true,
                        data = new
                        {
                            contractId = latest.Id,
                            redirectUrl = $"/Contracts/Positions/{latest.Id}"
                        },
                        toast = new { type = "warning", title = "Positions required", message = "You were redirected to add line items first." }
                    });
                }

                // ✅ No contract -> create header only then go manage items with toast flag
                var dto = new ContractDTOs
                {
                    Contract = new ContractDto
                    {
                        ProjectId = projectId,
                        Status = DocumentStatus.Draft,
                        Currency = "EUR",
                        StartDate = prj.StartDate,
                        EndDate = prj.EndDate,
                        Terms = null,
                        SignedAt = null
                    },
                    Items = null
                };

                var newContractId = await _contracts.CreateAsync(dto, ct);

                return new JsonResult(new
                {
                    ok = true,
                    data = new
                    {
                        contractId = newContractId,
                        redirectUrl = $"/Contracts/Positions/{newContractId}"
                    },
                    toast = new { type = "success", title = "Created", message = "Contract header created. Add Positions now." }
                });
            }
            catch
            {
                return new JsonResult(new
                {
                    ok = false,
                    toast = new { type = "error", title = "Server error", message = "An unexpected error occurred." }
                });
            }
        }
        /// <summary>
        /// The contract an action is meant to act on.
        ///
        /// These handlers used to take only a project id and call
        /// FirstOrDefaultAsync(c =&gt; c.ProjectId == projectId) with no ordering, so
        /// with more than one contract in a project they operated on whichever row
        /// the database happened to return. For sending a contract to a customer
        /// for signature, that is sending a document nobody chose.
        ///
        /// A contract id is now used when given. Without one, a project holding
        /// exactly one contract is unambiguous and still works; a project holding
        /// several refuses and says so, rather than guessing.
        /// </summary>
        private async Task<(Contract? Contract, IActionResult? Problem)> ResolveProjectContractAsync(
            Guid projectId, Guid contractId, CancellationToken ct)
        {
            var query = _db.Contracts
                .Include(c => c.Items)
                .Include(c => c.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(cu => cu.Contacts)
                .Include(c => c.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(cu => cu.EmailAddresses)
                .Where(c => c.ProjectId == projectId);

            if (contractId != Guid.Empty)
            {
                var chosen = await query.FirstOrDefaultAsync(c => c.Id == contractId, ct);

                return chosen is null
                    ? (null, ToastNotFound("Not found", "That contract is not part of this project."))
                    : (chosen, null);
            }

            var all = await query.OrderBy(c => c.CreatedAt).ToListAsync(ct);

            if (all.Count == 0)
                return (null, ToastNotFound("No contract", "There is no contract for this project."));

            if (all.Count > 1)
            {
                return (null, ToastBadRequest(
                    "Which contract?",
                    "This project has more than one contract. Open the one you mean and act on it there."));
            }

            return (all[0], null);
        }

        public async Task<IActionResult> OnPostSendProjectContractAsync(
            Guid projectId, Guid contractId, CancellationToken ct)
        {
            if (projectId == Guid.Empty)
                return new JsonResult(new { ok = false, toast = new { type = "error", title = "Error", message = "Invalid project id." } }) { StatusCode = 400 };

            // Which contract, said explicitly. Sending the wrong document to a
            // customer for signature is not a mistake that can be taken back.
            var (contract, problem) = await ResolveProjectContractAsync(projectId, contractId, ct);
            if (problem is not null) return problem;
            if (string.IsNullOrWhiteSpace(contract.Terms))
                return new JsonResult(new
                {
                    ok = false,
                    toast = new
                    {
                        type = "warning",
                        title = "Contract not generated",
                        message = "Generate the contract first before sending it."
                    }
                })
                { StatusCode = 409 };
            // شرطك: لازم يوجد عقد + line items
            var hasItems = contract.Items != null && contract.Items.Count > 0;
            if (!hasItems)
                return new JsonResult(new { ok = false, toast = new { type = "warning", title = "Missing Positions", message = "Please add at least one line item before sending." } }) { StatusCode = 409 };

            // لا ترسل إذا Signed (اختياري احترافي)
            if (contract.Status == DocumentStatus.Signed || contract.SignedAt != null)
                return new JsonResult(new { ok = false, toast = new { type = "info", title = "Already signed", message = "This contract is already signed." } }) { StatusCode = 409 };

            // 2) resolve recipient email (نفس منطقك: primary contact أولاً)
            var customer = contract.Project.Customer;

            string? recipientEmail =
                customer.Contacts?
                    .OrderByDescending(c => c.IsPrimary)
                    .Select(c => (c.Email ?? "").Trim())
                    .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));

            if (string.IsNullOrWhiteSpace(recipientEmail))
            {
                recipientEmail =
                    customer.EmailAddresses?
                        .OrderByDescending(ea => (ea.Kind ?? "").Trim().Equals("business", StringComparison.OrdinalIgnoreCase))
                        .Select(ea => (ea.Email ?? "").Trim())
                        .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));
            }

            if (string.IsNullOrWhiteSpace(recipientEmail))
            {
                return new JsonResult(new
                {
                    ok = false,
                    toast = new { type = "error", title = "No email", message = "Customer email not found (Contacts/EmailAddresses empty)." }
                })
                { StatusCode = 409 };
            }



            if (string.IsNullOrWhiteSpace(recipientEmail))
                return new JsonResult(new { ok = false, toast = new { type = "error", title = "No email", message = "Customer email not found." } }) { StatusCode = 409 };

            var recipientName = customer.Name ?? "Customer";

            // 3) Create secure token + store hash in ContractAccessLinks
            var rawToken = GenerateUrlSafeToken(32); // raw shown once
            var tokenHash = ContractAccessLink.HashToken(rawToken);

            var link = new ContractAccessLink
            {
                ContractId = contract.Id,
                RecipientEmail = recipientEmail.Trim(),
                TokenHash = tokenHash,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(14), // عدلها كما تريد
                RevokedAtUtc = null,
                LastOpenedAtUtc = null
            };

            _db.ContractAccessLinks.Add(link);

            // mark as sent (اختياري)
            if (contract.Status == DocumentStatus.Draft)
                contract.Status = DocumentStatus.Sent;

            await _db.SaveChangesAsync(ct);

            // 4) Build action url (absolute) -> /Contracts/Sign/{id}?t=token
            var actionUrl = Url.Page(
                pageName: "/Contracts/Sign",
                pageHandler: null,
                values: new { id = contract.Id, t = rawToken },
                protocol: Request.Scheme,
                host: Request.Host.ToUriComponent()
            );

            if (string.IsNullOrWhiteSpace(actionUrl))
                return new JsonResult(new { ok = false, toast = new { type = "error", title = "Error", message = "Failed to build contract link." } }) { StatusCode = 500 };

            // 5) Pick template: هنا اختيار بسيط (لو عندك لغة عميل خزنتها استخدمها)
            // حالياً: إن كان الإيميل ينتهي بـ .de أو الدومين ألماني... استخدم de وإلا en (عدل حسب نظامك)
            var template = "ContractReady.en";
            if (recipientEmail.EndsWith(".de", StringComparison.OrdinalIgnoreCase))
                template = "ContractReady.de";

            var subject = template.EndsWith(".de", StringComparison.OrdinalIgnoreCase)
                ? $"Vertrag {contract.ContractNo} – Bitte prüfen und unterschreiben"
                : $"Contract {contract.ContractNo} – Please review and sign";

            var html = await _templates.RenderAsync(template, new
            {
                Subject = subject,
                UserName = recipientName,
                ActionUrl = actionUrl,
                ContractNo = contract.ContractNo,
                ProjectTitle = contract.Project?.Title ?? "Project"
            }, ct);

            // ⚠️ مهم: MailKitEmailSender عندك يتطلب BCC
            var msg = new EmailMessage
            {
                From = new EmailAddress("placeholder@local", "placeholder"), // سيتم تجاهله من MailKitEmailSender واستبداله من SmtpOptions
                Subject = subject,
                HtmlBody = html,
                TextBody = $"Open: {actionUrl}",
                Bcc = new List<EmailAddress> { new EmailAddress(recipientEmail.Trim(), recipientName) }
            };

            await _emailSender.SendAsync(msg, ct);

            return new JsonResult(new
            {
                ok = true,
                toast = new { type = "success", title = "Sent", message = "Contract email sent successfully." }
            });
        }
        public async Task<IActionResult> OnPostCreateProjectContractLinkAsync(
            Guid projectId, Guid contractId, CancellationToken ct)
        {
            if (projectId == Guid.Empty)
                return new JsonResult(new
                {
                    ok = false,
                    toast = new { type = "error", title = "Error", message = "Invalid project id." }
                })
                { StatusCode = 400 };

            // A signing link is a link to one specific contract.
            var (contract, problem) = await ResolveProjectContractAsync(projectId, contractId, ct);
            if (problem is not null) return problem;

            if (contract is null)
                return new JsonResult(new
                {
                    ok = false,
                    toast = new { type = "warning", title = "No contract", message = "There is no contract for this project." }
                })
                { StatusCode = 404 };

            var hasItems = contract.Items != null && contract.Items.Count > 0;
            if (!hasItems)
                return new JsonResult(new
                {
                    ok = false,
                    toast = new { type = "warning", title = "Missing Positions", message = "Please add at least one line item before creating the link." }
                })
                { StatusCode = 409 };

            if (contract.Status == DocumentStatus.Signed || contract.SignedAt != null)
                return new JsonResult(new
                {
                    ok = false,
                    toast = new { type = "info", title = "Already signed", message = "This contract is already signed." }
                })
                { StatusCode = 409 };

            var customer = contract.Project.Customer;

            string? recipientEmail =
                customer.Contacts?
                    .OrderByDescending(c => c.IsPrimary)
                    .Select(c => (c.Email ?? "").Trim())
                    .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));

            if (string.IsNullOrWhiteSpace(recipientEmail))
            {
                recipientEmail =
                    customer.EmailAddresses?
                        .OrderByDescending(ea => (ea.Kind ?? "").Trim().Equals("business", StringComparison.OrdinalIgnoreCase))
                        .Select(ea => (ea.Email ?? "").Trim())
                        .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));
            }

            if (string.IsNullOrWhiteSpace(recipientEmail))
                return new JsonResult(new
                {
                    ok = false,
                    toast = new { type = "error", title = "No email", message = "Customer email not found." }
                })
                { StatusCode = 409 };

            var rawToken = GenerateUrlSafeToken(32);
            var tokenHash = ContractAccessLink.HashToken(rawToken);

            var link = new ContractAccessLink
            {
                ContractId = contract.Id,
                RecipientEmail = recipientEmail.Trim(),
                TokenHash = tokenHash,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(14),
                RevokedAtUtc = null,
                LastOpenedAtUtc = null
            };

            _db.ContractAccessLinks.Add(link);

            //if (contract.Status == DocumentStatus.Draft)
            //    contract.Status = DocumentStatus.Sent;

            await _db.SaveChangesAsync(ct);

            var actionUrl = Url.Page(
                pageName: "/Contracts/Sign",
                pageHandler: null,
                values: new { id = contract.Id, t = rawToken },
                protocol: Request.Scheme,
                host: Request.Host.ToUriComponent()
            );

            if (string.IsNullOrWhiteSpace(actionUrl))
                return new JsonResult(new
                {
                    ok = false,
                    toast = new { type = "error", title = "Error", message = "Failed to build contract link." }
                })
                { StatusCode = 500 };

            return new JsonResult(new
            {
                ok = true,
                data = new
                {
                    url = actionUrl
                },
                toast = new
                {
                    type = "success",
                    title = "Done",
                    message = "Contract link created successfully."
                }
            });
        }
        // helper: safe URL token
        private static string GenerateUrlSafeToken(int bytes)
        {
            var data = RandomNumberGenerator.GetBytes(bytes);
            return Convert.ToBase64String(data)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");
        }

    }
}
