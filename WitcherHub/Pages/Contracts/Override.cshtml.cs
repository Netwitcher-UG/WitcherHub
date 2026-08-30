using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Models.View.Contracts;
using WitcherHub.Application.Models.View.Project;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Contracts
{
    [Authorize]
    public class OverrideModel : PageModel
    {
        private readonly IContract _contracts;
        private readonly IProject _projects;
        private readonly IDistributedCache _cache;
        private readonly IContractAiJobs _jobs;
        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };
        public OverrideModel(IContract contracts, IProject projects,
            IDistributedCache cache, IContractAiJobs jobs)
        {
            _contracts = contracts;
            _projects = projects;
            _cache = cache;
            _jobs = jobs;
        }

        [BindProperty(SupportsGet = true, Name = "id")]
        public Guid Id { get; set; }

        [BindProperty]
        public ContractOverrideViewModel Vm { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (Id == Guid.Empty) return NotFound();

            var contract = await _contracts.GetContractAsync(Id, ct);
            if (contract is null) return NotFound();

            var prj = await _projects.GetProjectAsync(contract.ProjectId, ct);
            if (prj is null) return NotFound();

            await EnsureAiSnapshotFromDbIfMissingAsync(contract, ct);

            Vm = BuildVm(contract, prj, IsContractLocked(contract));
            return Page();
        }

        public async Task<IActionResult> OnPostAddPositionAsync(CancellationToken ct)
        {
            NormalizeListsFromForm();

            if (Vm.ContractId == Guid.Empty) return BadRequest();

            var contract = await _contracts.GetContractAsync(Vm.ContractId, ct);
            if (contract is null) return NotFound();
            if (IsContractLocked(contract)) throw new BadRequestAppException("Contract is locked.");

            Vm.Positions ??= new();

            var nextNo = Vm.Positions.Count == 0 ? 1 : Vm.Positions.Max(p => p.PositionNo) + 1;

            Vm.Positions.Add(new ContractOverrideViewModel.PositionVm
            {
                PositionNo = nextNo,
                Title = $"Position {nextNo}",
                Sections = new ContractOverrideViewModel.PositionVm.SectionsVm()
            });

            return Page();
        }

        public async Task<IActionResult> OnPostDeletePositionAsync(int posNo, CancellationToken ct)
        {
            NormalizeListsFromForm();

            if (Vm.ContractId == Guid.Empty) return BadRequest();

            var contract = await _contracts.GetContractAsync(Vm.ContractId, ct);
            if (contract is null) return NotFound();
            if (IsContractLocked(contract)) throw new BadRequestAppException("Contract is locked.");

            Vm.Positions ??= new();
            Vm.Positions = Vm.Positions.Where(p => p.PositionNo != posNo).ToList();

            // repack 1..n
            int i = 1;
            foreach (var p in Vm.Positions.OrderBy(x => x.PositionNo))
                p.PositionNo = i++;

            return Page();
        }

        public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
        {
            NormalizeListsFromForm();

            if (Vm.ContractId == Guid.Empty) return BadRequest();

            var contract = await _contracts.GetContractAsync(Vm.ContractId, ct);
            if (contract is null) return NotFound();
            if (IsContractLocked(contract)) throw new BadRequestAppException("Contract is locked.");

            var structured = MapVmToStructured(Vm);

            var update = new UpdateContractDto
            {
                Contract = new ContractDto
                {
                    ProjectId = contract.ProjectId,
                    Currency = contract.Currency,
                    Status = contract.Status,
                    StartDate = contract.StartDate,
                    EndDate = contract.EndDate,
                    Terms = contract.Terms,
                    TermsStructured = structured,
                    SignedAt = contract.SignedAt
                },
                Items = null
            };

            await _contracts.UpdateAsync(contract.Id, update, ct);

            TempData["Toast.Type"] = "success";
            TempData["Toast.Title"] = "Saved";
            TempData["Toast.Message"] = "Structured terms saved.";

            return RedirectToPage("/Contracts/Override", new { id = contract.Id });
        }

        /// <summary>
        /// Queues the rewrite and answers at once.
        ///
        /// This used to call the model on this very request and answer with the
        /// finished contract. Writing a contract takes longer than the platform
        /// proxy will hold a connection open, so the browser was shown HTTP 502
        /// while the work was still going and the document landed in a request
        /// nobody was listening to — the same fault that moved the positions
        /// screen onto the job queue, still standing here until now.
        ///
        /// The form is still posted as a form, so the model binding that fills the
        /// view model is untouched; only what happens next has changed. The page
        /// polls <see cref="OnPostAiJobStatusAsync"/> from here.
        /// </summary>
        public async Task<IActionResult> OnPostGenerateAsync(CancellationToken ct)
        {
            NormalizeListsFromForm();

            if (Vm.ContractId == Guid.Empty)
                return new JsonResult(new { ok = false, transient = false, message = "No contract was given." });

            var contract = await _contracts.GetContractAsync(Vm.ContractId, ct);

            if (contract is null)
                return new JsonResult(new { ok = false, transient = false, message = "That contract no longer exists." });

            if (IsContractLocked(contract))
            {
                return new JsonResult(new
                {
                    ok = false,
                    transient = false,
                    message = "This contract has been signed, so its wording can no longer be changed."
                });
            }

            var started = await _jobs.StartAsync(
                Vm.ContractId,
                ContractAiJobKind.Override,
                new OverrideJobRequest
                {
                    Structured = MapVmToStructured(Vm),

                    // The working copy this screen reads back is cached per user,
                    // and the job has no signed-in user of its own.
                    UserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                },

                // Sent by the browser, so a second press or a retry after a
                // timeout joins the first job rather than writing the contract
                // twice.
                requestKey: Vm.IdempotencyKey,
                ct);

            if (!started.Running)
            {
                return new JsonResult(new
                {
                    ok = false,
                    transient = false,
                    message = started.FailureReason
                });
            }

            return new JsonResult(new
            {
                ok = true,
                running = true,
                jobId = started.JobId,
                alreadyRunning = started.AlreadyRunning,
                message = started.AlreadyRunning
                    ? "This contract is already being written. Waiting for it to finish\u2026"
                    : "Writing the contract\u2026"
            });
        }

        /// <summary>
        /// How the rewrite is getting on. Polled by the page while it runs.
        ///
        /// The same shape the positions screen polls, so both are read by the same
        /// kind of loop and a failure is reported the same way on both.
        /// </summary>
        public async Task<IActionResult> OnPostAiJobStatusAsync(
            [FromBody] AiJobRequest? request, CancellationToken ct)
        {
            if (request is null || request.JobId == Guid.Empty)
                return new JsonResult(new { ok = false, transient = false, message = "No request given." });

            var state = await _jobs.GetAsync(request.JobId, ct);

            if (state.Running)
            {
                return new JsonResult(new
                {
                    ok = true,
                    running = true,
                    elapsedSeconds = (int)(state.Elapsed?.TotalSeconds ?? 0)
                });
            }

            if (state.Failed)
            {
                return new JsonResult(new
                {
                    ok = false,
                    running = false,
                    transient = state.IsTransientFailure,
                    message = state.FailureReason
                });
            }

            return Content(
                $"{{\"ok\":true,\"running\":false,\"result\":{state.ResultJson ?? "null"}}}",
                "application/json");
        }

        public sealed class AiJobRequest
        {
            public Guid JobId { get; set; }
        }

        // =====================
        // Helpers
        // =====================

        private static bool IsContractLocked(ContractViews.ContractDetailsView c)
        {
            var isSigned = c.SignedAt is not null;
            var st = c.Status.ToString().ToLowerInvariant();
            return isSigned || st == "signed";
        }

        private static ContractOverrideViewModel BuildVm(
            ContractViews.ContractDetailsView contract,
            ProjectViews.ProjectDetailsView prj,
            bool isLocked)
        {
            // load structured from DB
            ContractStructuredTermsDto? structured = null;

            if (contract.TermsStructured != null)
            {
                try
                {
                    structured = JsonSerializer.Deserialize<ContractStructuredTermsDto>(
                        contract.TermsStructured.RootElement.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch { structured = null; }
            }

            structured ??= BuildStructuredFromItems(contract);

            return new ContractOverrideViewModel
            {
                ContractId = contract.Id,
                ProjectId = contract.ProjectId,
                ContractNo = contract.ContractNo,
                Currency = contract.Currency ?? "EUR",
                ProjectTitle = prj.Title ?? "Project",
                CustomerName = prj.Customer?.Name ?? "",
                CustomerEmail = prj.Customer?.Email ?? "",
                IsLocked = isLocked,
                Positions = structured.Positions
                    .OrderBy(p => p.PositionNo)
                    .Select(p => new ContractOverrideViewModel.PositionVm
                    {
                        PositionNo = p.PositionNo,
                        Title = p.Title ?? "",
                        LineNetPrice = p.LineNetPrice,
                        Sections = new ContractOverrideViewModel.PositionVm.SectionsVm
                        {
                            Scope = p.Sections?.Scope ?? "",
                            Deliverables = p.Sections?.Deliverables ?? new List<string>(),
                            OutOfScope = p.Sections?.OutOfScope ?? new List<string>(),
                            CustomerResponsibilities = p.Sections?.CustomerResponsibilities ?? new List<string>(),
                            AcceptanceCriteria = p.Sections?.AcceptanceCriteria ?? new List<string>(),
                            Timeline = p.Sections?.Timeline ?? "",
                            Assumptions = p.Sections?.Assumptions ?? "",
                            Revisions = p.Sections?.Revisions ?? ""
                        }
                    })
                    .ToList()
            };
        }

        private static ContractStructuredTermsDto BuildStructuredFromItems(ContractViews.ContractDetailsView contract)
        {
            var dto = new ContractStructuredTermsDto
            {
                Version = "1.0",
                Language = "de-DE",
                Positions = new List<ContractPositionSpecDto>()
            };

            var items = (contract.Items ?? new List<ContractViews.ContractItemItemView>())
                .OrderBy(x => x.Position)
                .ToList();

            // This screen edits the structured terms built out of positions, so it
            // genuinely needs them — unlike generation, which does not. A contract
            // whose source is supplied text is edited as text instead, and saying
            // so is the difference between a dead end and a direction.
            if (items.Count == 0)
            {
                throw new BadRequestAppException(
                    "This screen edits the terms built from contract positions, and this contract has none. " +
                    "A contract built from supplied text is edited as text on the contract builder.");
            }

            foreach (var it in items)
            {
                dto.Positions.Add(new ContractPositionSpecDto
                {
                    PositionNo = it.Position,
                    Title = it.Title ?? it.ServiceName ?? $"Position {it.Position}",
                    Quantity = 1,
                    UnitNetPrice = null,
                    LineNetPrice = it.AgreedPrice,
                    TaxRatePercent = 19,
                    Sections = new ContractPositionSectionsDto
                    {
                        Scope = "",
                        Deliverables = new List<string>(),
                        OutOfScope = new List<string>(),
                        CustomerResponsibilities = new List<string>(),
                        AcceptanceCriteria = new List<string>(),
                        Timeline = "",
                        Assumptions = "",
                        Revisions = ""
                    },
                    CustomClauses = new List<ContractCustomClauseDto>(),
                    AiRaw = null
                });
            }

            return dto;
        }

        private static ContractStructuredTermsDto MapVmToStructured(ContractOverrideViewModel vm)
        {
            vm.Positions ??= new();

            return new ContractStructuredTermsDto
            {
                Version = "1.0",
                Language = "de-DE",
                Positions = vm.Positions
                    .OrderBy(p => p.PositionNo)
                    .Select(p => new ContractPositionSpecDto
                    {
                        PositionNo = p.PositionNo,
                        Title = p.Title ?? "",
                        Quantity = 1,
                        UnitNetPrice = null,
                        LineNetPrice = p.LineNetPrice,
                        TaxRatePercent = 19,
                        Sections = new ContractPositionSectionsDto
                        {
                            Scope = p.Sections?.Scope ?? "",
                            Deliverables = p.Sections?.Deliverables ?? new List<string>(),
                            OutOfScope = p.Sections?.OutOfScope ?? new List<string>(),
                            CustomerResponsibilities = p.Sections?.CustomerResponsibilities ?? new List<string>(),
                            AcceptanceCriteria = p.Sections?.AcceptanceCriteria ?? new List<string>(),
                            Timeline = p.Sections?.Timeline ?? "",
                            Assumptions = p.Sections?.Assumptions ?? "",
                            Revisions = p.Sections?.Revisions ?? ""
                        },
                        CustomClauses = new List<ContractCustomClauseDto>(),
                        AiRaw = null
                    })
                    .ToList(),
                GeneratedBy = "override",
                GeneratedAt = DateTimeOffset.UtcNow
            };
        }

        // BuildGenerateRequest moved to ContractOverrideGenerator, which is where
        // the generation now happens: this page queues the work and polls it.

        private void NormalizeListsFromForm()
        {
            if (Vm?.Positions == null) return;

            for (int i = 0; i < Vm.Positions.Count; i++)
            {
                Vm.Positions[i].Sections ??= new ContractOverrideViewModel.PositionVm.SectionsVm();

                Vm.Positions[i].Sections.Deliverables =
                    SplitLines(Request.Form[$"Vm.Positions[{i}].Sections.DeliverablesText"]);

                Vm.Positions[i].Sections.OutOfScope =
                    SplitLines(Request.Form[$"Vm.Positions[{i}].Sections.OutOfScopeText"]);

                Vm.Positions[i].Sections.CustomerResponsibilities =
                    SplitLines(Request.Form[$"Vm.Positions[{i}].Sections.CustomerResponsibilitiesText"]);

                Vm.Positions[i].Sections.AcceptanceCriteria =
                    SplitLines(Request.Form[$"Vm.Positions[{i}].Sections.AcceptanceCriteriaText"]);
            }

            static List<string> SplitLines(string? raw)
            {
                raw ??= "";
                return raw.Replace("\r\n", "\n")
                          .Split('\n')
                          .Select(x => x.Trim())
                          .Where(x => !string.IsNullOrWhiteSpace(x))
                          .ToList();
            }
        }
        private string AiSnapshotKey(Guid contractId)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anon";
            return $"contract:ai-snap:{uid}:{contractId}";
        }

        private async Task SaveAiSnapshotAsync(Guid contractId, ContractStructuredTermsDto structured, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(structured, _jsonOpts);

            await _cache.SetStringAsync(
                AiSnapshotKey(contractId),
                json,
                new DistributedCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromHours(12) // عدّلها حسب رغبتك
                },
                ct);
        }

        private async Task<ContractStructuredTermsDto?> GetAiSnapshotAsync(Guid contractId, CancellationToken ct)
        {
            var json = await _cache.GetStringAsync(AiSnapshotKey(contractId), ct);
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                return JsonSerializer.Deserialize<ContractStructuredTermsDto>(json, _jsonOpts);
            }
            catch
            {
                return null;
            }
        }

        private static ContractStructuredTermsDto? TryDeserializeStructured(ContractViews.ContractDetailsView contract)
        {
            if (contract.TermsStructured is null) return null;

            try
            {
                return JsonSerializer.Deserialize<ContractStructuredTermsDto>(
                    contract.TermsStructured.RootElement.GetRawText(),
                    _jsonOpts);
            }
            catch
            {
                return null;
            }
        }

        private async Task EnsureAiSnapshotFromDbIfMissingAsync(ContractViews.ContractDetailsView contract, CancellationToken ct)
        {
            var existing = await _cache.GetStringAsync(AiSnapshotKey(contract.Id), ct);
            if (!string.IsNullOrWhiteSpace(existing)) return;

            var structured = TryDeserializeStructured(contract);
            if (structured == null) return;

            if (string.Equals(structured.GeneratedBy, "override", StringComparison.OrdinalIgnoreCase))
                return;

            await SaveAiSnapshotAsync(contract.Id, structured, ct);
        }

        private static ContractOverrideViewModel BuildVmFromStructured(
            ContractViews.ContractDetailsView contract,
            ProjectViews.ProjectDetailsView prj,
            bool isLocked,
            ContractStructuredTermsDto structured)
        {
            return new ContractOverrideViewModel
            {
                ContractId = contract.Id,
                ProjectId = contract.ProjectId,
                ContractNo = contract.ContractNo,
                Currency = contract.Currency ?? "EUR",
                ProjectTitle = prj.Title ?? "Project",
                CustomerName = prj.Customer?.Name ?? "",
                CustomerEmail = prj.Customer?.Email ?? "",
                IsLocked = isLocked,
                Positions = (structured.Positions ?? new List<ContractPositionSpecDto>())
                    .OrderBy(p => p.PositionNo)
                    .Select(p => new ContractOverrideViewModel.PositionVm
                    {
                        PositionNo = p.PositionNo,
                        Title = p.Title ?? "",
                        LineNetPrice = p.LineNetPrice,
                        Sections = new ContractOverrideViewModel.PositionVm.SectionsVm
                        {
                            Scope = p.Sections?.Scope ?? "",
                            Deliverables = p.Sections?.Deliverables ?? new List<string>(),
                            OutOfScope = p.Sections?.OutOfScope ?? new List<string>(),
                            CustomerResponsibilities = p.Sections?.CustomerResponsibilities ?? new List<string>(),
                            AcceptanceCriteria = p.Sections?.AcceptanceCriteria ?? new List<string>(),
                            Timeline = p.Sections?.Timeline ?? "",
                            Assumptions = p.Sections?.Assumptions ?? "",
                            Revisions = p.Sections?.Revisions ?? ""
                        }
                    })
                    .ToList()
            };
        }
        public async Task<IActionResult> OnPostResetAsync(CancellationToken ct)
        {
            if (Vm.ContractId == Guid.Empty) return BadRequest();

            var contract = await _contracts.GetContractAsync(Vm.ContractId, ct);
            if (contract is null) return NotFound();
            if (IsContractLocked(contract)) throw new BadRequestAppException("Contract is locked.");

            var prj = await _projects.GetProjectAsync(contract.ProjectId, ct);
            if (prj is null) return NotFound();

            var snap = await GetAiSnapshotAsync(contract.Id, ct);
            if (snap is null)
            {
                TempData["Toast.Type"] = "warning";
                TempData["Toast.Title"] = "Reset";
                TempData["Toast.Message"] = "No GPT snapshot found in cache (maybe expired).";
                Vm = BuildVm(contract, prj, false);
                return Page();
            }

            Vm = BuildVmFromStructured(contract, prj, false, snap);

            TempData["Toast.Type"] = "success";
            TempData["Toast.Title"] = "Reset";
            TempData["Toast.Message"] = "Restored GPT version from cache.";

            return Page(); 
        }
    }
}