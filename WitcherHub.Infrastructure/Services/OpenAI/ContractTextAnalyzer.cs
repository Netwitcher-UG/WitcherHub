using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Contracts;

namespace WitcherHub.Infrastructure.Services.OpenAI
{
    /// <summary>
    /// Reads a supplied contract into structured values, and refuses to invent
    /// any of them.
    ///
    /// The model is told to leave a field out when the document does not say, and
    /// everything it does return is checked here before anyone sees it: values
    /// outside the schema are dropped, confidences are clamped, and every
    /// commercial value is marked as needing confirmation regardless of how sure
    /// the model claimed to be. A price nobody agreed is worse than no price, so
    /// a document with no figure produces PriceMissing and a warning rather than
    /// a number.
    /// </summary>
    public sealed class ContractTextAnalyzer : IContractTextAnalyzer
    {
        public const string CurrentPromptVersion = "contract-extract-v1";

        /// <summary>
        /// How much of the document is sent. Long enough for the contracts this
        /// is used on, short enough that a pasted book cannot run up a bill.
        /// </summary>
        private const int MaxCharacters = 60_000;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        private readonly IAiTextGenerator _ai;
        private readonly OpenAIOptions _options;
        private readonly ILogger<ContractTextAnalyzer> _logger;

        public ContractTextAnalyzer(
            IAiTextGenerator ai,
            IOptions<OpenAIOptions> options,
            ILogger<ContractTextAnalyzer> logger)
        {
            _ai = ai;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<ContractAnalysisResult> AnalyzeAsync(
            string documentText, string? languageHint = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(documentText))
                return ContractAnalysisResult.Failed("There is no contract text to analyse.");

            var text = documentText.Length > MaxCharacters
                ? documentText[..MaxCharacters]
                : documentText;

            string raw;
            try
            {
                raw = await _ai.GenerateTextAsync(BuildPrompt(text, languageHint));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (AiInvocationException ex)
            {
                // The supplied text is already stored. Analysis is an optional
                // step over it, so failing here costs nothing but the analysis.
                return ContractAnalysisResult.Failed(ex.UserMessage, ex.IsTransient, ex.CorrelationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Contract analysis failed for an unclassified reason.");
                return ContractAnalysisResult.Failed(
                    "The contract could not be analysed. Your contract text is saved and unchanged.",
                    transient: true);
            }

            if (!TryParse(raw, out var extraction, out var parseError))
            {
                _logger.LogWarning("Contract analysis returned unusable JSON: {Error}", parseError);

                return ContractAnalysisResult.Failed(
                    "The analysis came back in a form that could not be read. Your contract text is saved and unchanged.",
                    transient: true);
            }

            Sanitise(extraction!);

            return new ContractAnalysisResult
            {
                Succeeded = true,
                Extraction = extraction,
                Model = _options.Model
            };
        }

        // -------------------------------------------------------------------

        private static string BuildPrompt(string document, string? languageHint)
        {
            return $$"""
                You read a supplied contract and report exactly what it says. You are
                not writing a contract and not improving one.

                Return ONLY a JSON object. No prose, no markdown fence.

                Every field below is one of two shapes.

                A value field:
                  { "value": string|null, "sourceText": string|null, "confidence": number }
                - "value": exactly what the document says, in the document's own words
                  or normalised where obvious (dates as yyyy-MM-dd, amounts as digits).
                - "sourceText": the sentence or clause you read it from, quoted.
                - "confidence": 0 to 1.

                THE RULE THAT MATTERS MOST: if the document does not say, set "value"
                to null. Never supply a plausible value, a typical value, a market
                rate, a standard VAT rate, or a date you worked out. A missing price
                must come back as null. An absent clause must come back as null.

                Fields (all value fields unless noted):
                  title, contractType, purpose, language,
                  providerName, providerAddress, providerRepresentative,
                  customerName, customerAddress, customerRepresentative,
                  effectiveDate, startDate, endDate, duration, renewalRules,
                  terminationNotice,
                  totalPrice, currency, vatRate, vatTreatment, discounts,
                  billingCycle, paymentSchedule, paymentDueDates, deposit,
                  recurringCharges,
                  customerResponsibilities, providerResponsibilities,
                  acceptanceCriteria, revisions, assumptions, exclusions,
                  warranty, liability, confidentiality, intellectualProperty,
                  signatureParties, otherTerms

                Plus:
                  "positions": array, one entry per itemised service or line item the
                  document actually lists. Each:
                  {
                    "title": string, "description": string|null,
                    "quantity": number|null, "unit": string|null,
                    "unitPrice": number|null, "lineTotal": number|null,
                    "currency": string|null, "vatRatePercent": number|null,
                    "billingCycle": string|null, "sourceText": string|null,
                    "confidence": number
                  }
                  Return [] when the document names one overall price without
                  itemising services. Do not split a single total into invented
                  line items.

                  "priceMissing": true when the document names no price at all.
                  "warnings": array of short strings for anything a person must
                  check — a missing price, totals that disagree, unclear dates,
                  party details that look like a placeholder.

                {{(string.IsNullOrWhiteSpace(languageHint) ? "" : $"The document is expected to be in {languageHint}.")}}

                Contract document:
                <<<DOCUMENT
                {{document}}
                DOCUMENT
                """;
        }

        // -------------------------------------------------------------------

        internal static bool TryParse(string raw, out ContractExtractionDto? extraction, out string? error)
        {
            extraction = null;
            error = null;

            var json = ExtractJsonObject(raw);
            if (json is null)
            {
                error = "no JSON object found in the response";
                return false;
            }

            try
            {
                extraction = JsonSerializer.Deserialize<ContractExtractionDto>(json, JsonOptions);

                if (extraction is null)
                {
                    error = "the JSON object deserialised to nothing";
                    return false;
                }

                return true;
            }
            catch (JsonException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string? ExtractJsonObject(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var text = raw.Trim();

            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                var firstBreak = text.IndexOf('\n');
                if (firstBreak >= 0) text = text[(firstBreak + 1)..];

                var fence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (fence >= 0) text = text[..fence];

                text = text.Trim();
            }

            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');

            return start >= 0 && end > start ? text[start..(end + 1)] : null;
        }

        /// <summary>
        /// Brings the answer back inside the rules regardless of what came back.
        ///
        /// Confidence is clamped, every commercial field is flagged for
        /// confirmation whatever the model claimed, and a missing price is
        /// recorded as missing with a warning rather than left to be noticed.
        /// </summary>
        internal static void Sanitise(ContractExtractionDto e)
        {
            foreach (var field in AllValues(e))
            {
                field.Confidence = Math.Clamp(field.Confidence, 0d, 1d);

                if (string.IsNullOrWhiteSpace(field.Value))
                    field.Value = null;

                // Nothing arrives confirmed. Confirmation is an act by a person.
                field.Confirmed = false;
                field.NeedsConfirmation = true;
            }

            // Commercial fields stay flagged even at high confidence: the cost of
            // an unchecked wrong price is a contract for the wrong money.
            foreach (var field in Commercial(e))
                field.NeedsConfirmation = true;

            e.Positions ??= new List<ExtractedPositionDto>();
            e.Warnings ??= new List<string>();

            foreach (var p in e.Positions)
            {
                p.Confidence = Math.Clamp(p.Confidence, 0d, 1d);
                p.Accepted = false;      // never pre-ticked

                // A position with no title is not reviewable; drop the title only
                // so the row is visible and can be corrected, never the figures.
                if (string.IsNullOrWhiteSpace(p.Title))
                    p.Title = "(untitled position)";
            }

            var namesNoPrice =
                !e.TotalPrice.HasValue &&
                e.Positions.All(p => p.UnitPrice is null && p.LineTotal is null);

            if (namesNoPrice)
            {
                e.PriceMissing = true;

                if (!e.Warnings.Any(w => w.Contains("price", StringComparison.OrdinalIgnoreCase)))
                {
                    e.Warnings.Add(
                        "This contract names no price. Nothing has been filled in. Confirm that it is " +
                        "deliberately without a price before sending it.");
                }
            }
            else
            {
                e.PriceMissing = false;
            }

            // Two totals that disagree is the kind of thing that gets signed and
            // argued about later, so it is surfaced rather than reconciled.
            if (e.TotalPrice.HasValue && e.Positions.Count > 0)
            {
                var lineSum = e.Positions.Sum(p => p.LineTotal ?? (p.UnitPrice ?? 0m) * (p.Quantity ?? 1m));

                if (TryParseAmount(e.TotalPrice.Value, out var stated) &&
                    lineSum > 0m &&
                    Math.Abs(stated - lineSum) > 0.01m)
                {
                    e.Warnings.Add(
                        $"The stated total ({stated.ToString(CultureInfo.InvariantCulture)}) does not match the sum of " +
                        $"the listed items ({lineSum.ToString(CultureInfo.InvariantCulture)}). Neither has been changed.");
                }
            }
        }

        internal static bool TryParseAmount(string? value, out decimal amount)
        {
            amount = 0m;
            if (string.IsNullOrWhiteSpace(value)) return false;

            // Keep digits, separators and sign; drop currency symbols and words.
            var cleaned = new string(value.Where(c => char.IsDigit(c) || c is '.' or ',' or '-').ToArray());
            if (cleaned.Length == 0) return false;

            // German notation reaches this from German contracts: 1.234,56.
            if (cleaned.Contains(',') && cleaned.LastIndexOf(',') > cleaned.LastIndexOf('.'))
                cleaned = cleaned.Replace(".", "").Replace(',', '.');
            else
                cleaned = cleaned.Replace(",", "");

            return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
        }

        private static IEnumerable<ExtractedValue> AllValues(ContractExtractionDto e)
        {
            foreach (var property in typeof(ContractExtractionDto).GetProperties())
            {
                if (property.PropertyType != typeof(ExtractedValue)) continue;

                if (property.GetValue(e) is ExtractedValue value)
                {
                    yield return value;
                }
                else
                {
                    // A field the analyser omitted entirely still has to exist, so
                    // the review screen can show it as "not stated".
                    var placeholder = ExtractedValue.Empty;
                    property.SetValue(e, placeholder);
                    yield return placeholder;
                }
            }
        }

        private static IEnumerable<ExtractedValue> Commercial(ContractExtractionDto e)
        {
            yield return e.TotalPrice;
            yield return e.Currency;
            yield return e.VatRate;
            yield return e.VatTreatment;
            yield return e.Discounts;
            yield return e.BillingCycle;
            yield return e.PaymentSchedule;
            yield return e.PaymentDueDates;
            yield return e.Deposit;
            yield return e.RecurringCharges;
            yield return e.EffectiveDate;
            yield return e.StartDate;
            yield return e.EndDate;
            yield return e.Duration;
        }
    }
}
