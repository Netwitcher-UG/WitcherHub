using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Services.Contracts.Language;
using WitcherHub.Infrastructure.Services.OpenAI;

namespace WitcherHub.Infrastructure.Services.Contracts.Language
{
    /// <summary>
    /// Translates contract wording into German with the assistant, and trusts
    /// none of the answer.
    ///
    /// The call itself is the small part. What this is mostly made of is the
    /// checking either side of it: masking the values that must not change,
    /// splitting work into batches that fit, refusing an answer that renamed a
    /// field or lost a figure, and failing the whole normalization when any
    /// batch fails rather than returning most of a contract.
    ///
    /// It never falls back to untranslated text. A contract that quietly went out
    /// in Arabic because the translation timed out is the outcome this exists to
    /// prevent, so a failure here stops the contract and says why.
    /// </summary>
    public sealed class OpenAiContractLanguageNormalizer : IContractLanguageNormalizer
    {
        private readonly IAiTextGenerator _ai;
        private readonly OpenAIOptions _options;
        private readonly ILogger<OpenAiContractLanguageNormalizer> _logger;

        public OpenAiContractLanguageNormalizer(
            IAiTextGenerator ai,
            IOptions<OpenAIOptions> options,
            ILogger<OpenAiContractLanguageNormalizer> logger)
        {
            _ai = ai;
            _options = options.Value;
            _logger = logger;
        }

        private static readonly JsonSerializerOptions Read = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        public async Task<ContractNormalizationResult> NormalizeAsync(
            ContractNormalizationRequest request, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var batches = Batch(request.Fields).ToList();

            var provenance = new NormalizationProvenance(
                ContractTranslationPrompt.Version,
                ContractTranslationPrompt.SchemaVersion,
                _options.Model,
                DateTimeOffset.UtcNow,
                request.Fields.Count,
                batches.Count);

            if (request.Fields.Count == 0)
            {
                return new ContractNormalizationResult { Succeeded = true, Provenance = provenance };
            }

            var vault = new ProtectedValueVault();

            foreach (var value in request.ProtectedValues)
                vault.Protect(value.Value, value.Kind);

            var text = new Dictionary<string, string>(StringComparer.Ordinal);
            var languages = new Dictionary<string, string>(StringComparer.Ordinal);
            var blocking = new List<string>();
            var warnings = new List<string>();

            var batchNumber = 0;

            foreach (var batch in batches)
            {
                batchNumber++;

                // Every batch must succeed. A contract assembled from the three
                // batches that worked is a contract with a hole in it that reads
                // like a finished document.
                var outcome = await TranslateBatchAsync(
                    batch, vault, request.Terminology, batchNumber, batches.Count, ct);

                if (!outcome.Succeeded)
                    return ContractNormalizationResult.Failed(outcome.FailureReason!, provenance);

                foreach (var (id, value) in outcome.Text) text[id] = value;
                foreach (var (id, value) in outcome.Languages) languages[id] = value;

                blocking.AddRange(outcome.Blocking);
                warnings.AddRange(outcome.Warnings);
            }

            _logger.LogInformation(
                "Normalized {Fields} contract field(s) to German in {Batches} batch(es) using {Model}. " +
                "Prompt {Prompt}, schema {Schema}. {Blocking} blocking, {Warnings} warning(s).",
                request.Fields.Count, batches.Count, _options.Model,
                ContractTranslationPrompt.Version, ContractTranslationPrompt.SchemaVersion,
                blocking.Count, warnings.Count);

            return new ContractNormalizationResult
            {
                Succeeded = true,
                Text = text,
                DetectedLanguages = languages,
                BlockingIssues = blocking.Distinct(StringComparer.Ordinal).ToList(),
                Warnings = warnings.Distinct(StringComparer.Ordinal).ToList(),
                Provenance = provenance
            };
        }

        // ================================================================ one batch

