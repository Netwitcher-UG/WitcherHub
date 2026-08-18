using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Contracts;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.Services.OpenAI
{
    /// <summary>
    /// Asks the model to structure rough service notes into contract positions,
    /// then verifies the answer before anyone sees it.
    ///
    /// The model is allowed to improve wording and fill in descriptive detail. It
    /// is not allowed to change money, quantities, tax, discounts, currency, dates,
    /// billing cycles or durations: those come from the user and are restored from
    /// the user's own values if the answer differs. A contract is a commercial
    /// document, and a model that silently rounds a price has produced a different
    /// agreement from the one the user intended.
    /// </summary>
    public sealed class AiPositionOrganizer : IAiPositionOrganizer
    {
        /// <summary>Bump when the prompt changes, so drafts record what produced them.</summary>
        public const string CurrentPromptVersion = "positions-v1";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            // The schema asks for enum names ("Monthly", "Fixed"), so the reader has
            // to accept them. Without this every response fails to parse and the
            // organizer looks permanently unavailable.
            Converters = { new JsonStringEnumConverter(allowIntegerValues: true) },
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        private readonly IAiTextGenerator _ai;
        private readonly OpenAIOptions _options;
        private readonly ILogger<AiPositionOrganizer> _logger;

        public AiPositionOrganizer(
            IAiTextGenerator ai,
            IOptions<OpenAIOptions> options,
            ILogger<AiPositionOrganizer> logger)
        {
            _ai = ai;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<OrganizePositionsResult> OrganizeAsync(
            OrganizePositionsRequest request,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (string.IsNullOrWhiteSpace(request.RoughInput) && request.ExistingPositions.Count == 0)
                return OrganizePositionsResult.Failed("Describe the services, or add at least one position first.");

            string raw;
            try
            {
                raw = await _ai.GenerateTextAsync(BuildPrompt(request));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (AiInvocationException ex)
            {
                // The generator already worked out what actually went wrong and
                // whether waiting will help. Flattening that back into "not
                // reachable right now, transient: true" — as this used to — told
                // an owner whose account is out of credit, or whose model name is
                // wrong, to keep pressing the button.
                // Raised to Error when only the owner can clear it: a bad key, an
                // unusable model or an empty account will fail every call from now
                // on, and that deserves to stand out in the platform log rather
                // than blend into the ordinary run of timeouts.
                _logger.Log(
                    ex.NeedsOwnerAction ? LogLevel.Error : LogLevel.Warning,
                    "The position organizer could not use the model: {Kind} ({CorrelationId}).",
                    ex.Kind, ex.CorrelationId);

                return OrganizePositionsResult.Failed(
                    ex.UserMessage + " Your positions are unchanged — you can keep editing them by hand.",
                    ex.IsTransient);
            }
            catch (Exception ex)
            {
                // Anything the generator did not classify.
                _logger.LogWarning(ex, "The position organizer could not reach the model.");

                return OrganizePositionsResult.Failed(
                    "The assistant is not reachable right now. Your positions are unchanged — you can keep editing them by hand.",
                    transient: true);
            }

            if (!TryParsePositions(raw, out var proposed, out var parseError))
            {
                _logger.LogWarning("The position organizer returned unusable JSON: {Error}", parseError);

                return OrganizePositionsResult.Failed(
                    "The assistant returned something unreadable. Your positions are unchanged.",
                    transient: true);
            }

            var (positions, changes, rejected) = Reconcile(request, proposed);

            if (positions.Count == 0)
                return OrganizePositionsResult.Failed("The assistant did not produce any usable positions.");

            if (rejected.Count > 0)
            {
                _logger.LogWarning(
                    "The position organizer attempted to change {Count} commercial value(s); the user's figures were kept.",
                    rejected.Count);
            }

            return new OrganizePositionsResult
            {
                Succeeded = true,
                Positions = positions,
                Changes = changes,
                RejectedChanges = rejected,
                Model = _options.Model,
                PromptVersion = CurrentPromptVersion
            };
        }

        // -------------------------------------------------------------------
        // Prompt
        // -------------------------------------------------------------------

        private static string BuildPrompt(OrganizePositionsRequest request)
        {
            // Only what the task needs. No customer identity, no internal ids.
            var existing = request.ExistingPositions.Select(p => new
            {
                clientId = p.ClientId,
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
                billingCycle = p.BillingCycle.ToString(),
                durationPeriods = p.DurationPeriods,
                isFree = p.IsFree
            });

            return $$"""
                You organise rough notes into structured contract positions for a German agency.

                Return ONLY a JSON array. No prose, no markdown fence. Each element:

                {
                  "clientId": string|null,
                  "sourceType": "Manual"|"Catalog",
                  "catalogServiceId": string|null,
                  "position": number,
                  "title": string,
                  "serviceType": string|null,
                  "description": string|null,
                  "scope": string|null,
                  "deliverables": string[],
                  "quantity": number,
                  "unit": string|null,
                  "pricingModel": "Fixed"|"Unit"|"Tiered"|"Hourly",
                  "unitPrice": number|null,
                  "currency": string,
                  "vatRate": number|null,
                  "discountType": "Percent"|"Amount"|"Fixed"|null,
                  "discountValue": number|null,
                  "billingCycle": "OneTime"|"Monthly"|"Quarterly"|"SemiAnnual"|"Annual",
                  "durationPeriods": number|null,
                  "isFree": boolean,
                  "deliveryMethod": string|null,
                  "activationMethod": "NotApplicable"|"AfterSignature"|"AfterInitialPayment"|"OnSpecifiedDate"|"ManualActivation",
                  "startDate": "yyyy-MM-dd"|null,
                  "deliveryDate": "yyyy-MM-dd"|null,
                  "acceptanceCriteria": string[],
                  "customerResponsibilities": string[],
                  "assumptions": string[],
                  "exclusions": string[],
                  "notes": string|null
                }

                YOU MAY: improve wording, write clear descriptions and scope, propose
                deliverables, acceptance criteria, customer responsibilities,
                assumptions and exclusions that follow from the notes.

                YOU MUST NOT change any of these, for any reason, not even to correct
                what looks like a mistake — copy them exactly as given:
                quantity, unitPrice, currency, vatRate, discountType, discountValue,
                billingCycle, durationPeriods, isFree, startDate, deliveryDate.

                Do not invent services that are not described. Do not add legal or
                payment obligations. Write descriptive text in {{request.Language}}.
                Currency is {{request.Currency}}.

                Positions already entered (their commercial values are final):
                {{JsonSerializer.Serialize(existing)}}

                Additional notes from the user:
                {{request.RoughInput}}
                """;
        }

        // -------------------------------------------------------------------
        // Parsing
        // -------------------------------------------------------------------

        internal static bool TryParsePositions(string raw, out List<ManualPositionDto> positions, out string? error)
        {
            positions = new List<ManualPositionDto>();
            error = null;

            var json = ExtractJsonArray(raw);
            if (json is null)
            {
                error = "no JSON array found in the response";
                return false;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<List<ManualPositionDto>>(json, JsonOptions);

                if (parsed is null)
                {
                    error = "the JSON array deserialised to nothing";
                    return false;
                }

                positions = parsed;
                return true;
            }
            catch (JsonException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Pulls the JSON array out of a response that may be wrapped in a code
        /// fence or padded with commentary.
        /// </summary>
        private static string? ExtractJsonArray(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var text = raw.Trim();

            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                var firstBreak = text.IndexOf('\n');
                if (firstBreak >= 0) text = text[(firstBreak + 1)..];

                var fence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (fence >= 0) text = text[..fence];

                text = text.Trim();
            }

            var start = text.IndexOf('[');
            var end = text.LastIndexOf(']');

            return start >= 0 && end > start ? text[start..(end + 1)] : null;
        }

        // -------------------------------------------------------------------
        // Reconciliation — the guard
        // -------------------------------------------------------------------

        /// <summary>
        /// Matches the proposal against what the user entered, keeps the user's
        /// commercial values, and records every difference.
        /// </summary>
        internal static (List<ManualPositionDto> Positions, List<PositionChange> Changes, List<PositionChange> Rejected)
            Reconcile(OrganizePositionsRequest request, List<ManualPositionDto> proposed)
        {
            var changes = new List<PositionChange>();
            var rejected = new List<PositionChange>();
            var result = new List<ManualPositionDto>();

            var byClientId = request.ExistingPositions
                .Where(p => !string.IsNullOrWhiteSpace(p.ClientId))
                .GroupBy(p => p.ClientId!)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var byTitle = request.ExistingPositions
                .Where(p => !string.IsNullOrWhiteSpace(p.Title))
                .GroupBy(p => p.Title.Trim())
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var nextPosition = 1;

            foreach (var candidate in proposed)
            {
                if (string.IsNullOrWhiteSpace(candidate.Title))
                    continue;

                ManualPositionDto? original = null;

                if (!string.IsNullOrWhiteSpace(candidate.ClientId))
                    byClientId.TryGetValue(candidate.ClientId!, out original);

                if (original is null)
                    byTitle.TryGetValue(candidate.Title.Trim(), out original);

                candidate.Position = nextPosition++;

                if (original is null)
                {
                    // A position the user did not enter. Kept for review, but with
                    // no price attached: the model does not get to price work.
                    candidate.SourceType = ContractItemSource.Manual;
                    candidate.CatalogServiceId = null;
                    candidate.UnitPrice = null;
                    candidate.IsFree = false;
                    candidate.Currency = request.Currency;

                    changes.Add(new PositionChange(
                        candidate.Title, "position", null, candidate.Title, PositionChangeKind.AddedPosition));

                    result.Add(candidate);
                    continue;
                }

                RecordDescriptiveChanges(original, candidate, changes);
                RestoreCommercialValues(original, candidate, rejected);

                candidate.ClientId = original.ClientId;
                candidate.ContractItemId = original.ContractItemId;
                candidate.SourceType = original.SourceType;
                candidate.CatalogServiceId = original.CatalogServiceId;

                result.Add(candidate);
            }

            // Anything the model dropped is kept: silently losing an agreed
            // position would be worse than an untidy list.
            foreach (var missing in request.ExistingPositions)
            {
                if (result.Any(r => string.Equals(r.ClientId, missing.ClientId, StringComparison.OrdinalIgnoreCase)))
                    continue;

                missing.Position = nextPosition++;
                result.Add(missing);
            }

            return (result, changes, rejected);
        }

        private static void RecordDescriptiveChanges(
            ManualPositionDto original, ManualPositionDto candidate, List<PositionChange> changes)
        {
            void Compare(string field, string? before, string? after)
            {
                if (string.Equals(before?.Trim(), after?.Trim(), StringComparison.Ordinal))
                    return;

                if (string.IsNullOrWhiteSpace(after))
                    return;

                changes.Add(new PositionChange(original.Title, field, before, after, PositionChangeKind.Descriptive));
            }

            Compare("Title", original.Title, candidate.Title);
            Compare("Description", original.Description, candidate.Description);
            Compare("Scope", original.Scope, candidate.Scope);
            Compare("ServiceType", original.ServiceType, candidate.ServiceType);
            Compare("DeliveryMethod", original.DeliveryMethod, candidate.DeliveryMethod);
            Compare("Notes", original.Notes, candidate.Notes);
            Compare("Deliverables", Join(original.Deliverables), Join(candidate.Deliverables));
            Compare("AcceptanceCriteria", Join(original.AcceptanceCriteria), Join(candidate.AcceptanceCriteria));
            Compare("CustomerResponsibilities", Join(original.CustomerResponsibilities), Join(candidate.CustomerResponsibilities));
            Compare("Assumptions", Join(original.Assumptions), Join(candidate.Assumptions));
            Compare("Exclusions", Join(original.Exclusions), Join(candidate.Exclusions));
        }

        /// <summary>
        /// Overwrites every commercial field on the candidate with the user's value,
        /// recording any that differed.
        /// </summary>
        private static void RestoreCommercialValues(
            ManualPositionDto original, ManualPositionDto candidate, List<PositionChange> rejected)
        {
            void Guard<T>(string field, T mine, T theirs, Action restore)
            {
                if (!EqualityComparer<T>.Default.Equals(mine, theirs))
                {
                    rejected.Add(new PositionChange(
                        original.Title, field, Format(mine), Format(theirs), PositionChangeKind.RejectedCommercial));
                }

                restore();
            }

            Guard("Quantity", original.Quantity, candidate.Quantity, () => candidate.Quantity = original.Quantity);
            Guard("UnitPrice", original.UnitPrice, candidate.UnitPrice, () => candidate.UnitPrice = original.UnitPrice);
            Guard("Currency", original.Currency, candidate.Currency, () => candidate.Currency = original.Currency);
            Guard("VatRate", original.VatRate, candidate.VatRate, () => candidate.VatRate = original.VatRate);
            Guard("DiscountType", original.DiscountType, candidate.DiscountType, () => candidate.DiscountType = original.DiscountType);
            Guard("DiscountValue", original.DiscountValue, candidate.DiscountValue, () => candidate.DiscountValue = original.DiscountValue);
            Guard("BillingCycle", original.BillingCycle, candidate.BillingCycle, () => candidate.BillingCycle = original.BillingCycle);
            Guard("DurationPeriods", original.DurationPeriods, candidate.DurationPeriods, () => candidate.DurationPeriods = original.DurationPeriods);
            Guard("IsFree", original.IsFree, candidate.IsFree, () => candidate.IsFree = original.IsFree);
            Guard("StartDate", original.StartDate, candidate.StartDate, () => candidate.StartDate = original.StartDate);
            Guard("DeliveryDate", original.DeliveryDate, candidate.DeliveryDate, () => candidate.DeliveryDate = original.DeliveryDate);
            Guard("Unit", original.Unit, candidate.Unit, () => candidate.Unit = original.Unit);
            Guard("PricingModel", original.PricingModel, candidate.PricingModel, () => candidate.PricingModel = original.PricingModel);
        }

        private static string Join(IEnumerable<string> values) => string.Join(" | ", values);

        private static string? Format<T>(T value) => value switch
        {
            null => null,
            decimal d => d.ToString(CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }
}
