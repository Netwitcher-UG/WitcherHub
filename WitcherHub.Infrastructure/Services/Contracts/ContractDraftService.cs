using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using WitcherHub.Infrastructure.Services.OpenAI;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.Services.Contracts
{
    /// <summary>
    /// Produces contract wording from confirmed positions, one version at a time.
    ///
    /// The model writes prose. It is given the commercial figures as fixed facts to
    /// restate, and the figures stored on the contract are never taken back from
    /// its answer — the positions remain the source of truth for what was agreed.
    /// </summary>
    public sealed class ContractDraftService : IContractDraftService
    {
        public const string CurrentPromptVersion = "contract-draft-v1";

        private readonly AppDbContext _db;
        private readonly IContractPositions _positions;
        private readonly IAiTextGenerator _ai;
        private readonly OpenAIOptions _openAi;
        private readonly ContractTemplateOptions _template;
        private readonly ILogger<ContractDraftService> _logger;

        public ContractDraftService(
            AppDbContext db,
            IContractPositions positions,
            IAiTextGenerator ai,
            IOptions<OpenAIOptions> openAi,
            IOptions<ContractTemplateOptions> template,
            ILogger<ContractDraftService> logger)
        {
            _db = db;
            _positions = positions;
            _ai = ai;
            _openAi = openAi.Value;
            _template = template.Value;
            _logger = logger;
        }

        public async Task<ContractDraftResult> GenerateAsync(
            Guid contractId, GenerateDraftOptions options, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(options);

            var contract = await LoadAsync(contractId, ct);

            var approved = contract.Drafts.FirstOrDefault(d => d.IsApproved);
            if (approved is not null && !options.OverwriteApproved)
            {
                return ContractDraftResult.NeedsConfirmation(
                    $"Version {approved.Version} has already been approved. Confirm that you want to replace the approved wording.");
            }

            var positions = await _positions.GetPositionsAsync(contractId, ct);
            if (positions.Count == 0)
                return ContractDraftResult.Failed("Add at least one position before generating a contract.");

            var totals = _positions.CalculateTotals(positions, contract.Currency);

            string document;
            try
            {
                document = await _ai.GenerateTextAsync(BuildPrompt(contract, positions, totals, options));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The positions are already saved. The user loses nothing and can
                // write or paste the wording by hand.
                _logger.LogWarning(ex, "Contract draft generation failed for {ContractId}.", contractId);

                return ContractDraftResult.Failed(
                    "The assistant could not produce a draft. Your positions are saved — you can write the contract text by hand and save it.",
                    transient: true);
            }

            if (string.IsNullOrWhiteSpace(document))
            {
                return ContractDraftResult.Failed(
                    "The assistant returned an empty document. Your positions are saved.",
                    transient: true);
            }

            var draft = new ContractDraft
            {
                ContractId = contractId,
                Version = await NextVersionAsync(contractId, ct),
                DocumentMarkdown = document.Trim(),
                PositionsSnapshot = JsonSerializer.SerializeToDocument(positions),
                PromptVersion = CurrentPromptVersion,
                TemplateVersion = _template.BaseDePath,
                Model = _openAi.Model,
                GeneratedBy = "openai",
                GeneratedAt = DateTimeOffset.UtcNow
            };

            _db.Set<ContractDraft>().Add(draft);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Generated draft v{Version} for contract {ContractId} using {Model}.",
                draft.Version, contractId, _openAi.Model);

            return new ContractDraftResult { Succeeded = true, Draft = ToSummary(draft, totals) };
        }

        public async Task<IReadOnlyList<ContractDraftSummary>> GetDraftsAsync(
            Guid contractId, CancellationToken ct = default)
        {
            var drafts = await _db.Set<ContractDraft>()
                .AsNoTracking()
                .Where(d => d.ContractId == contractId)
                .OrderByDescending(d => d.Version)
                .ToListAsync(ct);

            return drafts.Select(d => ToSummary(d, null)).ToList();
        }

        public async Task<ContractDraftSummary?> GetDraftAsync(
            Guid contractId, int version, CancellationToken ct = default)
        {
            var draft = await _db.Set<ContractDraft>()
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.ContractId == contractId && d.Version == version, ct);

            return draft is null ? null : ToSummary(draft, null);
        }

        public async Task<ContractDraftResult> SaveEditedAsync(
            Guid contractId, int version, string documentMarkdown, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(documentMarkdown))
                return ContractDraftResult.Failed("The contract text cannot be empty.");

            var draft = await _db.Set<ContractDraft>()
                .FirstOrDefaultAsync(d => d.ContractId == contractId && d.Version == version, ct)
                ?? throw new NotFoundAppException("Draft not found.");

            if (draft.IsApproved)
            {
                return ContractDraftResult.NeedsConfirmation(
                    "This version is approved and cannot be edited. Generate a new version instead.");
            }

            draft.DocumentMarkdown = documentMarkdown.Trim();
            draft.GeneratedBy = "human-edit";

            await _db.SaveChangesAsync(ct);

            return new ContractDraftResult { Succeeded = true, Draft = ToSummary(draft, null) };
        }

        public async Task<ContractDraftResult> ApproveAsync(
            Guid contractId, int version, Guid? approvedById, CancellationToken ct = default)
        {
            var contract = await LoadAsync(contractId, ct);

            var draft = contract.Drafts.FirstOrDefault(d => d.Version == version)
                ?? throw new NotFoundAppException("Draft not found.");

            // Only one approved version at a time: two would leave it ambiguous
            // which text a signature applies to.
            foreach (var other in contract.Drafts.Where(d => d.IsApproved && d.Version != version))
            {
                other.IsApproved = false;
                other.ApprovedAt = null;
            }

            draft.IsApproved = true;
            draft.ApprovedAt = DateTimeOffset.UtcNow;
            draft.ApprovedById = approvedById;
            draft.DocumentHash = Sha256(draft.DocumentMarkdown);

            // The approved wording becomes the contract's terms, which is what the
            // signing page and the PDF read from.
            contract.Terms = draft.DocumentMarkdown;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Draft v{Version} approved for contract {ContractId}. Hash {Hash}.",
                version, contractId, draft.DocumentHash);

            return new ContractDraftResult { Succeeded = true, Draft = ToSummary(draft, null) };
        }

        // -------------------------------------------------------------------

        private async Task<Contract> LoadAsync(Guid contractId, CancellationToken ct)
        {
            if (contractId == Guid.Empty)
                throw new BadRequestAppException("Invalid contract id.");

            return await _db.Contracts
                .Include(c => c.Drafts)
                .FirstOrDefaultAsync(c => c.Id == contractId, ct)
                ?? throw new NotFoundAppException("Contract not found.");
        }

        private async Task<int> NextVersionAsync(Guid contractId, CancellationToken ct)
        {
            var highest = await _db.Set<ContractDraft>()
                .Where(d => d.ContractId == contractId)
                .Select(d => (int?)d.Version)
                .MaxAsync(ct) ?? 0;

            return highest + 1;
        }

        private static string BuildPrompt(
            Contract contract,
            IReadOnlyList<ManualPositionDto> positions,
            PositionTotalsDto totals,
            GenerateDraftOptions options)
        {
            // Deliberately excludes customer identity: the contract template fills
            // the parties in, and the model does not need personal data to write the
            // service sections.
            var payload = positions.Select(p => new
            {
                position = p.Position,
                title = p.Title,
                serviceType = p.ServiceType,
                description = p.Description,
                scope = p.Scope,
                deliverables = p.Deliverables,
                quantity = p.Quantity,
                unit = p.Unit,
                pricingModel = p.PricingModel.ToString(),
                unitPrice = p.UnitPrice,
                currency = p.Currency,
                vatRate = p.VatRate,
                discountType = p.DiscountType?.ToString(),
                discountValue = p.DiscountValue,
                netTotal = p.NetTotal,
                billingCycle = p.BillingCycle.ToString(),
                durationPeriods = p.DurationPeriods,
                deliveryMethod = p.DeliveryMethod,
                activationMethod = p.ActivationMethod.ToString(),
                startDate = p.StartDate?.ToString("yyyy-MM-dd"),
                deliveryDate = p.DeliveryDate?.ToString("yyyy-MM-dd"),
                acceptanceCriteria = p.AcceptanceCriteria,
                customerResponsibilities = p.CustomerResponsibilities,
                assumptions = p.Assumptions,
                exclusions = p.Exclusions,
                notes = p.Notes
            });

            return $$"""
                Write the service schedule of an agency contract, in {{options.Language}}, as Markdown.

                Produce these sections, in order:
                1. Gegenstand des Vertrags — what is being delivered, in prose.
                2. Leistungsumfang — one subsection per position, using its scope and deliverables.
                3. Mitwirkungspflichten des Kunden — customer responsibilities.
                4. Abnahme — acceptance criteria.
                5. Annahmen und Ausschlüsse — assumptions and exclusions.
                6. Vergütung und Zahlung — restate the figures below exactly.
                7. Laufzeit und Aktivierung — billing cycle, duration, activation.

                Rules:
                - Restate every number exactly as given. Do not recalculate, round or
                  convert anything, and do not add a figure that is not below.
                - Do not add legal clauses, liability terms, warranties or payment
                  obligations beyond the figures given. Separate legal sections are
                  appended from an approved template.
                - Do not invent services, dates or parties.
                - If information is missing, write "wird noch festgelegt" rather than
                  inventing a value.
                - Output only the contract Markdown. No commentary.

                Currency: {{totals.Currency}}
                Net total: {{totals.Subtotal}}
                Discount: {{totals.Discount}}
                VAT: {{totals.Vat}}
                Gross total: {{totals.Total}}
                Contract period: {{contract.StartDate?.ToString("yyyy-MM-dd") ?? "not set"}} to {{contract.EndDate?.ToString("yyyy-MM-dd") ?? "not set"}}

                Positions:
                {{JsonSerializer.Serialize(payload)}}

                {{(string.IsNullOrWhiteSpace(options.AdditionalInstructions)
                    ? ""
                    : "Additional guidance for the wording only:\n" + options.AdditionalInstructions)}}
                """;
        }

        private static ContractDraftSummary ToSummary(ContractDraft d, PositionTotalsDto? totals) =>
            new(d.Id, d.Version, d.DocumentMarkdown, d.Model, d.PromptVersion, d.TemplateVersion,
                d.GeneratedBy, d.GeneratedAt, d.IsApproved, d.ApprovedAt, d.DocumentHash, totals);

        internal static string Sha256(string value)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
