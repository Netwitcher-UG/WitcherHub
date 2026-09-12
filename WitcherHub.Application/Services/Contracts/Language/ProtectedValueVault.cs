using System.Globalization;
using System.Text.RegularExpressions;

namespace WitcherHub.Application.Services.Contracts.Language
{
    /// <summary>
    /// Keeps the things a translator must not touch out of the translator's
    /// reach.
    ///
    /// A model asked to put a sentence into German will, given the chance,
    /// render "Netwitcher UG (haftungsbeschränkt)" as "Netwitcher Ltd",
    /// helpfully translate "haftungsbeschränkt", turn "1.900,00 EUR" into
    /// "1,900.00 EUR" because that is how the number reads in the source
    /// language, or write a date as 03/08/2026 where the reader will take it for
    /// the eighth of March. None of those is a translation error the model would
    /// call an error; each of them changes what the contract says.
    ///
    /// So the values never reach it. Every occurrence is swapped for an opaque
    /// marker before the call and swapped back after, and the swap back is
    /// checked: a marker that came home changed, duplicated or missing fails the
    /// field rather than being patched up. That check is the whole point — it is
    /// the difference between hoping the model obeyed rule 10 and knowing it did.
    /// </summary>
    public sealed class ProtectedValueVault
    {
        private readonly List<ProtectedValue> _values = [];
        private int _next = 1;

        /// <summary>
        /// The marker shape. Deliberately unlike anything that occurs in German
        /// contract prose, and matched exactly when counting occurrences.
        /// </summary>
        public static readonly Regex TokenPattern =
            new(@"\{\{PROTECTED_(\d{3,})\}\}", RegexOptions.Compiled);

        public IReadOnlyList<ProtectedValue> Values => _values;

        /// <summary>
        /// Registers a value and returns its marker. The same value asked for
        /// twice gets the same marker, so a company name appearing in four
        /// fields is one entry the model has to respect rather than four.
        /// </summary>
        public string Protect(string value, ProtectedValueKind kind)
        {
            var trimmed = (value ?? "").Trim();

            if (trimmed.Length == 0) return "";

            var existing = _values.FirstOrDefault(
                v => string.Equals(v.Value, trimmed, StringComparison.Ordinal));

            if (existing is not null) return existing.Token;

            var token = "{{PROTECTED_" + _next.ToString("000", CultureInfo.InvariantCulture) + "}}";
            _next++;

            _values.Add(new ProtectedValue(token, trimmed, kind));

            return token;
        }

        /// <summary>
        /// The text with every registered value replaced by its marker.
        ///
        /// Longest first, so registering both "Netwitcher UG (haftungsbeschränkt)"
        /// and "Netwitcher" cannot leave the longer one half-replaced with the
        /// shorter one's marker embedded in it.
        /// </summary>
        public string Mask(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";

            var masked = text;

            foreach (var value in _values.OrderByDescending(v => v.Value.Length))
                masked = masked.Replace(value.Value, value.Token, StringComparison.Ordinal);

            return masked;
        }

        /// <summary>
        /// How many times each marker appears in a piece of text.
        /// </summary>
        public static IReadOnlyDictionary<string, int> Count(string? text)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);

            if (string.IsNullOrEmpty(text)) return counts;

            foreach (Match match in TokenPattern.Matches(text))
                counts[match.Value] = counts.GetValueOrDefault(match.Value) + 1;

            return counts;
        }

        /// <summary>
        /// Whether the answer carries exactly the markers the request did.
        ///
        /// Not "at least": a marker the model repeated puts a company name in the
        /// contract twice, and one it dropped removes it. Both are rejections.
        /// </summary>
        public static bool Matches(
            string? sent, string? returned, out string difference)
        {
            var before = Count(sent);
            var after = Count(returned);

            foreach (var (token, count) in before)
            {
                var got = after.GetValueOrDefault(token);

                if (got == count) continue;

                difference = got == 0
                    ? $"Der geschützte Wert {token} fehlt in der Übersetzung."
                    : $"Der geschützte Wert {token} kommt {got}× statt {count}× vor.";

                return false;
            }

            foreach (var token in after.Keys)
            {
                if (before.ContainsKey(token)) continue;

                difference = $"Die Übersetzung enthält den unbekannten Platzhalter {token}.";
                return false;
            }

            difference = "";
            return true;
        }

        /// <summary>
        /// The text with every marker put back to the value it stood for.
        /// Only called once <see cref="Matches"/> has said the markers survived.
        /// </summary>
        public string Restore(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";

            var restored = text;

            foreach (var value in _values)
                restored = restored.Replace(value.Token, value.Value, StringComparison.Ordinal);

            return restored;
        }

        /// <summary>
        /// Whether any marker is still in the text. Used on the finished
        /// document: a marker that reaches a PDF is internal machinery printed in
        /// a contract, which is worse than the translation it was protecting.
        /// </summary>
        public static bool ContainsToken(string? text) =>
            !string.IsNullOrEmpty(text) && TokenPattern.IsMatch(text);
    }
}