        private sealed record BatchOutcome
        {
            public required bool Succeeded { get; init; }
            public string? FailureReason { get; init; }
            public Dictionary<string, string> Text { get; init; } = new(StringComparer.Ordinal);
            public Dictionary<string, string> Languages { get; init; } = new(StringComparer.Ordinal);
            public List<string> Blocking { get; init; } = [];
            public List<string> Warnings { get; init; } = [];

            public static BatchOutcome Failed(string reason) =>
                new() { Succeeded = false, FailureReason = reason };
        }

        private async Task<BatchOutcome> TranslateBatchAsync(
            IReadOnlyList<TranslatableField> batch,
            ProtectedValueVault vault,
            ContractTerminology terminology,
            int number,
            int total,
            CancellationToken ct)
        {
            // The values go out of the text before the text goes out.
            var masked = batch
                .Select(f => f with { SourceText = vault.Mask(f.SourceText) })
                .ToList();

            var maskedById = masked.ToDictionary(f => f.FieldId, f => f.SourceText, StringComparer.Ordinal);

            var prompt = ContractTranslationPrompt.BuildUserPrompt(
                masked, vault.Values, terminology);

            AiCompletion completion;

            try
            {
                completion = await _ai.CompleteAsync(
                    new AiRequest(prompt)
                    {
                        SystemInstruction = ContractTranslationPrompt.System
                                            + "\n\nAntwortschema:\n"
                                            + ContractTranslationPrompt.JsonSchema,
                        MaxOutputTokens = 16000,
                        Purpose = $"contract.translate[{number}/{total}]"
                    },
                    ct);
            }
            catch (AiInvocationException ex)
            {
                // The user-facing sentence, not the provider's. The reference ties
                // it to the log line that carries the technical detail.
                _logger.LogWarning(
                    "Contract translation batch {Number}/{Total} failed: {Kind}. Reference {Reference}.",
                    number, total, ex.Kind, ex.CorrelationId);

                return BatchOutcome.Failed(
                    $"Die Übersetzung konnte nicht abgeschlossen werden. {ex.UserMessage}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }

            if (completion.FinishReason == AiFinishReason.Length)
            {
                // A cut-off answer parses as far as it got. For a translation
                // that means a contract whose last clauses are missing, which
                // nothing downstream would notice.
                return BatchOutcome.Failed(
                    "Die Antwort der Übersetzung wurde abgeschnitten und ist unvollständig.");
            }

            ContractNormalizationAnswer? answer;

            try
            {
                answer = JsonSerializer.Deserialize<ContractNormalizationAnswer>(Unfence(completion.Text), Read);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("Contract translation returned unusable JSON: {Detail}", ex.Message);
                return BatchOutcome.Failed("Die Übersetzung hat kein verwertbares JSON geliefert.");
            }

            if (answer is null)
                return BatchOutcome.Failed("Die Übersetzung hat kein verwertbares Ergebnis geliefert.");

            return Verify(batch, maskedById, vault, answer);
        }

        // ============================================================ verification

        /// <summary>
        /// Everything checked before a translated field is allowed into a
        /// contract. None of it asks the model whether it succeeded.
        /// </summary>
        private static BatchOutcome Verify(
            IReadOnlyList<TranslatableField> batch,
            IReadOnlyDictionary<string, string> maskedById,
            ProtectedValueVault vault,
            ContractNormalizationAnswer answer)
        {
            var outcome = new BatchOutcome { Succeeded = true };

            if (!string.Equals(answer.TargetLanguage?.Trim(), "de", StringComparison.OrdinalIgnoreCase))
                return BatchOutcome.Failed("Die Übersetzung hat Deutsch nicht als Zielsprache bestätigt.");

            var returned = answer.TranslatedFields ?? [];

            // 2 and 3: exactly the fields that were asked for, each once.
            var duplicates = returned
                .GroupBy(f => f.FieldId, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicates.Count > 0)
            {
                return BatchOutcome.Failed(
                    "Die Übersetzung enthält mehrfach dieselben Felder: " + string.Join(", ", duplicates) + ".");
            }

            var byId = returned.ToDictionary(f => f.FieldId, StringComparer.Ordinal);

            var missing = batch.Where(f => !byId.ContainsKey(f.FieldId)).Select(f => f.FieldId).ToList();

            if (missing.Count > 0)
            {
                return BatchOutcome.Failed(
                    "Die Übersetzung hat folgende Felder nicht zurückgegeben: " + string.Join(", ", missing) + ".");
            }

            var expected = batch.Select(f => f.FieldId).ToHashSet(StringComparer.Ordinal);
            var unknown = byId.Keys.Where(id => !expected.Contains(id)).ToList();

            if (unknown.Count > 0)
            {
                // A model inventing field ids has stopped following the schema,
                // and a plan that is partly salvaged is a contract nobody can
                // account for.
                return BatchOutcome.Failed(
                    "Die Übersetzung hat unbekannte Felder zurückgegeben: " + string.Join(", ", unknown) + ".");
            }

            foreach (var field in batch)
            {
                var translated = byId[field.FieldId];
                var masked = maskedById[field.FieldId];

                var problems = TranslationGuard.Inspect(field, masked, translated.TranslatedText);

                if (problems.Count > 0)
                {
                    outcome.Blocking.AddRange(problems);

                    // The source is kept so the reviewer sees what was meant. It
                    // is not silently used as the contract text: the blocking
                    // issue stops the contract either way.
                    outcome.Text[field.FieldId] = vault.Restore(masked);
                    outcome.Languages[field.FieldId] = translated.DetectedSourceLanguage;
                    continue;
                }

                if (translated.Status == TranslationStatus.NeedsReview)
                {
                    outcome.Blocking.Add(
                        $"{field.FieldId}: Die Übersetzung wurde als prüfbedürftig gekennzeichnet.");
                }

                outcome.Text[field.FieldId] = vault.Restore(translated.TranslatedText).Trim();
                outcome.Languages[field.FieldId] = translated.DetectedSourceLanguage;
            }

            foreach (var flag in answer.ReviewFlags ?? [])
            {
                if (flag.Level == ReviewSeverity.Blocking) outcome.Blocking.Add(flag.ToString());
                else outcome.Warnings.Add(flag.ToString());
            }

            // 13: the model's own verdict is honoured when it is negative and
            // ignored when it is positive, because a positive one is exactly
            // what a bad translation also reports.
            if (!answer.CanUseForContract)
            {
                outcome.Blocking.Add(
                    "Die Übersetzung hat das Ergebnis nicht als vertragstauglich eingestuft.");
            }

            return outcome;
        }

        // ================================================================ batching

        /// <summary>
        /// Splits the work into calls that fit, at field boundaries only.
        ///
        /// Never inside a field: half a sentence translated out of context is a
        /// sentence that means something else, and the halves would have to be
        /// glued back together by something that cannot read either of them.
        /// A single field too long for one call is a translation this refuses
        /// rather than cuts.
        /// </summary>
        internal static IEnumerable<IReadOnlyList<TranslatableField>> Batch(
            IReadOnlyList<TranslatableField> fields)
        {
            var batch = new List<TranslatableField>();
            var characters = 0;

            foreach (var field in fields)
            {
                var length = field.SourceText?.Length ?? 0;

                var wouldOverflow =
                    batch.Count >= ContractTranslationPrompt.MaxFieldsPerBatch ||
                    (batch.Count > 0 &&
                     characters + length > ContractTranslationPrompt.MaxTotalCharactersPerBatch);

                if (wouldOverflow)
                {
                    yield return batch;
                    batch = [];
                    characters = 0;
                }

                batch.Add(field);
                characters += length;
            }

            if (batch.Count > 0) yield return batch;
        }

        /// <summary>
        /// The answer without the code fence the model adds despite being told
        /// not to.
        /// </summary>
        private static string Unfence(string? raw)
        {
            var text = (raw ?? "").Trim();

            if (!text.StartsWith("```", StringComparison.Ordinal)) return text;

            var firstBreak = text.IndexOf('\n');
            if (firstBreak > 0) text = text[(firstBreak + 1)..];

            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0) text = text[..lastFence];

            return text.Trim();
        }
    }
}
