using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Services.Contracts.Language;
using WitcherHub.Infrastructure.Services.Contracts.Language;
using WitcherHub.Infrastructure.Services.OpenAI;

namespace WitcherHub.Tests
{
    /// <summary>
    /// What the normalizer does with an answer it cannot verify.
    ///
    /// The model is mocked throughout: these are about the machinery around the
    /// call, which is where the safety is. No test here needs an API key, and
    /// none makes a real request.
    ///
    /// The rule the whole class turns on is that a partial answer is a failure.
    /// A contract assembled from the fields that came back is a contract with a
    /// hole in it that reads like a finished document, which is worse than no
    /// contract at all — so anything short of a complete, verified translation
    /// stops the contract and says why.
    /// </summary>
    public class TheTranslatorRefusesABadAnswerTests
    {
        /// <summary>Answers with whatever the test hands it.</summary>
        private sealed class SaysExactly(string answer) : IAiTextGenerator
        {
            public string? LastPrompt { get; private set; }
            public string? LastSystemInstruction { get; private set; }
            public int Calls { get; private set; }

            public Task<string> GenerateTextAsync(string prompt)
            {
                Calls++;
                LastPrompt = prompt;
                return Task.FromResult(answer);
            }

            public Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct = default)
            {
                Calls++;
                LastPrompt = request.Prompt;
                LastSystemInstruction = request.SystemInstruction;

                return Task.FromResult(new AiCompletion(answer, AiFinishReason.Stop));
            }
        }

        private sealed class Throws(Exception failure) : IAiTextGenerator
        {
            public Task<string> GenerateTextAsync(string prompt) => throw failure;

