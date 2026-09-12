using System.Globalization;
using System.Text.RegularExpressions;

namespace WitcherHub.Application.Services.Contracts.Language
{
    /// <summary>
    /// Everything checked about a translation without asking the model whether it
    /// did its job.
    ///
    /// The model reports its own success. That report is worth having and worth
    /// nothing on its own: a run that quietly summarised three sentences into one,
    /// dropped a "nicht", or turned 1.900,00 into 1.900 will say
    /// canUseForContract true, because from inside the translation it looks
    /// finished. Each of those changes what the contract obliges somebody to do.
    ///
    /// So every rule here is arithmetic or string comparison on the two texts,
    /// and each one exists because of a specific way a translation goes wrong
    /// while still reading well.
    /// </summary>
    public static class TranslationGuard
    {
        /// <summary>
        /// Numbers, as they are written in either convention.
        ///
        /// Matches 1.900,00 and 1,900.00 and 19 and 19,5 — the point is not to
        /// parse them but to compare the multiset before and after, so a figure
        /// that appeared, vanished or changed shape is visible.
        /// </summary>
        private static readonly Regex Numbers =
            new(@"\d[\d.,]*", RegexOptions.Compiled);

        /// <summary>
        /// German negations, and the ones a translation drops most readily. A
        /// scope that said "Die Umsetzung ist nicht enthalten" and comes back
        /// without the "nicht" has inverted an exclusion into an obligation.
        /// </summary>
        private static readonly Regex GermanNegation =
            new(@"\b(?:nicht|kein|keine|keinen|keiner|keines|keinem|weder|ohne|ausgeschlossen|untersagt)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SourceNegation =
            new(@"\b(?:not|no|never|without|excluded|nicht|kein|keine|ohne|pas|aucun|değil|yok)\b|\bلا\b|\bليس\b|\bبدون\b|\bغير\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Markup and script, which a contract field never legitimately carries.</summary>
        private static readonly Regex Markup =
            new(@"<\s*(?:script|iframe|object|embed|style|img|a|div|span|p)\b|</\s*\w+\s*>|javascript:",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Instructions aimed at a model rather than at a reader. A translated
        /// field containing these means the injection was carried through into
        /// the contract instead of being translated as the text it is.
        /// </summary>
        private static readonly Regex Injection =
            new(@"\b(?:ignore (?:all )?(?:previous|prior|above)|disregard (?:the )?(?:previous|above)|" +
                @"system prompt|you are now|act as|new instructions?:|override (?:the )?instructions?)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// How much shorter a translation may be before it has summarised.
        /// German is typically longer than English and much longer than Arabic,
        /// so half the source length is already generous.
        /// </summary>
        private const double ShortestAcceptableRatio = 0.5;

        /// <summary>Below this, length ratios say nothing useful.</summary>
        private const int TooShortToJudge = 40;

        /// <summary>
        /// Checks one translated field against the text it came from. Returns the
        /// problems found, each already phrased for the review screen.
        /// </summary>
        public static IReadOnlyList<string> Inspect(
            TranslatableField source, string maskedSource, string translated)
        {
            var problems = new List<string>();

            if (string.IsNullOrWhiteSpace(translated))
            {
                problems.Add($"{source.FieldId}: Die Übersetzung ist leer.");
                return problems;
            }

            // 4. Protected placeholders intact.
            if (!ProtectedValueVault.Matches(maskedSource, translated, out var difference))
                problems.Add($"{source.FieldId}: {difference}");

            // 5. Figures unchanged. Compared as a multiset of the digit runs, so
            //    reordering a sentence is fine and losing "14" is not.
            var before = Figures(maskedSource);
            var after = Figures(translated);

            foreach (var (figure, count) in before)
            {
                var got = after.GetValueOrDefault(figure);

                if (got < count)
                {
                    problems.Add(
                        $"{source.FieldId}: Die Zahl „{figure}“ aus dem Ausgangstext fehlt in der Übersetzung.");
                }
            }

            foreach (var (figure, count) in after)
            {
                if (before.GetValueOrDefault(figure) >= count) continue;

                // 12. No invented figures. A translation that adds a number has
                //     added a term — a deadline, a quantity or a price nobody
                //     agreed. Ordinals a German sentence legitimately needs are
                //     rare enough to be worth a review line.
                problems.Add(
                    $"{source.FieldId}: Die Übersetzung enthält die Zahl „{figure}“, die im Ausgangstext nicht vorkommt.");
            }

            // 6. Negation not lost.
            if (SourceNegation.IsMatch(maskedSource) && !GermanNegation.IsMatch(translated))
            {
                problems.Add(
                    $"{source.FieldId}: Der Ausgangstext enthält eine Verneinung oder einen Ausschluss, " +
                    "die Übersetzung nicht.");
            }

            // 7. No markup or script.
            if (Markup.IsMatch(translated))
                problems.Add($"{source.FieldId}: Die Übersetzung enthält Markup oder Skript.");

            // 8. No instructions aimed at the model.
            if (Injection.IsMatch(translated))
            {
                problems.Add(
                    $"{source.FieldId}: Die Übersetzung enthält modellgerichtete Anweisungen statt Vertragstext.");
            }

            // 11. Not summarised.
            if (maskedSource.Length >= TooShortToJudge &&
                translated.Length < maskedSource.Length * ShortestAcceptableRatio)
            {
                problems.Add(
                    $"{source.FieldId}: Die Übersetzung ist deutlich kürzer als der Ausgangstext " +
                    $"({translated.Length} statt {maskedSource.Length} Zeichen) und wurde möglicherweise zusammengefasst.");
            }

            return problems;
        }

        /// <summary>
        /// The digit runs in a piece of text, counted.
        ///
        /// Placeholders are removed first: {{PROTECTED_001}} carries a number
        /// that is machinery rather than a contractual figure, and counting it
        /// would report a change every time the model moved a company name in
        /// the sentence.
        /// </summary>
        private static IReadOnlyDictionary<string, int> Figures(string text)
        {
            var withoutTokens = ProtectedValueVault.TokenPattern.Replace(text ?? "", " ");
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (Match match in Numbers.Matches(withoutTokens))
            {
                var figure = match.Value.Trim('.', ',');

                if (figure.Length == 0) continue;

                counts[figure] = counts.GetValueOrDefault(figure) + 1;
            }

            return counts;
        }

        /// <summary>
        /// A figure, formatted the German way, for the places that need to state
        /// one outside the translator.
        /// </summary>
        public static string Money(decimal value, string? currency) =>
            value.ToString("N2", CultureInfo.GetCultureInfo("de-DE")) +
            " " + (string.IsNullOrWhiteSpace(currency) ? "EUR" : currency);
    }
}
