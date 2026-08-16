using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Services.Contracts;
using WitcherHub.Domain.Commercial;

namespace WitcherHub.Infrastructure.Services.OpenAI
{
    /// <summary>
    /// Reads an arbitrary commercial document into structured terms.
    ///
    /// The pipeline, in order: understand the document, list what was recognised,
    /// classify each of those, keep only the charges, structure them, validate,
    /// and hand the result to the financial engine. Paragraphs are never turned
    /// straight into stored positions — what a paragraph means has to be settled
    /// before anything is created from it.
    ///
    /// The prompt describes a domain, not a document. It contains no service
    /// name, no customer, no industry, no price and no example from any contract
    /// this system has processed, because a prompt written around one agreement
    /// reads the next one as if it were that agreement. Everything specific
    /// arrives at runtime as the source text.
    ///
    /// The model is asked for meaning and never for arithmetic. It says what
    /// recurs, what is committed, what is optional; the application multiplies.
    /// </summary>
    public sealed class SemanticContractAnalyzer : ISemanticContractAnalyzer
    {
        public const string CurrentPromptVersion = "semantic-extraction-v1";

        /// <summary>
        /// A ceiling on how much document is sent. Not a business rule — a limit
        /// on cost and latency, applied the same way to every document.
        /// </summary>
        private const int MaxCharacters = 80_000;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            Converters = { new JsonStringEnumConverter(allowIntegerValues: true) }
        };

        private readonly IAiTextGenerator _ai;
        private readonly OpenAIOptions _options;
        private readonly ILogger<SemanticContractAnalyzer> _logger;

