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
    }
}
