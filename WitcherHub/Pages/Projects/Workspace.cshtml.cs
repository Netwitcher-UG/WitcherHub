using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Infrastructure.Services.Contracts.Delivery;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.Authentication;
using WitcherHub.Application.Services.Contracts.Delivery;
using WitcherHub.Application.Models.Email;
using WitcherHub.Application.Interfaces.Email;
using WitcherHub.Application.Models.View.Project;
using WitcherHub.Domain.Projects;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Services.Invoices;
using WitcherHub.Infrastructure.Services.Lexware;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Projects
{
    public class WorkspaceModel : PageModel
    {
        private readonly IProject _projects;
        private readonly AppDbContext _db;
        private readonly LexwareInvoiceSyncService _lexwareInvoiceSyncService;
        private readonly LexwareInvoiceStatusSyncService _lexwareInvoiceStatusSyncService;
        private readonly InvoicePublicLinkService _invoicePublicLinkService;
        private readonly IContractDeliveryService _delivery;
        private readonly IConfiguration _configuration;
        private readonly IEmailTemplateRenderer _templates;
        private readonly IEmailSender _email;
        private readonly ILogger<WorkspaceModel> _logger;

        public WorkspaceModel(
      IProject projects,
      AppDbContext db,
      LexwareInvoiceSyncService lexwareInvoiceSyncService,
      LexwareInvoiceStatusSyncService lexwareInvoiceStatusSyncService,
      InvoicePublicLinkService invoicePublicLinkService,
      IContractDeliveryService delivery,
      IConfiguration configuration,
      IEmailTemplateRenderer templates,
      IEmailSender email,
      ILogger<WorkspaceModel> logger)
        {
            _projects = projects;
            _db = db;
            _lexwareInvoiceSyncService = lexwareInvoiceSyncService;
            _lexwareInvoiceStatusSyncService = lexwareInvoiceStatusSyncService;
            _invoicePublicLinkService = invoicePublicLinkService;
            _delivery = delivery;
            _configuration = configuration;
            _templates = templates;
            _email = email;
            _logger = logger;
        }

        [BindProperty(SupportsGet = true, Name = "id")]
        public Guid ProjectId { get; set; }

        [BindProperty(SupportsGet = true, Name = "tab")]
        public string? Tab { get; set; }

        public string ProjectTitle { get; private set; } = "Project Workspace";

        /// <summary>
        /// The whole summary, rendered by the server.
        ///
        /// The page used to draw a "Loading…" card and then fill the title, the
        /// status, the customer and the dates from a fetch. Everything that
        /// matters at a glance therefore arrived after the page did, which is why
        /// the workspace read as empty — there was a moment where it genuinely
        /// was. None of it needs a round trip; it is all one query.
        /// </summary>
        public ProjectViews.ProjectDetailsView? Project { get; private set; }

        /// <summary>
        /// The project's own status beside the state of its documents, from the
        /// one place that answers it.
        /// </summary>
        public ProjectWorkflowState? Workflow { get; private set; }

        public Guid? CurrentContractId { get; private set; }
        public bool ShowManualInvoiceButton { get; private set; }

        /// <summary>
        /// Every contract in this project, with whether it may be sent, to whom,
        /// and what its outstanding signing request is doing.
        /// </summary>
        public IReadOnlyList<ContractDeliveryState> Deliveries { get; private set; } = [];

        /// <summary>
        /// The signing URL from the action just performed, handed to the page
        /// once so the browser can put it on the clipboard.
        ///
        /// Never stored, never logged, never rendered as visible text. It lives
        /// for one response and then only the hash of its token remains.
        /// </summary>
        public string? CopiedSigningUrl { get; private set; }

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (ProjectId == Guid.Empty)
                return RedirectToPage("/Projects");

            Tab = NormalizeTab(Tab);

            var project = await _projects.GetProjectAsync(ProjectId, ct);
            if (project is null)
                return NotFound();

            ProjectTitle = string.IsNullOrWhiteSpace(project.Title)
                ? "Project Workspace"
                : project.Title;

            Project = project;
            Workflow = await _projects.GetWorkflowStateAsync(ProjectId, ct);

            await LoadContractStateAsync(ct);
            await LoadDeliveryStatesAsync(ct);

            return Page();
        }

        /// <summary>
        /// The delivery state of every contract in the project.
        /// </summary>
        private async Task LoadDeliveryStatesAsync(CancellationToken ct)
        {
            var contractIds = await _db.Contracts
                .Where(c => c.ProjectId == ProjectId)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => c.Id)
                .ToListAsync(ct);

            var states = new List<ContractDeliveryState>(contractIds.Count);

            foreach (var id in contractIds)
                states.Add(await _delivery.DescribeAsync(id, ct));

            Deliveries = states;
        }

        // ══════════════════════════════════════════════════════════════════
        // Sending an approved contract to the customer
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Issues a signing link and hands it back once, for the clipboard.
        ///
        /// No e-mail is sent. The person copying it is the one delivering it,
        /// which is a perfectly ordinary way to get a contract to somebody who
        /// asked for it on the phone.
        /// </summary>
        public async Task<IActionResult> OnPostCreateSigningLinkAsync(Guid contractId, CancellationToken ct)
        {
            if (ProjectId == Guid.Empty || contractId == Guid.Empty) return NotFound();

            var result = await _delivery.CreateLinkAsync(
                contractId,
                ContractDeliveryMethod.CopiedLink,
                PublicBaseUrl.Resolve(_configuration) ?? "",
                CurrentUserId(),
                recipientEmail: null,
                ct);

            if (!result.Succeeded)
                return await BackToContractsAsync("error", "Signaturlink nicht erstellt", result.Message, ct);

            // Carried on TempData for exactly one redirect. It is put on the
            // clipboard by script and never written into the page as text.
            TempData["Delivery.CopiedUrl"] = result.SigningUrl;

            return await BackToContractsAsync(
                "success",
                "Signaturlink erstellt",
                $"Der sichere Signaturlink wurde kopiert. Er ist bis zum " +
                $"{result.ExpiresAt:dd.MM.yyyy} gültig.",
                ct);
        }

        /// <summary>
        /// Sends the approved contract to the customer's stored address.
        ///
        /// The request is marked as sent only once the mail has actually gone
        /// out. A failure leaves it unsent and revoked, so the page never tells
        /// the owner a customer has been written to when nobody has.
        /// </summary>
        public async Task<IActionResult> OnPostSendContractEmailAsync(
            Guid contractId, string? recipientEmail, CancellationToken ct)
        {
            if (ProjectId == Guid.Empty || contractId == Guid.Empty) return NotFound();

            var created = await _delivery.CreateLinkAsync(
                contractId,
                ContractDeliveryMethod.Email,
                PublicBaseUrl.Resolve(_configuration) ?? "",
                CurrentUserId(),
                recipientEmail,
                ct);

            if (!created.Succeeded)
                return await BackToContractsAsync("error", "Vertrag nicht versendet", created.Message, ct);

            var state = await _delivery.DescribeAsync(contractId, ct);

            try
            {
                await SendSigningEmailAsync(state, created, recipientEmail, ct);
                await _delivery.MarkSentAsync(created.RequestId!.Value, ct);
            }
            catch (Exception ex)
            {
                // The provider's words go to the log; the reader gets a sentence
                // and a reference.
                var reference = Guid.NewGuid().ToString("n")[..8];

                _logger.LogError(ex,
                    "Contract signing e-mail failed. Contract {ContractId} Request {RequestId} Reference {Reference}",
                    contractId, created.RequestId, reference);

                await _delivery.MarkDeliveryFailedAsync(created.RequestId!.Value, reference, ct);

                return await BackToContractsAsync(
                    "error",
                    "Vertrag nicht versendet",
                    $"Die E-Mail konnte nicht zugestellt werden. Es wurde nichts versendet. Referenz {reference}.",
                    ct);
            }

            return await BackToContractsAsync(
                "success",
                "Vertrag versendet",
                $"Der Vertrag {state.ContractNo} wurde an {state.LatestRequestRecipient} " +
                "zur elektronischen Unterzeichnung gesendet.",
                ct);
        }

        /// <summary>Withdraws an outstanding signing link.</summary>
        public async Task<IActionResult> OnPostCancelSigningRequestAsync(
            Guid contractId, Guid requestId, CancellationToken ct)
        {
            if (ProjectId == Guid.Empty || requestId == Guid.Empty) return NotFound();

            var result = await _delivery.CancelAsync(requestId, CurrentUserId(), ct);

            return await BackToContractsAsync(
                result.Succeeded ? "success" : "error",
                result.Succeeded ? "Versand storniert" : "Nicht storniert",
                result.Succeeded
                    ? "Der Signaturlink wurde deaktiviert und kann nicht mehr verwendet werden."
                    : result.Message,
                ct);
        }

        /// <summary>
        /// The e-mail itself, from the project's own template and sender.
        ///
        /// The signing URL is interpolated into the template and into nothing
        /// else — it is not logged, and the audit record keeps only the request
        /// id and the snapshot hash.
        /// </summary>
        private async Task SendSigningEmailAsync(
            ContractDeliveryState state,
            ContractDeliveryResult created,
            string? requestedRecipient,
            CancellationToken ct)
        {
            var recipient = state.LatestRequestRecipient;

            if (string.IsNullOrWhiteSpace(recipient))
                throw new InvalidOperationException("No recipient was recorded for the signature request.");

            var html = await _templates.RenderAsync("ContractReady.de", new
            {
                Subject = $"Vertrag {state.ContractNo} zur elektronischen Unterzeichnung",
                UserName = state.CustomerName,
                ContractNo = state.ContractNo,
                ProjectTitle = ProjectTitle,
                ActionUrl = created.SigningUrl,
                ExpirationDate = created.ExpiresAt?.ToLocalTime().ToString("dd.MM.yyyy") ?? "",
                TermsUrl = "https://netwitcher.com/de/agb-fuer-agenturen"
            }, ct);

            var from = _configuration["Smtp:From"] ?? _configuration["Smtp:User"] ?? "no-reply@netwitcher.de";

            await _email.SendAsync(new EmailMessage
            {
                From = new EmailAddress(from, "Netwitcher"),
                To = [new EmailAddress(recipient!, state.CustomerName)],
                Subject = $"Vertrag {state.ContractNo} zur elektronischen Unterzeichnung",
                HtmlBody = html
            }, ct);
        }

        private Guid? CurrentUserId()
        {
            var raw = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            return Guid.TryParse(raw, out var id) ? id : null;
        }

        private async Task<IActionResult> BackToContractsAsync(
            string type, string title, string? message, CancellationToken ct)
        {
            TempData["Toast.Type"] = type;
            TempData["Toast.Title"] = title;
            TempData["Toast.Message"] = message ?? "";

            await Task.CompletedTask;

            return RedirectToPage("/Projects/Workspace", new { id = ProjectId, tab = "contracts" });
        }

        public async Task<IActionResult> OnPostGenerateInvoiceAsync(Guid contractId, CancellationToken ct)
        {
            if (ProjectId == Guid.Empty || contractId == Guid.Empty)
                return NotFound();

            Tab = NormalizeTab(Tab);
            if (string.IsNullOrWhiteSpace(Tab))
                Tab = "contracts";

            try
            {
                var contract = await _db.Contracts
                    .Include(c => c.Items)
                    .FirstOrDefaultAsync(c => c.Id == contractId, ct);

                if (contract is null)
                    return NotFound();

                if (contract.ProjectId != ProjectId)
                    return Forbid();

                if (contract.Status != DocumentStatus.Signed)
                {
                    TempData["Toast.Type"] = "warning";
                    TempData["Toast.Title"] = "Not allowed";
                    TempData["Toast.Message"] = "Invoice can only be generated after the contract is signed.";
                    return RedirectToPage("/Projects/Workspace", new { id = ProjectId, tab = "contracts" });
                }

                if (contract.InvoiceSendMode != InvoiceSendMode.Manual)
                {
                    TempData["Toast.Type"] = "warning";
                    TempData["Toast.Title"] = "Not allowed";
                    TempData["Toast.Message"] = "Manual invoice generation is available only for contracts with Manual invoice mode.";
                    return RedirectToPage("/Projects/Workspace", new { id = ProjectId, tab = "contracts" });
                }

                if (contract.Items == null || contract.Items.Count == 0)
                {
                    TempData["Toast.Type"] = "warning";
                    TempData["Toast.Title"] = "Positions required";
                    TempData["Toast.Message"] = "Please add at least one Position first.";
                    return RedirectToPage("/Projects/Workspace", new { id = ProjectId, tab = "contracts" });
                }

                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var hasOneTimeItems = contract.Items.Any(i => i.BillingCycle == BillingCycle.OneTime);
                var hasRecurringItems = contract.Items.Any(i => i.BillingCycle != BillingCycle.OneTime);

                var results = new List<InvoiceGenerationResult>();

                if (hasRecurringItems)
                {
                    var start = contract.RecurringStartDate ?? contract.StartDate ?? today;

                    contract.RecurringEnabled = true;
                    contract.RecurringIsActive = true;

                    if (contract.NextRecurringInvoiceDate == null)
                        contract.NextRecurringInvoiceDate = start;

                    await _db.SaveChangesAsync(ct);
                }

                if (hasOneTimeItems)
                {
                    results.Add(await _lexwareInvoiceSyncService.CreateOneTimeInvoiceFromContractAsync(contract.Id, ct));
                }

                if (hasRecurringItems)
                {
                    if (!contract.NextRecurringInvoiceDate.HasValue)
                    {
                        results.Add(InvoiceGenerationResult.Warning("Recurring start date is missing."));
                    }
                    else if (contract.NextRecurringInvoiceDate.Value > today)
                    {
                        results.Add(InvoiceGenerationResult.Warning(
                            $"Recurring invoice is not due yet. Next cycle date is {contract.NextRecurringInvoiceDate.Value:yyyy-MM-dd}."));
                    }
                    else
                    {
                        while (contract.NextRecurringInvoiceDate.HasValue &&
                               contract.NextRecurringInvoiceDate.Value <= today)
                        {
                            var recurringResult =
                                await _lexwareInvoiceSyncService.CreateRecurringInvoiceFromContractAsync(
                                    contract.Id,
                                    contract.NextRecurringInvoiceDate.Value,
                                    ct);

                            results.Add(recurringResult);

                            if (!recurringResult.Created)
                                break;

                            await _db.Entry(contract).ReloadAsync(ct);
                        }
                    }
                }

                var createdCount = results.Count(r => r.Created);
                var message = string.Join(" ",
                    results.Select(r => r.Message)
                           .Where(m => !string.IsNullOrWhiteSpace(m))
                           .Distinct());

                if (createdCount > 0)
                {
                    TempData["Toast.Type"] = "success";
                    TempData["Toast.Title"] = "Done";
                    TempData["Toast.Message"] = message;
                }
                else
                {
                    TempData["Toast.Type"] = "warning";
                    TempData["Toast.Title"] = "Invoice not created";
                    TempData["Toast.Message"] = string.IsNullOrWhiteSpace(message)
                        ? "No invoice was created."
                        : message;
                }
            }
            catch (Exception ex)
            {
                TempData["Toast.Type"] = "warning";
                TempData["Toast.Title"] = "Invoice failed";
                TempData["Toast.Message"] = ex.GetBaseException().Message;
            }

            return RedirectToPage("/Projects/Workspace", new { id = ProjectId, tab = "contracts" });
        }

        private async Task LoadContractStateAsync(CancellationToken ct)
        {
            var contract = await _db.Contracts
                .Include(c => c.Signatures)
                .Where(c => c.ProjectId == ProjectId)
                .OrderByDescending(c => c.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            if (contract is null)
            {
                CurrentContractId = null;
                ShowManualInvoiceButton = false;
                return;
            }

            CurrentContractId = contract.Id;

            var isSigned =
                contract.Signatures.Any(s => s.SignedAt != null) ||
                contract.Status == DocumentStatus.Signed;

            ShowManualInvoiceButton =
                isSigned &&
                contract.InvoiceSendMode == InvoiceSendMode.Manual;
        }
        public async Task<IActionResult> OnPostCreateInvoicePublicLinkAsync(Guid invoiceId, CancellationToken ct)
        {
            try
            {
                if (ProjectId == Guid.Empty || invoiceId == Guid.Empty)
                    return new JsonResult(new
                    {
                        ok = false,
                        toast = new { type = "error", title = "Error", message = "Invalid project or invoice id." }
                    })
                    { StatusCode = 400 };

                var invoice = await _db.Invoices
                    .Include(x => x.Project)
                        .ThenInclude(p => p.Customer)
                            .ThenInclude(c => c.Contacts)
                    .Include(x => x.Project)
                        .ThenInclude(p => p.Customer)
                            .ThenInclude(c => c.EmailAddresses)
                    .FirstOrDefaultAsync(x => x.Id == invoiceId && x.ProjectId == ProjectId, ct);

                if (invoice is null)
                    return new JsonResult(new
                    {
                        ok = false,
                        toast = new { type = "error", title = "Not found", message = "Invoice not found." }
                    })
                    { StatusCode = 404 };

                string? recipientEmail =
                    invoice.Project?.Customer?.Contacts?
                        .OrderByDescending(c => c.IsPrimary)
                        .Select(c => (c.Email ?? "").Trim())
                        .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));

                if (string.IsNullOrWhiteSpace(recipientEmail))
                {
                    recipientEmail =
                        invoice.Project?.Customer?.EmailAddresses?
                            .OrderByDescending(ea => (ea.Kind ?? "").Trim().Equals("business", StringComparison.OrdinalIgnoreCase))
                            .Select(ea => (ea.Email ?? "").Trim())
                            .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));
                }

                var rawToken = await _invoicePublicLinkService.CreateAsync(
                    invoice.Id,
                    recipientEmail,
                    expiresInDays: 14,
                    oneTimeUse: false,
                    ct: ct);

                var publicUrl = Url.Page(
                    pageName: "/Public/Invoices/View",
                    pageHandler: null,
                    values: new { t = rawToken },
                    protocol: Request.Scheme,
                    host: Request.Host.ToUriComponent());

                if (string.IsNullOrWhiteSpace(publicUrl))
                    return new JsonResult(new
                    {
                        ok = false,
                        toast = new { type = "error", title = "Error", message = "Failed to build public link." }
                    })
                    { StatusCode = 500 };

                return new JsonResult(new
                {
                    ok = true,
                    data = new
                    {
                        url = publicUrl,
                        expiresAt = DateTimeOffset.UtcNow.AddDays(14),
                        recipientEmail
                    },
                    toast = new { type = "success", title = "Done", message = "Secure invoice link created successfully." }
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new
                {
                    ok = false,
                    toast = new { type = "error", title = "Server error", message = ex.GetBaseException().Message }
                })
                { StatusCode = 500 };
            }
        }
        public async Task<IActionResult> OnPostChangeInvoiceStatusAsync(
      Guid invoiceId,
      DocumentStatus status,
      CancellationToken ct)
        {
            try
            {
                if (ProjectId == Guid.Empty || invoiceId == Guid.Empty)
                    return new JsonResult(new
                    {
                        ok = false,
                        toast = new { type = "error", title = "Error", message = "Invalid project or invoice id." }
                    })
                    { StatusCode = 400 };

                var allowed =
                    status == DocumentStatus.Open ||
                    status == DocumentStatus.Overdue ||
                    status == DocumentStatus.Paid ||
                    status == DocumentStatus.Cancelled;

                if (!allowed)
                {
                    return new JsonResult(new
                    {
                        ok = false,
                        toast = new
                        {
                            type = "error",
                            title = "Invalid status",
                            message = "Allowed statuses are Open, Overdue, Paid, and Cancelled."
                        }
                    })
                    { StatusCode = 400 };
                }

                var result = await _lexwareInvoiceStatusSyncService
                    .ChangeStatusFromWebsiteAsync(ProjectId, invoiceId, status, ct);

                return new JsonResult(new
                {
                    ok = true,
                    data = new
                    {
                        invoiceId = result.InvoiceId,
                        status = result.LocalStatus,
                        lexwareStatus = result.LexwareStatus
                    },
                    toast = new
                    {
                        type = "success",
                        title = "Done",
                        message = result.Message
                    }
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new
                {
                    ok = false,
                    toast = new { type = "error", title = "Server error", message = ex.GetBaseException().Message }
                })
                { StatusCode = 500 };
            }
        }
        private static string NormalizeTab(string? tab)
        {
            return (tab ?? "").Trim().ToLowerInvariant() switch
            {
                "quotes" => "quotes",
                "invoices" => "invoices",
                "contracts" => "contracts",
                _ => "overview"
            };
        }
    }
}
