using System.Text;
using System.Text.RegularExpressions;

namespace WitcherHub.Application.Services.Contracts.Language
{
    /// <summary>One piece of a contract that can be put into another language.</summary>
    /// <param name="Line">The line it came from, so the answer goes back where it belongs.</param>
    /// <param name="Cell">The table cell, or -1 when the whole line is one piece.</param>
    /// <param name="Prefix">
    /// The markup and numbering that must survive untouched — "## 3. ", "- ",
    /// "(2) ", "**". It is stripped before the text is sent and put back after,
    /// so a model cannot renumber a section or lose a bullet.
    /// </param>
    /// <param name="Suffix">The closing markup, for bold labels.</param>
    /// <param name="Text">The words themselves.</param>
    public sealed record MarkdownSegment(int Line, int Cell, string Prefix, string Suffix, string Text)
    {
        public string FieldId => Cell < 0 ? $"L{Line}" : $"L{Line}C{Cell}";
    }

    /// <summary>
    /// Takes a contract apart into the parts that are words, and puts it back
    /// together again.
    ///
    /// The point is what it refuses to send. A contract is mostly prose, but it
    /// also carries a price table, section numbers, signature rules and markup,
    /// and a translator handed the whole document will eventually renumber a
    /// clause, reformat 1.900,00 as 1,900.00 because that is how the number reads
    /// in the target language, or return a table with a column missing. None of
    /// those is something the translation was asked to do, and each changes what
    /// the contract says.
    ///
    /// So the structure is handled here, in code, and the model only ever sees
    /// runs of words:
    ///
    ///   * headings keep their "## 3. " and only the title travels;
    ///   * list markers, "(2)" paragraph numbers and bold markers are stripped
    ///     off and put back;
    ///   * table cells are translated one at a time, and only those containing no
    ///     digit at all — so every figure in the price table survives by
    ///     construction rather than by the model being careful;
    ///   * rules, separators and signature lines are not sent.
    /// </summary>
    public static class TranslatableMarkdown
    {
        private static readonly Regex Heading = new(@"^(#{1,6}\s+(?:\d+(?:\.\d+)*\.?\s+)?)(.*)$", RegexOptions.Compiled);
        private static readonly Regex ListItem = new(@"^(\s*[-*+]\s+)(.*)$", RegexOptions.Compiled);
        private static readonly Regex NumberedParagraph = new(@"^(\(\d+\)\s+)(.*)$", RegexOptions.Compiled);
        /// <summary>
        /// A bold label, and whatever follows it on the line.
        ///
        /// "**Leistungsumfang**" is a heading and its words travel. But
        /// "**Vertragsnummer:** C-4711" and "**Vertragsdatum:** 12.09.2026" are a
        /// label with a value beside it, and the value must not move. Matching
        /// only whole-line bold sent those lines whole, and a reformatted date —
        /// 12.09.2026 becoming 09/12/2026 — carries the same digits, so nothing
        /// downstream would have caught it.
        ///
        /// The label is translated; everything after the closing marker is kept.
        /// </summary>
        private static readonly Regex BoldLabel =
            new(@"^\s*(\*\*)([^*]+?)(\*\*)(.*)$", RegexOptions.Compiled);
        private static readonly Regex TableSeparator = new(@"^\s*\|[\s:|-]+\|\s*$", RegexOptions.Compiled);

        /// <summary>A cell holding a figure. Never sent, whatever else is in it.</summary>
        private static readonly Regex HasDigit = new(@"\d", RegexOptions.Compiled);

        /// <summary>Something with at least one letter in it is worth translating.</summary>
        private static readonly Regex HasLetter = new(@"\p{L}", RegexOptions.Compiled);

