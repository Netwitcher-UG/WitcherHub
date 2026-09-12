using System.Globalization;
using System.Text.RegularExpressions;

namespace WitcherHub.Application.Services.Contracts
{
    /// <summary>
    /// One passage that is not German, and where it is.
    /// </summary>
    /// <param name="Field">The field it came from, named the way the review screen names it.</param>
    /// <param name="Text">The passage itself, so the reviewer can see what to fix.</param>
    /// <param name="Reason">What was detected — a script, or a run of English.</param>
    public sealed record UntranslatedPassage(string Field, string Text, string Reason)
    {
        public override string ToString() => $"{Field}: {Reason} — „{Trim(Text)}“";

        private static string Trim(string text) =>
            text.Length <= 120 ? text : text[..120].TrimEnd() + " […]";
    }

    /// <summary>
    /// Checks that the descriptive content of a contract is actually in German.
    ///
    /// The contract is written in German whatever language the service was
    /// entered in, which means the model is translating — and a translation
    /// nobody checked is a promise nobody checked. A contract that reaches a
    /// customer with half a sentence of Arabic or English in the scope is not a
    /// formatting problem; it is wording somebody may be asked to sign without
    /// being able to read.
    ///
    /// What this can and cannot do is worth being plain about, because the
    /// difference decides how its findings are used:
    ///
    ///   * A non-Latin script is a fact. Arabic, Hebrew, Cyrillic, Greek and CJK
    ///     characters in a German contract's descriptive text were not
    ///     translated, and that is detected outright.
    ///
    ///   * English is a judgement. There is no reliable way to tell "Monthly
    ///     Social Media Management" from a product name with a regular
    ///     expression, so English is detected by function words — the, and, of,
    ///     with, shall — which almost never appear in German prose and are the
    ///     hardest part of English to avoid. It is deliberately conservative and
    ///     its findings are for review, never for rejection on their own.
    ///
    /// And a name is not a translation failure. An Arabic customer name, an
    /// Arabic signatory, a company registered in Arabic script — those are
    /// exactly the things that must NOT be translated, so anything the model
    /// declared as a preserved term, and anything matching a party's own name, is
    /// excluded before the scripts are counted. The alternative is a system that
    /// refuses to issue a contract to a customer because of how their name is
    /// spelled.
    /// </summary>
    public static class GermanOnlyCheck
    {
        /// <summary>
        /// Scripts that no descriptive passage of a German contract is written
        /// in. Latin and common punctuation are the only things expected.
        /// </summary>
        private static readonly (string Name, Regex Pattern)[] ForeignScripts =
        [
            ("Arabischer Text", new Regex(@"\p{IsArabic}", RegexOptions.Compiled)),
            ("Hebräischer Text", new Regex(@"\p{IsHebrew}", RegexOptions.Compiled)),
            ("Kyrillischer Text", new Regex(@"\p{IsCyrillic}", RegexOptions.Compiled)),
            ("Griechischer Text", new Regex(@"\p{IsGreek}", RegexOptions.Compiled)),
            ("Ostasiatischer Text", new Regex(
                @"[\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}\p{IsHangulSyllables}]",
                RegexOptions.Compiled))
        ];