        public SemanticContractAnalyzer(
            IAiTextGenerator ai,
            IOptions<OpenAIOptions> options,
            ILogger<SemanticContractAnalyzer> logger)
        {
            _ai = ai;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<SemanticAnalysisResult> AnalyzeAsync(
            string documentText,
            SemanticAnalysisOptions? options = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(documentText))
                return SemanticAnalysisResult.Failed("There is no document text to analyse.");

            options ??= new SemanticAnalysisOptions();

            var text = documentText.Length > MaxCharacters ? documentText[..MaxCharacters] : documentText;

            string raw;
            try
            {
                raw = await _ai.GenerateTextAsync(BuildPrompt(text, options));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (AiInvocationException ex)
            {
                return SemanticAnalysisResult.Failed(ex.UserMessage, ex.IsTransient, ex.CorrelationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Semantic contract analysis failed for an unclassified reason.");

                return SemanticAnalysisResult.Failed(
                    "The document could not be analysed. It is saved and unchanged.", transient: true);
            }

            if (!TryParse(raw, out var extraction, out var parseError))
            {
                _logger.LogWarning("Semantic analysis returned unusable JSON: {Error}", parseError);

                return SemanticAnalysisResult.Failed(
                    "The analysis came back in a form that could not be read. Your document is saved and unchanged.",
                    transient: true);
            }

            // Only the concepts classified as charges become terms. Everything
            // else is kept as context and stays out of the money.
            var billable = extraction!.Terms ?? new List<ProposedTermDto>();

            var mapped = ProposedTermMapper.ToDomain(billable, options.FallbackCurrency);

            // Partial results survive: a term with one unusable field is kept and
            // flagged, not discarded along with everything that was right.
            var validated = TermValidator.Validate(mapped);

            var financials = ContractFinancialEngine.Calculate(
                validated.Terms, options.FallbackCurrency, options.ContractMonths);

            _logger.LogInformation(
                "Semantic analysis produced {Concepts} concept(s), {Terms} billable term(s), " +
                "{Issues} issue(s), {Unresolved} unresolved amount(s). Committed net {Committed}.",
                extraction.Concepts?.Count ?? 0, validated.Terms.Count,
                validated.Issues.Count, financials.Unresolved.Count, financials.CommittedNet);

            return new SemanticAnalysisResult
            {
                Succeeded = true,
                Extraction = extraction,
                Terms = validated.Terms,
                Issues = validated.Issues,
                DiscardedReasons = validated.DiscardedReasons,
                Financials = financials,
                Model = _options.Model,
                PromptVersion = CurrentPromptVersion
            };
        }

        // -------------------------------------------------------------------

        private static string BuildPrompt(string document, SemanticAnalysisOptions options)
        {
            var conceptKinds = string.Join(" | ", Enum.GetNames<CommercialConceptKind>());
            var pricingKinds = string.Join(" | ", Enum.GetNames<PricingModelKind>());
            var commitments = string.Join(" | ", Enum.GetNames<Commitment>());

            return $$"""
                You read commercial documents of any kind, in any language, from any
                industry, and report what they mean. You do not write documents and
                you do not perform arithmetic.

                Return ONLY a JSON object. No prose, no markdown fence.

                WORK IN THIS ORDER.

                1. Understand the document as a whole before extracting anything.
                2. List every passage that carries commercial or contractual meaning.
                3. Classify each one. Most are not charges.
                4. Only then, build structured terms from the ones that are charges.

                {
                  "detectedLanguage": string|null,
                  "documentType": string|null,
                  "documentTitle": string|null,
                  "purpose": string|null,

                  "concepts": [ {
                      "key": string,
                      "kind": {{conceptKinds}},
                      "summary": string,
                      "sourceSnippet": string,
                      "confidence": number,
                      "reasoning": string|null,
                      "isAmbiguous": boolean,
                      "ambiguity": string|null,
                      "relatedKeys": string[],
                      "relationKind": "phasesOfOneCharge"|"separateCharges"|"alternatives"|"optionalExtras"|"detailOf"|null
                  } ],

                  "terms": [ {
                      "key": string,
                      "name": string,
                      "description": string|null,
                      "category": string|null,
                      "pricingModel": {{pricingKinds}},
                      "quantity": number|null,
                      "quantityUnit": string|null,
                      "unitRate": number|null,
                      "fixedAmount": number|null,
                      "currency": string|null,
                      "billingRecurrence": string|null,
                      "deliveryRecurrence": string|null,
                      "paymentSchedule": string|null,
                      "startDate": string|null,
                      "endDate": string|null,
                      "durationCount": number|null,
                      "durationUnit": string|null,
                      "minimumCommitment": number|null,
                      "cap": number|null,
                      "discountPercent": number|null,
                      "discountAmount": number|null,
                      "taxRatePercent": number|null,
                      "taxTreatment": string|null,
                      "isMandatory": boolean|null,
                      "conditions": string|null,
                      "notes": string|null,
                      "commitment": {{commitments}},
                      "phases": [ {
                          "label": string|null, "sequence": number,
                          "startDate": string|null, "endDate": string|null,
                          "startCondition": string|null, "endCondition": string|null,
                          "durationCount": number|null, "durationUnit": string|null,
                          "pricingModel": string|null, "rate": number|null,
                          "currency": string|null, "quantity": number|null,
                          "quantityUnit": string|null, "billingRecurrence": string|null,
                          "discountPercent": number|null, "discountAmount": number|null,
                          "conditions": string|null, "sourceSnippet": string|null,
                          "confidence": number
                      } ],
                      "sourceSnippet": string|null,
                      "confidence": number,
                      "reasoning": string|null,
                      "isAmbiguous": boolean,
                      "ambiguity": string|null,
                      "openQuestions": string[]
                  } ],

                  "detectedParties": { "<field>": string|null },
                  "detectedContractTerms": { "<field>": string|null },
                  "openQuestions": string[],
                  "warnings": string[]
                }

                RULES.

                NEVER INVENT. If the document does not say, use null. Not a typical
                value, not a market rate, not a standard tax rate, not a date you
                worked out, not a quantity you assumed. A missing price stays
                missing. This matters more than completeness.

                ONLY CHARGES BECOME TERMS. A payment condition, a deadline, a scope
                limitation, a service description and a legal clause all belong in
                "concepts" and none of them is a term. If it is not something that
                will be billed, it is not a term.

                COMMITMENT IS A JUDGEMENT, AND IT IS THE ONE THAT MATTERS MOST.
                Ask: is the customer certain to owe this? An agreed fee for an
                agreed period is Committed. A rate per hour with no agreed hours is
                Variable — the rate is definite, the amount is not. A figure given
                as an expectation is Estimated. Something charged only if taken up
                is Optional. Something charged only if a stated event occurs is
                Conditional. If you cannot tell, say Unknown. Do not mark something
                Committed because a number is present.

                DISTINGUISH THREE DIFFERENT FREQUENCIES. The unit a quantity is
                counted in, how often it is delivered, and how often it is billed
                are three separate things and are frequently different. Report each
                where the document states it, in the document's own words. Do not
                copy one into another.

                PRICING THAT CHANGES OVER TIME IS PHASES, NOT SEPARATE TERMS. If
                one charge is priced one way for a period and differently
                afterwards, that is one term with phases. A phase boundary may be a
                date, a number of periods, a project stage, a quantity, or a
                condition in words — record whichever the document uses, and leave
                the others null. Do not assume phases are months.

                RELATIONSHIPS ARE SEMANTIC. Facts about one charge are often
                scattered: a description in one clause, a price in another, a term
                in a third. Join them into one term. Where several amounts appear
                and you cannot tell whether they are phases of one charge, separate
                charges, alternatives, or optional extras — say so in "ambiguity"
                and add an open question. Do not pick one silently.

                NO ARITHMETIC. Do not total anything, do not multiply a rate by a
                quantity, do not compute tax, do not annualise a monthly figure.
                Report the components. The application calculates.

                QUOTE YOUR SOURCE. Every term and every concept carries the passage
                it came from, and a confidence between 0 and 1.

                STRUCTURE IS NOT MEANING. The document may be a formal contract, an
                email, notes, OCR output, bullet points, a table flattened into
                text, or several languages mixed together. Headings may be absent.
                A price may appear before or after what it is for. Read for meaning.

                {{(string.IsNullOrWhiteSpace(options.LanguageHint)
                    ? ""
                    : $"The document is expected to be in {options.LanguageHint}, but verify rather than assume.")}}

                Document:
                <<<DOCUMENT
                {{document}}
                DOCUMENT
                """;
        }

        internal static bool TryParse(string raw, out SemanticExtractionDto? extraction, out string? error)
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
                extraction = JsonSerializer.Deserialize<SemanticExtractionDto>(json, JsonOptions);

                if (extraction is null)
                {
                    error = "the JSON object deserialised to nothing";
                    return false;
                }

                extraction.Concepts ??= new List<DetectedConceptDto>();
                extraction.Terms ??= new List<ProposedTermDto>();
                extraction.OpenQuestions ??= new List<string>();
                extraction.Warnings ??= new List<string>();

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
    }
}
