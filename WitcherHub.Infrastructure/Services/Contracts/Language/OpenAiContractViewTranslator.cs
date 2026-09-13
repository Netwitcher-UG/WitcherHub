using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Services.Contracts.Language;

namespace WitcherHub.Infrastructure.Services.Contracts.Language
{
    /// <summary>
    /// Puts an approved German contract into the language the customer chose.
    ///
    /// The structure never reaches the model. The document is split in code into
    /// runs of words — headings without their numbers, list items without their
    /// bullets, table cells that contain no figure — and only those are sent.
    /// What comes back is checked segment by segment and dropped into the
    /// original document, so the numbering, the price table and every amount are
    /// the same objects they were before the call.
    ///
    /// A translation that cannot be verified is refused rather than shown. The
    /// German is always there to fall back to, and a customer reading half a
    /// translation has no way to know which half.
    /// </summary>
    public sealed class OpenAiContractViewTranslator : IContractViewTranslator
    {
        private readonly IAiTextGenerator _ai;
        private readonly ILogger<OpenAiContractViewTranslator> _logger;

        public OpenAiContractViewTranslator(
            IAiTextGenerator ai, ILogger<OpenAiContractViewTranslator> logger)
        {
            _ai = ai;
            _logger = logger;
        }

        private sealed class Answer
        {
            [JsonPropertyName("segments")]
            public List<Segment> Segments { get; set; } = [];

            [JsonPropertyName("reviewFlags")]
            public List<Flag> ReviewFlags { get; set; } = [];
        }

        private sealed class Segment
        {
            [JsonPropertyName("id")] public string Id { get; set; } = "";
            [JsonPropertyName("text")] public string Text { get; set; } = "";
        }

        private sealed class Flag
        {
            [JsonPropertyName("id")] public string Id { get; set; } = "";
            [JsonPropertyName("issue")] public string Issue { get; set; } = "";

            public override string ToString() => $"{Id}: {Issue}";
        }

        private static readonly JsonSerializerOptions Read = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        public async Task<ContractViewTranslation> TranslateAsync(
            string markdown,
            string language,
            IReadOnlyList<ProtectedValue> protectedValues,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(markdown))
                return ContractViewTranslation.Failed("Es liegt kein Vertragstext vor.");

            if (!ContractViewTranslationPrompt.Languages.TryGetValue(language, out var languageName))
                return ContractViewTranslation.Failed($"Die Sprache „{language}“ wird nicht unterstützt.");

            var vault = new ProtectedValueVault();

            foreach (var value in protectedValues)
                vault.Protect(value.Value, value.Kind);

            var segments = TranslatableMarkdown.Split(markdown);

            if (segments.Count == 0)
                return ContractViewTranslation.Failed("Es gibt nichts zu übersetzen.");

            // A run of words too long for the agreed schema to answer. Refused
            // here, where the reason can be logged, rather than sent to come
            // back as a schema violation with nothing to point at.
            var overlong = segments.FirstOrDefault(
                s => s.Text.Length > ContractViewTranslationPrompt.MaxSegmentLength);

            if (overlong is not null)
            {
                _logger.LogWarning(
                    "A contract view was not translated: segment {Field} is {Length} characters, over the {Limit} allowed.",
                    overlong.FieldId, overlong.Text.Length, ContractViewTranslationPrompt.MaxSegmentLength);

                return ContractViewTranslation.Failed(
                    "Ein Abschnitt des Vertrages ist für eine Übersetzung zu lang.");
            }

            var translated = new Dictionary<string, string>(StringComparer.Ordinal);
            var flags = new List<string>();

            foreach (var batch in Batch(segments))
            {
                var outcome = await TranslateBatchAsync(batch, vault, languageName, ct);

                // Every batch has to succeed. A document assembled from the
                // batches that worked is a document whose untranslated half the
                // reader cannot identify.
                if (outcome.Failure is not null)
                    return ContractViewTranslation.Failed(outcome.Failure);

                foreach (var (id, text) in outcome.Text) translated[id] = text;

                flags.AddRange(outcome.Flags);
            }

            _logger.LogInformation(
                "Translated a contract view into {Language}: {Segments} segment(s), {Flags} flag(s). Prompt {Prompt}.",
                language, segments.Count, flags.Count, ContractViewTranslationPrompt.Version);

            return new ContractViewTranslation
            {
                Succeeded = true,
                Markdown = TranslatableMarkdown.Reassemble(markdown, segments, translated),
                ReviewFlags = flags
            };
        }

        // ================================================================ a batch

