using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WitcherHub.Application.Services.Contracts
{
    /// <summary>
    /// What the generator returns: clause content, and no presentation.
    ///
    /// The model used to return the finished document as Markdown, which meant it
    /// decided the heading levels, the numbering, whether there was a title, and
    /// whether the parties appeared at all — so two contracts generated a minute
    /// apart did not look like the same company's contracts. It returns sections
    /// now, and <see cref="ToDocumentMarkdown"/> is the only thing that decides
    /// how they are set.
    /// </summary>
    public sealed class GeneratedContractContent
    {
        [JsonPropertyName("language")]
        public string? Language { get; set; }

        /// <summary>"Dienstleistungsvertrag", "Wartungsvertrag", and so on.</summary>
        [JsonPropertyName("contractType")]
        public string? ContractType { get; set; }

        [JsonPropertyName("preamble")]
        public string? Preamble { get; set; }

        [JsonPropertyName("sections")]
        public List<ContractSectionContent> Sections { get; set; } = new();

        public bool HasContent => Sections.Any(s => s.HasContent);

        /// <summary>
        /// Renders the clauses as the §§ of a German contract.
        ///
        /// The numbering is applied here, not asked for: a model told to write
        /// "§ 4" will eventually write two of them, or skip one, and a contract
        /// with two § 4s is a contract somebody has to explain. Same for the (1)
        /// (2) paragraph numbers and the a) b) c) items.
        /// </summary>
        public string ToClauseMarkdown()
        {
            var md = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(Preamble))
            {
                md.Append("## Präambel\n\n")
                  .Append(Preamble.Trim())
                  .Append("\n\n");
            }

            var number = 0;

            foreach (var section in Sections.Where(s => s.HasContent))
            {
                number++;

                md.Append("## § ").Append(number).Append(' ')
                  .Append(CleanHeading(section.Heading, number))
                  .Append("\n\n");

                var paragraphs = section.Paragraphs
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p.Trim())
                    .ToList();

                for (var i = 0; i < paragraphs.Count; i++)
                {
                    // A § with one paragraph needs no number: "(1)" alone reads
                    // as though a "(2)" went missing.
                    var prefix = paragraphs.Count > 1 ? $"({i + 1}) " : "";

                    md.Append(prefix).Append(paragraphs[i]).Append("\n\n");
                }

                var items = section.Items
                    .Where(i => !string.IsNullOrWhiteSpace(i))
                    .Select(i => i.Trim())
                    .ToList();

                if (items.Count > 0)
                {
                    // Markdown has no lettered list, so the letters are written
                    // out and the list is left unordered.
                    for (var i = 0; i < items.Count; i++)
                        md.Append("- ").Append(Letter(i)).Append(") ").Append(items[i]).Append('\n');

                    md.Append('\n');
                }
            }

            return StripCoverageIds(md.ToString().TrimEnd()) + "\n";
        }

        /// <summary>
        /// Removes the internal coverage references, wherever they got in.
        ///
        /// The ids exist so the application can check its own work; they mean
        /// nothing to a customer and printing one in a contract would be an
        /// obvious defect in a document somebody signs. The prompt says not to
        /// write them, which is worth saying and not worth relying on — a model
        /// asked to state which entries a paragraph covers will sometimes state it
        /// in the paragraph.
        ///
        /// Deliberately narrow: it matches the shape this application issues
        /// (POS-001-02, REC-004, SRC-011, TRM-002, TOT-001) and leaves anything
        /// else alone, because a contract may perfectly well contain a customer's
        /// own reference like "ABC-123".
        /// </summary>
        internal static string StripCoverageIds(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // The id, plus any bracket, parenthesis or separator it was wrapped in,
            // so removing it does not leave "( , )" behind.
            var stripped = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"[\[\(]?\s*\b(?:POS|REC|SRC|TRM|TOT)-\d{3}(?:-\d{2})?\b(?:\s*[,;]\s*\b(?:POS|REC|SRC|TRM|TOT)-\d{3}(?:-\d{2})?\b)*\s*[\]\)]?",
                "");

            // Tidy the punctuation the removal can strand.
            stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"[ \t]{2,}", " ");
            stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @" +([,.;:])", "$1");

            return stripped;
        }

        /// <summary>
        /// Strips a number the model added anyway, so "§ 4 § 4 Vergütung" cannot
        /// happen, and falls back to something honest when the heading is empty.
        /// </summary>
        private static string CleanHeading(string? heading, int number)
        {
            var text = (heading ?? "").Trim();

            if (text.Length == 0) return $"Abschnitt {number}";

            // "§ 4 Vergütung", "§4 Vergütung", "4. Vergütung", "4 Vergütung"
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"^\s*(§\s*)?\d+\s*[\.\)]?\s*", "");

            return text.Length == 0 ? $"Abschnitt {number}" : text;
        }

        private static char Letter(int index) => (char)('a' + (index % 26));

        // ---------------------------------------------------------------

        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        /// <summary>
        /// Reads the model's answer, tolerating the two things it does anyway:
        /// wrapping JSON in a code fence, and adding a sentence before it.
        /// </summary>
        public static bool TryParse(string? raw, out GeneratedContractContent content, out string? error)
        {
            content = new GeneratedContractContent();
            error = null;

            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "The assistant returned nothing.";
                return false;
            }

            var json = ExtractJson(raw);

            if (json is null)
            {
                error = "No JSON object was found in the answer.";
                return false;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<GeneratedContractContent>(json, ReadOptions);

                if (parsed is null || !parsed.HasContent)
                {
                    error = "The answer contained no contract sections.";
                    return false;
                }

                content = parsed;
                return true;
            }
            catch (JsonException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// The outermost {...} in the answer. Balanced rather than greedy, so a
        /// trailing explanation after the object does not swallow the parse.
        /// </summary>
        internal static string? ExtractJson(string raw)
        {
            var start = raw.IndexOf('{');
            if (start < 0) return null;

            var depth = 0;
            var inString = false;
            var escaped = false;

            for (var i = start; i < raw.Length; i++)
            {
                var c = raw[i];

                if (escaped) { escaped = false; continue; }
                if (c == '\\' && inString) { escaped = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;

                if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return raw[start..(i + 1)];
            }

            return null;
        }
    }

    public sealed class ContractSectionContent
    {
        [JsonPropertyName("heading")]
        public string? Heading { get; set; }

        [JsonPropertyName("paragraphs")]
        public List<string> Paragraphs { get; set; } = new();

        [JsonPropertyName("items")]
        public List<string> Items { get; set; } = new();

        /// <summary>
        /// The coverage ids this section says it accounts for.
        ///
        /// A declaration, not a fact. It is checked against the text for anything
        /// with a figure in it, and taken at its word for topics — which is why an
        /// unclaimed topic counts as a gap even when the section reads well.
        /// </summary>
        [JsonPropertyName("covers")]
        public List<string> Covers { get; set; } = new();

        public bool HasContent =>
            Paragraphs.Any(p => !string.IsNullOrWhiteSpace(p)) ||
            Items.Any(i => !string.IsNullOrWhiteSpace(i));
    }
}