        /// <summary>
        /// English function words. Chosen because they carry no meaning on their
        /// own, which is what makes them hard to leave out of English prose and
        /// unlikely to appear in German — unlike nouns, which a German contract
        /// legitimately borrows ("Content", "Reporting", "Social Media").
        /// </summary>
        private static readonly Regex EnglishFunctionWords = new(
            @"\b(?:the|and|of|for|with|shall|will|must|are|is|be|been|this|that|from|" +
            @"including|according|provided|between|their|which|these|those|when|where)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// How many English function words in one passage stop being a borrowed
        /// term and start being an English sentence. Two is already unusual in
        /// German prose; one ("Content for Instagram") is not.
        /// </summary>
        private const int EnglishWordsThatMeanASentence = 2;

        /// <summary>
        /// Text short enough that a script hit is far more likely to be a name
        /// than a passage. Checked anyway, but only when it was not declared as a
        /// preserved term.
        /// </summary>
        private const int NameLength = 60;

        /// <summary>
        /// Every descriptive passage that is not German.
        /// </summary>
        /// <param name="fields">Field name to text, in the order a reviewer reads them.</param>
        /// <param name="preserved">
        /// Values that are deliberately not translated — company names, personal
        /// names, brands, product names, domains, identifiers. Anything the model
        /// declared, plus the parties' own names.
        /// </param>
        public static IReadOnlyList<UntranslatedPassage> Inspect(
            IEnumerable<KeyValuePair<string, string?>> fields,
            IEnumerable<string>? preserved = null)
        {
            ArgumentNullException.ThrowIfNull(fields);

            var allowed = (preserved ?? [])
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .ToList();

            var found = new List<UntranslatedPassage>();

            foreach (var (field, value) in fields)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;

                var text = Without(value!, allowed);

                if (string.IsNullOrWhiteSpace(text)) continue;

                var script = ForeignScripts.FirstOrDefault(s => s.Pattern.IsMatch(text));

                if (script.Pattern is not null)
                {
                    found.Add(new UntranslatedPassage(field, value!, script.Name));
                    continue;
                }

                // Only whole passages, not names. A single short line that is
                // not declared as preserved is still checked — an untranslated
                // title is exactly the case the owner reported — but the English
                // rule needs enough words to be a sentence.
                if (CountEnglish(text) >= EnglishWordsThatMeanASentence)
                {
                    found.Add(new UntranslatedPassage(
                        field, value!,
                        text.Length <= NameLength
                            ? "Vermutlich englischer Text"
                            : "Englischer Text"));
                }
            }

            return found;
        }

        /// <summary>
        /// The text with every preserved term removed, so a German sentence
        /// carrying an Arabic company name is a German sentence.
        /// </summary>
        private static string Without(string text, IReadOnlyList<string> preserved)
        {
            foreach (var term in preserved)
            {
                text = text.Replace(term, " ", StringComparison.OrdinalIgnoreCase);

                // Also each word of a multi-word name on its own: a name reaches
                // the contract inflected or split across a line as often as it
                // arrives whole.
                if (!term.Contains(' ', StringComparison.Ordinal)) continue;

                foreach (var word in term.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (word.Length > 2)
                        text = text.Replace(word, " ", StringComparison.OrdinalIgnoreCase);
            }

            // Addresses, links and identifiers are never translated and are full
            // of Latin fragments that read as English.
            text = Regex.Replace(text, @"https?://\S+|www\.\S+|\S+@\S+\.\S+", " ");

            return text;
        }

        private static int CountEnglish(string text)
        {
            var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in EnglishFunctionWords.Matches(text))
                distinct.Add(match.Value);

            return distinct.Count;
        }

        /// <summary>
        /// What the reviewer is told, in the language the rest of the review
        /// notes are written in. One line per passage, naming the field.
        /// </summary>
        public static string Describe(IReadOnlyList<UntranslatedPassage> passages) =>
            "Nicht übersetzte Inhalte: " +
            string.Join(" | ", passages.Select(p => p.ToString()));

        /// <summary>
        /// A contract number, tax number or registry number, which must survive
        /// translation unchanged. Offered so callers can add them to the
        /// preserved list without each inventing the same pattern.
        /// </summary>
        public static IEnumerable<string> IdentifiersIn(params string?[] values) =>
            values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim());

        /// <summary>
        /// German money, for the places that format it outside the composer.
        /// Kept here so "1.900,00 EUR" is written the same way everywhere.
        /// </summary>
        public static string Money(decimal value, string? currency) =>
            value.ToString("N2", CultureInfo.GetCultureInfo("de-DE")) +
            " " + (string.IsNullOrWhiteSpace(currency) ? "EUR" : currency);
    }
}