        private sealed record BatchOutcome
        {
            public string? Failure { get; init; }
            public Dictionary<string, string> Text { get; init; } = new(StringComparer.Ordinal);
            public List<string> Flags { get; init; } = [];

            public static BatchOutcome Failed(string reason) => new() { Failure = reason };
        }

        private async Task<BatchOutcome> TranslateBatchAsync(
            IReadOnlyList<MarkdownSegment> batch,
            ProtectedValueVault vault,
            string languageName,
            CancellationToken ct)
        {
            var masked = batch
                .Select(s => s with { Text = vault.Mask(s.Text) })
                .ToList();

            var maskedById = masked.ToDictionary(s => s.FieldId, s => s.Text, StringComparer.Ordinal);

            var prompt = ContractViewTranslationPrompt.BuildUserPrompt(masked, vault.Values, languageName);

            AiCompletion completion;

            try
            {
                completion = await _ai.CompleteAsync(
                    new AiRequest(prompt)
                    {
                        SystemInstruction = ContractViewTranslationPrompt.System(languageName)
                                            + "\n\nResponse schema:\n"
                                            + ContractViewTranslationPrompt.JsonSchema,
                        MaxOutputTokens = 16000,
                        Purpose = "contract.view.translate"
                    },
                    ct);
            }
            catch (AiInvocationException ex)
            {
                _logger.LogWarning(
                    "Contract view translation failed: {Kind}. Reference {Reference}.",
                    ex.Kind, ex.CorrelationId);

                return BatchOutcome.Failed("Die Übersetzung ist derzeit nicht verfügbar.");
            }

            if (completion.FinishReason == AiFinishReason.Length)
                return BatchOutcome.Failed("Die Übersetzung wurde abgeschnitten und ist unvollständig.");

            Answer? answer;

            try
            {
                answer = JsonSerializer.Deserialize<Answer>(Unfence(completion.Text), Read);
            }
            catch (JsonException)
            {
                return BatchOutcome.Failed("Die Übersetzung hat kein verwertbares Ergebnis geliefert.");
            }

            if (answer is null)
                return BatchOutcome.Failed("Die Übersetzung hat kein verwertbares Ergebnis geliefert.");

            var outcome = new BatchOutcome();
            var byId = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var segment in answer.Segments)
            {
                if (byId.ContainsKey(segment.Id))
                    return BatchOutcome.Failed("Die Übersetzung enthält doppelte Abschnitte.");

                byId[segment.Id] = segment.Text ?? "";
            }

            foreach (var segment in batch)
            {
                if (!byId.TryGetValue(segment.FieldId, out var text) || string.IsNullOrWhiteSpace(text))
                    return BatchOutcome.Failed("Die Übersetzung ist unvollständig.");

                var maskedSource = maskedById[segment.FieldId];

                // The two checks that matter for a reading aid: a figure that
                // moved, and a protected name that did not come home. Everything
                // else about this text is the German's job, not the aid's.
                var problems = TranslationGuard.Inspect(
                    new TranslatableField(segment.FieldId, "", maskedSource, TranslatableContentType.Paragraph),
                    maskedSource,
                    text);

                // Negation is checked against German words, which is the wrong
                // question for a translation out of German — the target language
                // spells its negations differently. The figure and placeholder
                // rules are language-independent and are the ones kept.
                problems = problems
                    .Where(p => !p.Contains("Verneinung", StringComparison.Ordinal))
                    .ToList();

                if (problems.Count > 0)
                    return BatchOutcome.Failed("Die Übersetzung weicht inhaltlich vom Vertragstext ab.");

                outcome.Text[segment.FieldId] = vault.Restore(text).Trim();
            }

            foreach (var flag in answer.ReviewFlags) outcome.Flags.Add(flag.ToString());

            return outcome;
        }

        internal static IEnumerable<IReadOnlyList<MarkdownSegment>> Batch(
            IReadOnlyList<MarkdownSegment> segments)
        {
            var batch = new List<MarkdownSegment>();
            var characters = 0;

            foreach (var segment in segments)
            {
                var length = segment.Text?.Length ?? 0;

                if (batch.Count >= ContractViewTranslationPrompt.MaxSegmentsPerBatch ||
                    (batch.Count > 0 && characters + length > ContractViewTranslationPrompt.MaxCharactersPerBatch))
                {
                    yield return batch;
                    batch = [];
                    characters = 0;
                }

                batch.Add(segment);
                characters += length;
            }

            if (batch.Count > 0) yield return batch;
        }

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