        /// <summary>
        /// Every run of words in the document, with where it came from.
        /// </summary>
        public static IReadOnlyList<MarkdownSegment> Split(string? markdown)
        {
            var segments = new List<MarkdownSegment>();

            if (string.IsNullOrWhiteSpace(markdown)) return segments;

            var lines = markdown.Replace("\r\n", "\n").Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (!HasLetter.IsMatch(line)) continue;

                // ── table rows ───────────────────────────────────────────────
                if (line.TrimStart().StartsWith('|'))
                {
                    if (TableSeparator.IsMatch(line)) continue;

                    var cells = line.Split('|');

                    for (var c = 0; c < cells.Length; c++)
                    {
                        var cell = cells[c];

                        // A cell with a figure in it is a figure. It is not sent
                        // at all, so no translation can reformat it.
                        if (HasDigit.IsMatch(cell) || !HasLetter.IsMatch(cell)) continue;

                        var bold = BoldLabel.Match(cell.Trim());

                        segments.Add(bold.Success
                            ? new MarkdownSegment(
                                i, c, "**", "**" + bold.Groups[4].Value, bold.Groups[2].Value.Trim())
                            : new MarkdownSegment(i, c, "", "", cell.Trim()));
                    }

                    continue;
                }

                // ── headings, keeping their number ───────────────────────────
                var heading = Heading.Match(line);

                if (heading.Success && HasLetter.IsMatch(heading.Groups[2].Value))
                {
                    segments.Add(new MarkdownSegment(
                        i, -1, heading.Groups[1].Value, "", heading.Groups[2].Value.Trim()));

                    continue;
                }

                // ── bold labels, with whatever value sits beside them ────────
                var bolded = BoldLabel.Match(line);

                if (bolded.Success && HasLetter.IsMatch(bolded.Groups[2].Value))
                {
                    segments.Add(new MarkdownSegment(
                        i, -1, "**", "**" + bolded.Groups[4].Value, bolded.Groups[2].Value.Trim()));

                    continue;
                }

                // ── list items and numbered paragraphs ───────────────────────
                var item = ListItem.Match(line);

                if (item.Success && HasLetter.IsMatch(item.Groups[2].Value))
                {
                    segments.Add(new MarkdownSegment(i, -1, item.Groups[1].Value, "", item.Groups[2].Value.Trim()));
                    continue;
                }

                var numbered = NumberedParagraph.Match(line);

                if (numbered.Success && HasLetter.IsMatch(numbered.Groups[2].Value))
                {
                    segments.Add(new MarkdownSegment(
                        i, -1, numbered.Groups[1].Value, "", numbered.Groups[2].Value.Trim()));

                    continue;
                }

                // ── ordinary prose ───────────────────────────────────────────
                segments.Add(new MarkdownSegment(i, -1, "", "", line.Trim()));
            }

            return segments;
        }

        /// <summary>
        /// The document again, with each run of words replaced by its
        /// translation and everything else exactly as it was.
        ///
        /// A segment with no translation keeps its original text, so a partial
        /// answer produces a partly translated document rather than a document
        /// with holes — and the caller refuses partial answers anyway.
        /// </summary>
        public static string Reassemble(
            string markdown,
            IReadOnlyList<MarkdownSegment> segments,
            IReadOnlyDictionary<string, string> translations)
        {
            var lines = markdown.Replace("\r\n", "\n").Split('\n').ToList();

            // Cells first and right to left, so replacing one does not move the
            // ones after it.
            foreach (var group in segments
                         .Where(s => s.Cell >= 0)
                         .GroupBy(s => s.Line))
            {
                var cells = lines[group.Key].Split('|').ToList();

                foreach (var segment in group)
                {
                    if (!translations.TryGetValue(segment.FieldId, out var translated) ||
                        string.IsNullOrWhiteSpace(translated))
                    {
                        continue;
                    }

                    cells[segment.Cell] = " " + segment.Prefix + translated.Trim() + segment.Suffix + " ";
                }

                lines[group.Key] = string.Join("|", cells);
            }

            foreach (var segment in segments.Where(s => s.Cell < 0))
            {
                if (!translations.TryGetValue(segment.FieldId, out var translated) ||
                    string.IsNullOrWhiteSpace(translated))
                {
                    continue;
                }

                lines[segment.Line] = segment.Prefix + translated.Trim() + segment.Suffix;
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// A stable fingerprint of the wording, so a cached translation is
        /// discarded the moment the contract it was made from changes.
        /// </summary>
        public static string Fingerprint(string? markdown)
        {
            var bytes = System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes((markdown ?? "").Replace("\r\n", "\n")));

            var text = new StringBuilder(bytes.Length * 2);

            foreach (var b in bytes) text.Append(b.ToString("x2"));

            return text.ToString();
        }
    }
}