            public Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct = default) =>
                throw failure;
        }

        private sealed class RunsOutOfRoom : IAiTextGenerator
        {
            public Task<string> GenerateTextAsync(string prompt) => Task.FromResult("{}");

            public Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct = default) =>
                Task.FromResult(new AiCompletion("{\"targetLanguage\":\"de\"", AiFinishReason.Length));
        }

        private static OpenAiContractLanguageNormalizer Normalizer(IAiTextGenerator ai) =>
            new(ai,
                Options.Create(new OpenAIOptions { ApiKey = "test", Model = "test-model" }),
                NullLogger<OpenAiContractLanguageNormalizer>.Instance);

        private static ContractNormalizationRequest OneField(string source = "Monthly SEO reporting.") =>
            new()
            {
                Fields =
                [
                    new TranslatableField("service.0.scope", "Leistungsumfang", source,
                        TranslatableContentType.Paragraph)
                ]
            };

        private static string Answer(
            string fieldId = "service.0.scope",
            string text = "Monatliches SEO-Reporting.",
            string status = "translated",
            bool canUse = true,
            string flags = "[]") =>
            $$"""
            {
              "targetLanguage": "de",
              "translatedFields": [
                {
                  "fieldId": "{{fieldId}}",
                  "translatedText": "{{text}}",
                  "detectedSourceLanguage": "en",
                  "translationStatus": "{{status}}"
                }
              ],
              "reviewFlags": {{flags}},
              "canUseForContract": {{(canUse ? "true" : "false")}}
            }
            """;

        // ============================================================ the happy path

        [Fact]
        public async Task AGoodAnswerIsAccepted()
        {
            var result = await Normalizer(new SaysExactly(Answer())).NormalizeAsync(OneField());

            Assert.True(result.Succeeded, result.FailureReason);
            Assert.True(result.CanUseForContract);
            Assert.Equal("Monatliches SEO-Reporting.", result.Text["service.0.scope"]);
            Assert.Equal("en", result.DetectedLanguages["service.0.scope"]);
        }

        [Fact]
        public async Task TheProvenanceIsRecordedWhateverHappens()
        {
            var result = await Normalizer(new SaysExactly("not json at all")).NormalizeAsync(OneField());

            Assert.False(result.Succeeded);
            Assert.Equal(ContractTranslationPrompt.Version, result.Provenance.PromptVersion);
            Assert.Equal(ContractTranslationPrompt.SchemaVersion, result.Provenance.SchemaVersion);
            Assert.Equal("test-model", result.Provenance.Model);
        }

        [Fact]
        public async Task NothingIsAskedWhenThereIsNothingToTranslate()
        {
            var ai = new SaysExactly(Answer());

            var result = await Normalizer(ai).NormalizeAsync(new ContractNormalizationRequest { Fields = [] });

            Assert.True(result.Succeeded);
            Assert.Equal(0, ai.Calls);
        }

        // ==================================================== answers that are refused

        [Fact]
        public async Task InvalidJsonIsRejected()
        {
            var result = await Normalizer(new SaysExactly("{ this is not json")).NormalizeAsync(OneField());

            Assert.False(result.Succeeded);
            Assert.Contains("JSON", result.FailureReason!, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AMissingFieldIsRejected()
        {
            var answer = """
                { "targetLanguage": "de", "translatedFields": [], "reviewFlags": [], "canUseForContract": true }
                """;

            var result = await Normalizer(new SaysExactly(answer)).NormalizeAsync(OneField());

            Assert.False(result.Succeeded);
            Assert.Contains("service.0.scope", result.FailureReason!, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AFieldTheModelRenamedIsRejected()
        {
            // Answering about service.9 when asked about service.0 is reported as
            // the missing field it is — the requested one did not come back, and
            // matching the answer up by position instead is how a description
            // lands on the wrong position.
            var result = await Normalizer(new SaysExactly(Answer(fieldId: "service.9.scope")))
                .NormalizeAsync(OneField());

            Assert.False(result.Succeeded);
            Assert.Contains("service.0.scope", result.FailureReason!, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AnExtraFieldNobodyAskedForIsRejected()
        {
            // Everything requested came back, and something else came with it. A
            // model inventing field ids has stopped following the schema, and a
            // plan that is partly salvaged is a contract nobody can account for.
            var answer = """
                {
                  "targetLanguage": "de",
                  "translatedFields": [
                    { "fieldId": "service.0.scope", "translatedText": "Monatliches SEO-Reporting.", "detectedSourceLanguage": "en", "translationStatus": "translated" },
                    { "fieldId": "service.7.scope", "translatedText": "Erfunden.", "detectedSourceLanguage": "en", "translationStatus": "translated" }
                  ],
                  "reviewFlags": [],
                  "canUseForContract": true
                }
                """;

            var result = await Normalizer(new SaysExactly(answer)).NormalizeAsync(OneField());

            Assert.False(result.Succeeded);
            Assert.Contains("unbekannte", result.FailureReason!, StringComparison.Ordinal);
            Assert.Contains("service.7.scope", result.FailureReason!, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ADuplicatedFieldIsRejected()
        {
            var answer = """
                {
                  "targetLanguage": "de",
                  "translatedFields": [
                    { "fieldId": "service.0.scope", "translatedText": "Eins.", "detectedSourceLanguage": "en", "translationStatus": "translated" },
                    { "fieldId": "service.0.scope", "translatedText": "Zwei.", "detectedSourceLanguage": "en", "translationStatus": "translated" }
                  ],
                  "reviewFlags": [],
                  "canUseForContract": true
                }
                """;

            var result = await Normalizer(new SaysExactly(answer)).NormalizeAsync(OneField());

            Assert.False(result.Succeeded);
            Assert.Contains("mehrfach", result.FailureReason!, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AnAnswerInTheWrongLanguageIsRejected()
        {
            var answer = Answer().Replace("\"targetLanguage\": \"de\"", "\"targetLanguage\": \"en\"");

            var result = await Normalizer(new SaysExactly(answer)).NormalizeAsync(OneField());

            Assert.False(result.Succeeded);
            Assert.Contains("Zielsprache", result.FailureReason!, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ATruncatedAnswerIsRejected()
        {
            // A cut-off answer parses as far as it got, which for a translation
            // means a contract whose last clauses are simply missing.
            var result = await Normalizer(new RunsOutOfRoom()).NormalizeAsync(OneField());

            Assert.False(result.Succeeded);
            Assert.Contains("abgeschnitten", result.FailureReason!, StringComparison.Ordinal);
        }

        // ============================================= failures that must not fall back

        [Fact]
        public async Task ATimeoutFailsTheTranslationRatherThanPassingTheSourceThrough()
        {
            var result = await Normalizer(new Throws(new AiInvocationException(
                    AiFailureKind.Timeout, "took too long", "REF123")))
                .NormalizeAsync(OneField());

            // Recoverable — the contract is not approved and the owner can try
            // again — and emphatically not "carry on with the English".
            Assert.False(result.Succeeded);
            Assert.False(result.CanUseForContract);
            Assert.Empty(result.Text);
            Assert.Contains("REF123", result.FailureReason!, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AMissingApiKeyFailsTheSameWay()
        {
            var result = await Normalizer(new Throws(new AiInvocationException(
                    AiFailureKind.NotConfigured, "no key", "REF999")))
                .NormalizeAsync(OneField());

            Assert.False(result.Succeeded);
            Assert.Empty(result.Text);
        }

        // ================================================================ flags

        [Fact]
        public async Task ABlockingReviewFlagStopsTheContract()
        {
            const string flags = """
                [{"fieldId":"service.0.scope","sourceExcerpt":"…","issue":"Mehrdeutig",
                  "recommendedAction":"Rückfrage beim Kunden","severity":"blocking"}]
                """;

            var result = await Normalizer(new SaysExactly(Answer(flags: flags))).NormalizeAsync(OneField());

            Assert.True(result.Succeeded);
            Assert.False(result.CanUseForContract);
            Assert.Contains(result.BlockingIssues, i => i.Contains("Mehrdeutig", StringComparison.Ordinal));
        }

        [Fact]
        public async Task AWarningDoesNotStopTheContract()
        {
            const string flags = """
                [{"fieldId":"service.0.scope","sourceExcerpt":"…","issue":"Stilistisch unklar",
                  "recommendedAction":"Prüfen","severity":"warning"}]
                """;

            var result = await Normalizer(new SaysExactly(Answer(flags: flags))).NormalizeAsync(OneField());

            Assert.True(result.CanUseForContract);
            Assert.Single(result.Warnings);
        }

        [Fact]
        public async Task TheModelsOwnRefusalIsHonoured()
        {
            var result = await Normalizer(new SaysExactly(Answer(canUse: false))).NormalizeAsync(OneField());

            Assert.False(result.CanUseForContract);
        }

        [Fact]
        public async Task AFieldMarkedNeedsReviewBlocks()
        {
            var result = await Normalizer(new SaysExactly(Answer(status: "needs_review")))
                .NormalizeAsync(OneField());

            Assert.False(result.CanUseForContract);
            Assert.Contains(result.BlockingIssues, i => i.Contains("prüfbedürftig", StringComparison.Ordinal));
        }

        [Fact]
        public async Task AnAnswerThatFailsADeterministicCheckBlocks()
        {
            // The model says it succeeded; the arithmetic says the deadline is
            // gone. The arithmetic wins.
            var result = await Normalizer(new SaysExactly(Answer(text: "Monatliches Reporting.")))
                .NormalizeAsync(OneField("Monthly reporting within 14 days."));

            Assert.True(result.Succeeded);
            Assert.False(result.CanUseForContract);
            Assert.Contains(result.BlockingIssues, i => i.Contains("14", StringComparison.Ordinal));
        }

        // =========================================================== what is sent

        [Fact]
        public async Task TheModelIsGivenTheRulesAndTheSchema()
        {
            var ai = new SaysExactly(Answer());

            await Normalizer(ai).NormalizeAsync(OneField());

            Assert.Contains("professioneller Übersetzungs", ai.LastSystemInstruction!);
            Assert.Contains("Antwortschema", ai.LastSystemInstruction!);
            Assert.Contains("\"enum\": [\"de\"]", ai.LastSystemInstruction!);

            // The field arrives as quoted JSON inside a labelled block, not as
            // prose pasted into a sentence.
            Assert.Contains("ÜBERSETZBARE FELDER", ai.LastPrompt!);
            Assert.Contains("\"fieldId\": \"service.0.scope\"", ai.LastPrompt!);
        }

        [Fact]
        public async Task ProtectedValuesAreMaskedBeforeTheyReachTheModel()
        {
            var ai = new SaysExactly("""
                {
                  "targetLanguage": "de",
                  "translatedFields": [
                    { "fieldId": "service.0.scope", "translatedText": "Leistungen für {{PROTECTED_001}}.",
                      "detectedSourceLanguage": "en", "translationStatus": "translated" }
                  ],
                  "reviewFlags": [],
                  "canUseForContract": true
                }
                """);

            var request = new ContractNormalizationRequest
            {
                Fields =
                [
                    new TranslatableField("service.0.scope", "Leistungsumfang",
                        "Services for Musterfirma GmbH.", TranslatableContentType.Paragraph)
                ],
                ProtectedValues =
                [
                    new ProtectedValue("", "Musterfirma GmbH", ProtectedValueKind.LegalCompanyName)
                ]
            };

            var result = await Normalizer(ai).NormalizeAsync(request);

            // The name never appeared in the translatable text sent out …
            var fieldsBlock = ai.LastPrompt![ai.LastPrompt.IndexOf("ÜBERSETZBARE FELDER", StringComparison.Ordinal)..
                                             ai.LastPrompt.IndexOf("GESCHÜTZTE WERTE", StringComparison.Ordinal)];

            Assert.DoesNotContain("Musterfirma GmbH", fieldsBlock);
            Assert.Contains("{{PROTECTED_001}}", fieldsBlock);

            // … and it is back, exactly, in the result.
            Assert.Equal("Leistungen für Musterfirma GmbH.", result.Text["service.0.scope"]);
            Assert.DoesNotContain("PROTECTED", result.Text["service.0.scope"]);
        }

        // ================================================================ batching

        [Fact]
        public void WorkIsSplitAtFieldBoundariesOnly()
        {
            var fields = Enumerable.Range(0, 95)
                .Select(i => new TranslatableField(
                    $"f{i}", "Leistungsumfang", new string('x', 500), TranslatableContentType.Paragraph))
                .ToList();

            var batches = OpenAiContractLanguageNormalizer.Batch(fields).ToList();

            Assert.True(batches.Count > 1);

            // Every field appears exactly once, in order. Splitting inside a
            // field would leave half a sentence to be translated out of context
            // and glued back by something that cannot read either half.
            Assert.Equal(
                fields.Select(f => f.FieldId),
                batches.SelectMany(b => b).Select(f => f.FieldId));
        }

        [Fact]
        public async Task OneFailingBatchFailsTheWholeTranslation()
        {
            // Answers the first batch and then breaks. A contract built from the
            // batch that worked is the outcome this prevents.
            var ai = new AnswersOnceThenFails();

            var fields = Enumerable.Range(0, 60)
                .Select(i => new TranslatableField(
                    $"f{i}", "Leistungsumfang", "Monthly reporting.", TranslatableContentType.Paragraph))
                .ToList();

            var result = await Normalizer(ai).NormalizeAsync(new ContractNormalizationRequest { Fields = fields });

            Assert.False(result.Succeeded);
            Assert.Empty(result.Text);
        }

        private sealed class AnswersOnceThenFails : IAiTextGenerator
        {
            private int _calls;

            public Task<string> GenerateTextAsync(string prompt) =>
                CompleteAsync(new AiRequest(prompt)).ContinueWith(t => t.Result.Text);

            public Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct = default)
            {
                if (++_calls > 1)
                {
                    throw new AiInvocationException(
                        AiFailureKind.RateLimited, "too many requests", "REF-BATCH");
                }

                var ids = System.Text.RegularExpressions.Regex
                    .Matches(request.Prompt, @"""fieldId"":\s*""([^""]+)""")
                    .Select(m => m.Groups[1].Value);

                var answer = new
                {
                    targetLanguage = "de",
                    translatedFields = ids.Select(id => new
                    {
                        fieldId = id,
                        translatedText = "Monatliche Berichterstattung.",
                        detectedSourceLanguage = "en",
                        translationStatus = "translated"
                    }),
                    reviewFlags = Array.Empty<object>(),
                    canUseForContract = true
                };

                return Task.FromResult(new AiCompletion(
                    JsonSerializer.Serialize(answer), AiFinishReason.Stop));
            }
        }
    }
}
