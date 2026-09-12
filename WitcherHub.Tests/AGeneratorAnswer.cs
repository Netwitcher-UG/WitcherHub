namespace WitcherHub.Tests
{
    /// <summary>
    /// A well-formed answer from the contract generator, for tests that need the
    /// generation step to succeed without caring what it says.
    ///
    /// The generator returns structured clause content now, not the finished
    /// Markdown: the application composes the title, the parties, the § numbering
    /// and the signature block, so that every contract looks like the same
    /// company's contract instead of however the model felt like formatting it
    /// that run. Tests that stubbed the old answer with "## Wording" were
    /// stubbing a shape nothing produces any more.
    /// </summary>
    internal static class AGeneratorAnswer
    {
        /// <summary>The smallest answer that parses and has content.</summary>
        public const string Minimal = """
            {
              "language": "de",
              "contractType": "Dienstleistungsvertrag",
              "sections": [
                {
                  "heading": "Gegenstand des Vertrags",
                  "paragraphs": ["Der Auftragnehmer erbringt die vereinbarten Leistungen."]
                }
              ]
            }
            """;

        /// <summary>
        /// A fuller answer: several §§, numbered paragraphs and a lettered list,
        /// for tests that look at the composed document.
        /// </summary>
        public const string Complete = """
            {
              "language": "de",
              "contractType": "Dienstleistungsvertrag",
              "preamble": "Die Parteien vereinbaren die laufende Betreuung der Vertriebskanäle.",
              "sections": [
                {
                  "heading": "Gegenstand des Vertrags",
                  "paragraphs": [
                    "Der Auftragnehmer erbringt für den Auftraggeber die vereinbarten Leistungen.",
                    "Der Leistungsumfang ergibt sich aus den vereinbarten Positionen."
                  ]
                },
                {
                  "heading": "Leistungsumfang",
                  "paragraphs": ["Die Leistungen umfassen insbesondere:"],
                  "items": [
                    "Betreuung der bestehenden Verkaufskanäle",
                    "Pflege der Produktdaten"
                  ]
                },
                {
                  "heading": "Vergütung und Zahlung",
                  "paragraphs": ["Die Vergütung richtet sich nach den vereinbarten Positionen."]
                }
              ]
            }
            """;
        /// <summary>
        /// The structured "Anlage A – Leistungsbeschreibung", in the schema the
        /// Agenturvertrag generator's prompt sets out.
        ///
        /// A contract with positions is written by that generator now — the same
        /// one a signed quote uses — and it asks for JSON, not clauses. A stub
        /// that answers every prompt with clause content makes it throw, and the
        /// test then reports "the assistant could not produce a contract" when
        /// what actually happened is that the harness spoke the wrong language.
        /// </summary>
        public const string AnlageA = """
            {
              "version": "1.0",
              "language": "de-DE",
              "positions": [
                {
                  "positionNo": 1,
                  "title": "Monatliche Betreuung",
                  "quantity": 1,
                  "unitNetPrice": 2000,
                  "lineNetPrice": 2000,
                  "taxRatePercent": 19,
                  "sections": {
                    "scope": "Laufende Betreuung der Vertriebskanaele des Auftraggebers.",
                    "deliverables": ["Monatlicher Report"],
                    "outOfScope": ["Mediabudget"],
                    "customerResponsibilities": ["Zugaenge bereitstellen"],
                    "acceptanceCriteria": ["Report bis zum 5. Werktag"],
                    "timeline": "Monatlich",
                    "assumptions": "Die Zugaenge stehen zur Verfuegung.",
                    "revisions": "Eine Korrekturschleife je Report."
                  },
                  "customClauses": []
                }
              ]
            }
            """;

        /// <summary>
        /// A well-formed plan from the contract planner: the model describing the
        /// work and choosing clause modules from the allowed list.
        ///
        /// The builder's Generate goes through ContractComposer now, which asks a
        /// different question from either of the older generators. A stub that
        /// answers every prompt with clause content makes the composer fail to
        /// parse, and the test then reports that the assistant refused when what
        /// actually happened is that the harness spoke the wrong language.
        /// </summary>
        public const string Plan = """
            {
              "contractClassification": {
                "overallType": "service",
                "recurrence": "recurring",
                "reason": "Laufende Betreuung ohne abnahmefaehiges Werk."
              },
              "serviceSections": [
                {
                  "serviceItemId": "",
                  "serviceType": "service",
                  "scope": "Laufende Betreuung der Vertriebskanaele des Auftraggebers.",
                  "deliverables": ["Monatlicher Report"],
                  "outOfScope": ["Mediabudget"],
                  "customerObligations": ["Zugaenge bereitstellen"],
                  "acceptanceCriteria": [],
                  "assumptions": ["Datenquellen stehen zur Verfuegung"],
                  "dependencies": ["Zugriff auf die Search Console"],
                  "revisionRules": ["Redaktionelle Korrekturen am Bericht"],
                  "riskNotes": ["Tool-Updates koennen Datenverlaeufe beeinflussen"]
                }
              ],
              "selectedClauseModuleIds": [
                "SERVICE_NO_SUCCESS_GUARANTEE",
                "THIRD_PARTY_PLATFORM_DEPENDENCY"
              ],
              "missingInformation": [],
              "reviewFlags": [],
              "canGenerateFinalContract": true
            }
            """;

        /// <summary>
        /// The plan, with the position id echoed back from the request.
        ///
        /// The schema requires the model to return the serviceItemId it was
        /// given, and the composer matches descriptions to positions on it — a
        /// section carrying any other id describes nothing, which the composer
        /// now refuses to approve. A stub answering with a fixed id would
        /// therefore exercise that refusal on every test rather than the case
        /// the test is about, so it does here what the model is told to do.
        /// </summary>
        public static string PlanFor(string prompt)
        {
            var given = System.Text.RegularExpressions.Regex.Match(
                prompt, @"""serviceItemId"":\s*""([^""]*)""");

            return given.Success
                ? Plan.Replace("\"serviceItemId\": \"\"", $"\"serviceItemId\": \"{given.Groups[1].Value}\"",
                    StringComparison.Ordinal)
                : Plan;
        }

        /// <summary>
        /// The answer the prompt is actually asking for.
        ///
        /// Three generators are reachable and each wants a different shape: the
        /// contract planner wants a plan, the Agenturvertrag generator wants
        /// Anlage A as JSON, and the pipeline wants clause content. Every stub
        /// routes through this so a test can say what the model returns without
        /// also having to know which generator ran.
        /// </summary>
        public static string For(string prompt, string clauseAnswer)
        {
            if (prompt.Contains("ERLAUBTE KLAUSELMODULE", StringComparison.Ordinal))
                return PlanFor(prompt);

            if (prompt.Contains("Anlage A", StringComparison.Ordinal) &&
                prompt.Contains("Return JSON ONLY", StringComparison.Ordinal))
                return AnlageA;

            return clauseAnswer;
        }
    }
}
