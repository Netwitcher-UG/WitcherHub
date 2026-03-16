using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Services.Lexware;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Projects
{
    public class WorkspaceModel : PageModel
    {
        private readonly IProject _projects;
        private readonly AppDbContext _db;
        private readonly LexwareInvoiceSyncService _lexwareInvoiceSyncService;

        public WorkspaceModel(
            IProject projects,
            AppDbContext db,
            LexwareInvoiceSyncService lexwareInvoiceSyncService)
        {
            _projects = projects;
            _db = db;
            _lexwareInvoiceSyncService = lexwareInvoiceSyncService;
        }

        [BindProperty(SupportsGet = true, Name = "id")]
        public Guid ProjectId { get; set; }

        [BindProperty(SupportsGet = true, Name = "tab")]
        public string? Tab { get; set; }

        public string ProjectTitle { get; private set; } = "Project Workspace";

        public Guid? CurrentContractId { get; private set; }
        public bool ShowManualInvoiceButton { get; private set; }

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

            await LoadContractStateAsync(ct);

            return Page();
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
