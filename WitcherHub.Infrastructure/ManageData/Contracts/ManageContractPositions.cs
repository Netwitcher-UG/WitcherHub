using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.ManageData.Contracts
{
    /// <summary>
    /// Persists contract positions, whether they came from the Service Catalog or
    /// were typed in by hand.
    ///
    /// Manual positions are stored as ordinary ContractItem rows with ServiceId
    /// left null. Every position also carries a snapshot of the agreed terms, so a
    /// later edit to a catalog service cannot change what a signed contract says.
    /// </summary>
    public sealed class ManageContractPositions : IContractPositions
    {
        private static readonly JsonSerializerOptions SnapshotOptions = new()
        {
            WriteIndented = false
        };

        private readonly AppDbContext _db;
        private readonly ILogger<ManageContractPositions> _logger;

        public ManageContractPositions(AppDbContext db, ILogger<ManageContractPositions> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<IReadOnlyList<ManualPositionDto>> GetPositionsAsync(
            Guid contractId, CancellationToken ct = default)
        {
            if (contractId == Guid.Empty)
                throw new BadRequestAppException("Invalid contract id.");

            var items = await _db.ContractItems
                .AsNoTracking()
                .Where(x => x.ContractId == contractId)
                .OrderBy(x => x.Position)
                .ToListAsync(ct);

            return items.Select(ToDto).ToList();
        }

        public PositionTotalsDto CalculateTotals(
            IReadOnlyList<ManualPositionDto> positions, string fallbackCurrency = "EUR")
            => PositionTotalsDto.From(positions, fallbackCurrency);

        public async Task<PositionTotalsDto> SavePositionsAsync(
            Guid contractId,
            IReadOnlyList<ManualPositionDto> positions,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(positions);

            if (contractId == Guid.Empty)
                throw new BadRequestAppException("Invalid contract id.");

            var contract = await _db.Contracts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.Id == contractId, ct)
                ?? throw new NotFoundAppException("Contract not found.");

            if (contract.Status != DocumentStatus.Draft)
                throw new BadRequestAppException("Positions can only be changed while the contract is a draft.");

            // An empty list is a legitimate save. A contract built from supplied
            // text has no positions, and refusing to store that left the builder
            // in a state it could not leave: the browser asked the user to save
            // before generating, and saving was refused for having nothing to
            // save. Only a contract with no text either is short of a source, and
            // that is caught where generation is decided rather than here.
            if (positions.Count == 0)
            {
                var hasText = await _db.Set<ContractDraft>().AnyAsync(d => d.ContractId == contractId, ct);

                if (!hasText && contract.Items.Count == 0)
                {
                    throw new BadRequestAppException(
                        "This contract has neither positions nor contract text. Add a position, or paste the " +
                        "contract text, and it can be built from either.");
                }
            }

            // A position may reference the catalog, but it never has to. Verify the
            // ones that do, so a stale id cannot produce a dangling reference.
            var catalogIds = positions
                .Where(p => p.CatalogServiceId.HasValue)
                .Select(p => p.CatalogServiceId!.Value)
                .Distinct()
                .ToList();

            if (catalogIds.Count > 0)
            {
                var known = await _db.Set<ServiceCatalogItem>()
                    .Where(s => catalogIds.Contains(s.Id))
                    .Select(s => s.Id)
                    .ToListAsync(ct);

                var unknown = catalogIds.Except(known).ToList();
                if (unknown.Count > 0)
                    throw new BadRequestAppException("One of the positions refers to a service that no longer exists.");
            }

            // The commercial terms behind any positions adopted from an analysed
            // document, so the structure a priced line cannot hold — phases, a
            // delivery frequency separate from the billing one, caps, the passage
            // it was read from — is stored beside the line rather than discarded.
            var terms = await LoadAdoptedTermsAsync(contractId, positions, ct);

            var existing = contract.Items.ToDictionary(i => i.Id);
            var keptIds = new HashSet<Guid>();
            var order = 1;

            foreach (var dto in positions)
            {
                dto.Position = order++;

                var item = dto.ContractItemId.HasValue && existing.TryGetValue(dto.ContractItemId.Value, out var found)
                    ? found
                    : null;

                if (item is null)
                {
                    item = new ContractItem { ContractId = contractId };
                    _db.ContractItems.Add(item);
                }

                Apply(dto, item);

                if (dto.SourceTermKey is not null && terms.TryGetValue(dto.SourceTermKey, out var term))
                {
                    item.CommercialTerm = term;

                    // A person chose this line and can edit it before saving, so it
                    // is reviewed by the time it gets here. Re-analysis must not
                    // silently overwrite it.
                    item.IsHumanReviewed = true;
                }

                keptIds.Add(item.Id);
            }

            // Positions the user removed.
            var removed = contract.Items.Where(i => i.Id != Guid.Empty && !keptIds.Contains(i.Id)).ToList();
            if (removed.Count > 0)
                _db.ContractItems.RemoveRange(removed);

            await _db.SaveChangesAsync(ct);

            var totals = PositionTotalsDto.From(positions, contract.Currency);

            _logger.LogInformation(
                "Saved {Count} position(s) for contract {ContractId} ({Manual} manual).",
                positions.Count, contractId, positions.Count(p => p.SourceType == ContractItemSource.Manual));

            return totals;
        }

        // -------------------------------------------------------------------

        /// <summary>
        /// The stored commercial terms for the positions being saved, by key.
        ///
        /// Reads them out of the semantic analysis kept against the contract's
        /// drafts. A key that matches nothing simply yields no term: the position is
        /// saved as the priced line it is, which is what happens for every manually
        /// entered position anyway.
        /// </summary>
        private async Task<Dictionary<string, JsonDocument>> LoadAdoptedTermsAsync(
            Guid contractId,
            IReadOnlyList<ManualPositionDto> positions,
            CancellationToken ct)
        {
            var wanted = positions
                .Select(p => p.SourceTermKey)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct()
                .ToHashSet(StringComparer.Ordinal);

            if (wanted.Count == 0) return new Dictionary<string, JsonDocument>(StringComparer.Ordinal);

            var analyses = await _db.Set<ContractDraft>()
                .AsNoTracking()
                .Where(d => d.ContractId == contractId && d.SemanticAnalysis != null)
                .OrderByDescending(d => d.Version)
                .Select(d => d.SemanticAnalysis!)
                .ToListAsync(ct);

            var found = new Dictionary<string, JsonDocument>(StringComparer.Ordinal);

            foreach (var analysis in analyses)
            {
                if (!analysis.RootElement.TryGetProperty("terms", out var terms) ||
                    terms.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var term in terms.EnumerateArray())
                {
                    if (!term.TryGetProperty("key", out var key)) continue;

                    var value = key.GetString();
                    if (value is null || !wanted.Contains(value) || found.ContainsKey(value)) continue;

                    // Newest version wins: drafts are read newest first, so a term
                    // re-read by a later analysis supersedes the earlier reading.
                    found[value] = JsonDocument.Parse(term.GetRawText());
                }
            }

            return found;
        }

        private static void Apply(ManualPositionDto dto, ContractItem item)
        {
            item.Title = (dto.Title ?? "").Trim();
            item.Description = string.IsNullOrWhiteSpace(dto.Description)
                ? item.Title
                : dto.Description!.Trim();

            item.Source = dto.SourceType;

            // Null for a manual position, and equally null for one read out of a
            // supplied contract: neither has a catalog service behind it, and a
            // placeholder catalog row would be inventing a service we do not sell.
            item.ServiceId = dto.SourceType is ContractItemSource.Manual
                                            or ContractItemSource.ExtractedFromContractText
                ? null
                : dto.CatalogServiceId;

            item.SourceDraftId = dto.SourceDraftId;

            item.ServiceTypeLabel = dto.ServiceType;
            item.PricingModelName = dto.PricingModel.ToString();
            item.Quantity = dto.Quantity;
            item.UnitName = dto.Unit ?? "";
            item.UnitPrice = dto.UnitPrice ?? 0m;
            item.Currency = dto.Currency;
            item.VatRatePercent = dto.VatRate;
            item.DiscountType = dto.DiscountType;
            item.DiscountValue = dto.DiscountValue;
            item.BillingCycle = dto.BillingCycle;
            item.DurationPeriods = dto.DurationPeriods;
            item.ActivationMethod = dto.ActivationMethod;
            item.StartDate = dto.StartDate;
            item.DeliveryDate = dto.DeliveryDate;
            item.IsFree = dto.IsFree;
            item.Position = dto.Position;

            // Recomputed here, never taken from the browser.
            item.AgreedPrice = dto.NetTotal;

            item.Config = BuildConfig(dto);
            item.PriceBreakdown = BuildBreakdown(dto);

            // The snapshot records what was agreed. Written once: a position that
            // already carries one keeps it, so re-saving a draft cannot rewrite the
            // terms a customer already reviewed.
            if (item.Snapshot is null)
            {
                item.Snapshot = BuildSnapshot(dto);
                item.SnapshotTakenAt = DateTimeOffset.UtcNow;
            }
        }

        private static JsonDocument BuildConfig(ManualPositionDto dto) =>
            JsonSerializer.SerializeToDocument(new
            {
                scope = dto.Scope,
                deliverables = dto.Deliverables,
                deliveryMethod = dto.DeliveryMethod,
                acceptanceCriteria = dto.AcceptanceCriteria,
                customerResponsibilities = dto.CustomerResponsibilities,
                assumptions = dto.Assumptions,
                exclusions = dto.Exclusions,
                notes = dto.Notes
            }, SnapshotOptions);

        private static JsonDocument BuildBreakdown(ManualPositionDto dto) =>
            JsonSerializer.SerializeToDocument(new
            {
                net = dto.NetTotal,
                vatRate = dto.VatRate ?? 0m,
                vat = dto.VatAmount,
                gross = dto.GrossTotal,
                currency = dto.Currency,
                pricingModel = dto.PricingModel.ToString(),
                quantity = dto.Quantity,
                unitPrice = dto.UnitPrice ?? 0m,
                discountType = dto.DiscountType?.ToString(),
                discountValue = dto.DiscountValue,
                isFree = dto.IsFree,
                calculatedAt = DateTimeOffset.UtcNow
            }, SnapshotOptions);

        private static JsonDocument BuildSnapshot(ManualPositionDto dto) =>
            JsonSerializer.SerializeToDocument(new
            {
                title = dto.Title,
                serviceType = dto.ServiceType,
                description = dto.Description,
                scope = dto.Scope,
                deliverables = dto.Deliverables,
                quantity = dto.Quantity,
                unit = dto.Unit,
                pricingModel = dto.PricingModel.ToString(),
                unitPrice = dto.UnitPrice,
                currency = dto.Currency,
                vatRate = dto.VatRate,
                discountType = dto.DiscountType?.ToString(),
                discountValue = dto.DiscountValue,
                billingCycle = dto.BillingCycle.ToString(),
                durationPeriods = dto.DurationPeriods,
                isFree = dto.IsFree,
                deliveryMethod = dto.DeliveryMethod,
                activationMethod = dto.ActivationMethod.ToString(),
                startDate = dto.StartDate?.ToString("yyyy-MM-dd"),
                deliveryDate = dto.DeliveryDate?.ToString("yyyy-MM-dd"),
                acceptanceCriteria = dto.AcceptanceCriteria,
                customerResponsibilities = dto.CustomerResponsibilities,
                assumptions = dto.Assumptions,
                exclusions = dto.Exclusions,
                notes = dto.Notes,
                netTotal = dto.NetTotal,
                vatAmount = dto.VatAmount,
                grossTotal = dto.GrossTotal
            }, SnapshotOptions);

        private static ManualPositionDto ToDto(ContractItem item)
        {
            var config = item.Config?.RootElement;

            return new ManualPositionDto
            {
                ClientId = item.Id.ToString("n"),
                ContractItemId = item.Id,
                SourceType = item.Source,
                CatalogServiceId = item.ServiceId,
                SourceDraftId = item.SourceDraftId,
                Position = item.Position,
                Title = item.Title,
                ServiceType = item.ServiceTypeLabel,
                Description = item.Description,
                Scope = ReadString(config, "scope"),
                Deliverables = ReadArray(config, "deliverables"),
                Quantity = item.Quantity,
                Unit = item.UnitName,
                PricingModel = Enum.TryParse<PricingModel>(item.PricingModelName, out var pm) ? pm : PricingModel.Fixed,
                UnitPrice = item.UnitPrice,
                Currency = item.Currency ?? "EUR",
                VatRate = item.VatRatePercent,
                DiscountType = item.DiscountType,
                DiscountValue = item.DiscountValue,
                BillingCycle = item.BillingCycle,
                DurationPeriods = item.DurationPeriods,
                IsFree = item.IsFree,
                DeliveryMethod = ReadString(config, "deliveryMethod"),
                ActivationMethod = item.ActivationMethod,
                StartDate = item.StartDate,
                DeliveryDate = item.DeliveryDate,
                AcceptanceCriteria = ReadArray(config, "acceptanceCriteria"),
                CustomerResponsibilities = ReadArray(config, "customerResponsibilities"),
                Assumptions = ReadArray(config, "assumptions"),
                Exclusions = ReadArray(config, "exclusions"),
                Notes = ReadString(config, "notes")
            };
        }

        private static string? ReadString(JsonElement? element, string name) =>
            element is { ValueKind: JsonValueKind.Object } e
            && e.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static List<string> ReadArray(JsonElement? element, string name)
        {
            if (element is not { ValueKind: JsonValueKind.Object } e
                || !e.TryGetProperty(name, out var value)
                || value.ValueKind != JsonValueKind.Array)
            {
                return new List<string>();
            }

            return value.EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString()!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }
    }
}
