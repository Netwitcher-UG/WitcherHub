using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Models.View.Contracts;
using WitcherHub.Application.Models.View.Project;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.Services.Contracts
{
    /// <summary>
    /// Writes the contract from wording edited by hand on the override screen.
    ///
    /// The work is unchanged; where it runs is not. It used to sit in the page
    /// model and execute on the POST that carried the form, so a generation that
    /// takes minutes was answered with HTTP 502 by the platform proxy long before
    /// it finished — and the document it eventually produced was saved into a
    /// request nobody was listening to. Here it can be driven by the background
    /// job queue the other two assistant actions already use.
    /// </summary>
    public sealed class ContractOverrideGenerator : IContractOverrideGenerator
    {
        private readonly IContract _contracts;
        private readonly IProject _projects;
        private readonly IContractDocumentGenerator _generator;
        private readonly IDistributedCache _cache;
        private readonly ILogger<ContractOverrideGenerator> _logger;

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public ContractOverrideGenerator(
            IContract contracts,
            IProject projects,
            IContractDocumentGenerator generator,
            IDistributedCache cache,
            ILogger<ContractOverrideGenerator> logger)
        {
            _contracts = contracts;
            _projects = projects;
            _generator = generator;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>
        /// The working copy the override screen reads back when it reloads. Keyed
        /// per user because it is one person's unsaved edit, not a fact about the
        /// contract — which is why the job has to be told who asked.
        /// </summary>
        public static string SnapshotKey(string? userId, Guid contractId) =>
            $"contract:ai-snap:{(string.IsNullOrWhiteSpace(userId) ? "anon" : userId)}:{contractId}";

        public async Task<ContractOverrideGenerationResult> GenerateAsync(
            Guid contractId,
            ContractStructuredTermsDto structured,
            string? userId,
            CancellationToken ct = default)
        {
            var contract = await _contracts.GetContractAsync(contractId, ct);
            if (contract is null)
                return ContractOverrideGenerationResult.Fail(
                    "That contract no longer exists.", transient: false);

            var project = await _projects.GetProjectAsync(contract.ProjectId, ct);
            if (project is null)
                return ContractOverrideGenerationResult.Fail(
                    "The project this contract belongs to no longer exists.", transient: false);

            // Re-checked here rather than trusted from the request that queued the
            // job: a contract can be signed in the minutes between pressing the
            // button and the work reaching the front of the queue, and rewriting a
            // signed contract is the one outcome that cannot be undone.
            if (IsLocked(contract))
                return ContractOverrideGenerationResult.Fail(
                    "This contract has been signed, so its wording can no longer be changed.",
                    transient: false);

            var request = BuildGenerateRequest(project, contract);
            request.StructuredOverride = structured;

            GenerateContractDocumentResponse document;
            try
            {
                document = await _generator.GenerateAsync(request, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (AiInvocationException ex)
            {
                // The generator already established what went wrong and whether
                // waiting helps. UserMessage carries the cause, the setting to
                // change and a reference that ties it to the log — and never the
                // key, the prompt or the provider's raw response.
                if (ex.NeedsOwnerAction)
                {
                    _logger.LogError(
                        "Override generation for contract {ContractId} cannot succeed until an " +
                        "administrator acts: {Kind}, reference {Reference}.",
                        contractId, ex.Kind, ex.CorrelationId);
                }
                else
                {
                    _logger.LogWarning(
                        "Override generation for contract {ContractId} failed: {Kind}, reference {Reference}.",
                        contractId, ex.Kind, ex.CorrelationId);
                }

                return ContractOverrideGenerationResult.Fail(ex.UserMessage, ex.IsTransient);
            }

            // The screen's own working copy, so reloading after a generation shows
            // what was generated rather than the edit that produced it.
            await SaveSnapshotAsync(userId, contractId, document.Structured, ct);

            await _contracts.UpdateAsync(contractId, new UpdateContractDto
            {
                Contract = new ContractDto
                {
                    ProjectId = contract.ProjectId,
                    Currency = contract.Currency,
                    Status = DocumentStatus.Draft,
                    StartDate = contract.StartDate,
                    EndDate = contract.EndDate,
                    Terms = document.FullDocument,
                    TermsStructured = document.Structured,
                    SignedAt = null
                },
                Items = null
            }, ct);

            _logger.LogInformation(
                "Override generation for contract {ContractId} wrote a new version.", contractId);

            return ContractOverrideGenerationResult.Ok(contract.ProjectId);
        }

        private async Task SaveSnapshotAsync(
            string? userId, Guid contractId, ContractStructuredTermsDto structured, CancellationToken ct)
        {
            try
            {
                await _cache.SetStringAsync(
                    SnapshotKey(userId, contractId),
                    JsonSerializer.Serialize(structured, Json),
                    new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(12) },
                    ct);
            }
            catch (Exception ex)
            {
                // The contract itself is what matters and it is written either
                // way. Losing the screen's working copy is a worse reload, not a
                // failed generation, so it must not fail the job.
                _logger.LogWarning(ex,
                    "The override working copy for contract {ContractId} could not be cached.", contractId);
            }
        }

        private static bool IsLocked(ContractViews.ContractDetailsView contract) =>
            contract.SignedAt is not null ||
            string.Equals(contract.Status.ToString(), "signed", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Moved here unchanged from the page model, which is the only reason it
        /// reads the way it does: it is the same request the synchronous handler
        /// built, so the document produced is the same document.
        /// </summary>
        private static GenerateContractDocumentRequest BuildGenerateRequest(
            ProjectViews.ProjectDetailsView project,
            ContractViews.ContractDetailsView contract)
        {
            var customerName = project.Customer?.Name ?? "";
            var customerEmail = project.Customer?.Email ?? "";

            var customerBlock = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(customerName)) customerBlock.AppendLine(customerName);
            if (!string.IsNullOrWhiteSpace(customerEmail)) customerBlock.AppendLine(customerEmail);

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
                        : JsonSerializer.Deserialize<Dictionary<string, object>>(
                              x.Config.RootElement.GetRawText()) ?? new()
                })
                .ToList();

            return new GenerateContractDocumentRequest
            {
                ContractNo = contract.ContractNo,
                ProjectTitle = project.Title ?? "Project",
                Currency = contract.Currency ?? "EUR",
                StartDate = contract.StartDate ?? project.StartDate,
                EndDate = contract.EndDate ?? project.EndDate,

                SignerName = "",
                SignerEmail = customerEmail,

                LeaveCustomerFieldsBlank = false,
                IncludePricesInServicesSection = true,
                CustomerBlockOverride = customerBlock.ToString().TrimEnd(),

                Services = lines,
                ProjectId = contract.ProjectId
            };
        }
    }
}
