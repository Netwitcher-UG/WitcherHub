using System.Text;
using System.Text.RegularExpressions;

namespace WitcherHub.Application.Services.Contracts
{
    /// <summary>
    /// How a German contract refers to its own sections, and to the law.
    ///
    /// The documents this application produced set every clause as "§ 4
    /// Vergütung". The owner asked for the symbol to go — and the symbol is the
    /// easy half. A contract that drops it without thinking ends up saying
    /// "gemäß 4 dieses Vertrags", which refers to nothing, or silently deletes
    /// "§§ 611 ff. BGB", which is a statement about which law governs the
    /// agreement and cannot be dropped without changing what was agreed.
    ///
    /// So there are two different things here that happen to share a character:
    ///
    ///   * a reference to a section of THIS contract, which becomes "Ziffer 4";
    ///   * a reference to a section of a STATUTE, which stays exactly as precise
    ///     as it was and is merely spelled out — "Paragraph 126a BGB".
    ///
    /// Telling them apart is done on the text, not on a guess: German statute
    /// abbreviations carry at least two capital letters (BGB, UStG, DSGVO,
    /// GmbHG, MarkenG), and ordinary German words — including the nouns that
    /// follow an internal reference, "dieses Vertrags", "dieser Vereinbarung" —
    /// carry exactly one. That is a property of the language rather than a list
    /// somebody has to keep up to date, and where it cannot decide it says so
    /// instead of choosing.
    /// </summary>
    public static class GermanLegalText
    {
        /// <summary>
        /// Abbreviations that continue a citation rather than ending it. A token
        /// ending in a full stop normally means the sentence stopped there; these
        /// are the ones where it did not.
        /// </summary>
        private static readonly HashSet<string> CitationParts = new(StringComparer.Ordinal)
        {
            "Abs.", "Abs", "Nr.", "Nr", "S.", "S", "Satz", "Alt.", "Alt",
            "lit.", "lit", "Buchst.", "Buchst", "ff.", "ff", "f.", "f",
            "Halbsatz", "Var.", "Var", "i.V.m.", "iVm"
        };

        private const int TokensToLookAhead = 5;

        /// <summary>
        /// Every paragraph sign in the text, spelled out — internal references as
        /// "Ziffer", statutory ones as "Paragraph", both keeping the number, the
        /// subsection and the statute they already had.
        ///
        /// Idempotent: text that has already been through this has no signs left
        /// to convert, so running it twice changes nothing.
        /// </summary>
        public static string SpellOutParagraphSigns(string? text)
        {
            if (string.IsNullOrEmpty(text) || !text.Contains('§', StringComparison.Ordinal))
                return text ?? "";

            var result = new StringBuilder(text.Length + 16);
            var i = 0;

            while (i < text.Length)
            {
                if (text[i] != '§')
                {
                    result.Append(text[i]);
                    i++;
                    continue;
                }

                var plural = i + 1 < text.Length && text[i + 1] == '§';
                var afterSign = i + (plural ? 2 : 1);

                // The spacing the source used is not carried over: "§4" and
                // "§ 4" both become one word followed by one space.
                var rest = afterSign;
                while (rest < text.Length && (text[rest] == ' ' || text[rest] == ' ')) rest++;

                var statutory = LooksStatutory(text, rest);

                result.Append(plural
                    ? statutory ? "Paragraphen" : "Ziffern"
                    : statutory ? "Paragraph" : "Ziffer");

                // A sign with nothing after it is left as a word on its own
                // rather than gaining a space it has no use for.
                if (rest < text.Length) result.Append(' ');

                i = rest;
            }

            return result.ToString();
        }

        /// <summary>
        /// True when what follows the sign cites a statute.
        ///
        /// Reads at most a handful of tokens and stops at the end of the
        /// sentence, so "gemäß § 4 dieses Vertrags. Im Übrigen gilt das BGB."
        /// is read as the internal reference it is rather than reaching across
        /// the full stop for a word that belongs to the next sentence.
        /// </summary>
        private static bool LooksStatutory(string text, int from)
        {
            var seen = 0;
            var i = from;

            while (i < text.Length && seen < TokensToLookAhead)
            {
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                if (i >= text.Length) break;

                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;

                var token = text[start..i];
                if (token.Length == 0) break;

                if (CapitalCount(token.Trim(',', ';', ')', '(', '"')) >= 2) return true;

                seen++;

                // The sentence ended, so anything further belongs to a different
                // statement and says nothing about this reference.
                if (EndsSentence(token)) break;
            }

            return false;
        }

        private static bool EndsSentence(string token)
        {
            if (!token.EndsWith('.') && !token.EndsWith(':') && !token.EndsWith(';')) return false;

            return !CitationParts.Contains(token) && !CitationParts.Contains(token.TrimEnd(',', ')'));
        }

        private static int CapitalCount(string token)
        {
            var capitals = 0;

            foreach (var c in token)
                if (char.IsUpper(c)) capitals++;

            return capitals;
        }

        // ==================================================== section numbering

        /// <summary>
        /// A top-level contract section heading: "3. Vergütung und Zahlung".
        ///
        /// The number is applied here and never asked of the model. A model told
        /// to write "§ 4" will eventually write two of them or skip one, and the
        /// same is true of "4." — the numbering is the application's to keep
        /// sequential, and it is the only thing cross-references can rely on.
        /// </summary>
        public static string Heading(int number, string? title) =>
            $"{number}. {CleanTitle(title, number)}";

        /// <summary>A subsection heading: "1.2 Liefergegenstände".</summary>
        public static string Heading(int number, int subNumber, string? title) =>
            $"{number}.{subNumber} {CleanTitle(title, number)}";

        /// <summary>
        /// The title with any numbering the model added of its own accord taken
        /// off, so "3. § 3 Vergütung" cannot happen.
        ///
        /// Covers what a model actually produces: "§ 4 Vergütung", "§4 Vergütung",
        /// "4. Vergütung", "4 Vergütung", "4.1 Vergütung", and the spelled-out
        /// forms this application itself now writes.
        /// </summary>
        public static string CleanTitle(string? title, int fallbackNumber)
        {
            var text = (title ?? "").Trim();

            if (text.Length == 0) return $"Abschnitt {fallbackNumber}";

            text = Regex.Replace(
                text,
                @"^\s*(?:§{1,2}\s*|(?:Ziffern?|Paragraphen?)\s+)?\d+(?:\.\d+)*\s*[\.\)]?\s*",
                "",
                RegexOptions.CultureInvariant);

            text = text.Trim();

            return text.Length == 0 ? $"Abschnitt {fallbackNumber}" : text;
        }

        // ======================================================= the final check

        /// <summary>
        /// Whether a finished document still carries the symbol anywhere.
        ///
        /// Used as a check on the way out rather than as a way of removing it:
        /// somewhere upstream is producing text nobody converted, and a contract
        /// is not the place to paper over that. What it finds is reported for
        /// review, and the finding names the line.
        /// </summary>
        public static IReadOnlyList<string> FindParagraphSigns(string? document)
        {
            if (string.IsNullOrEmpty(document) || !document.Contains('§', StringComparison.Ordinal))
                return [];

            return document
                .Split('\n')
                .Where(line => line.Contains('§', StringComparison.Ordinal))
                .Select(line => line.Trim())
                .Take(20)
                .ToList();
        }
    }
}
