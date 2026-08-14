using System.Globalization;

namespace WitcherHub.Pages.Models.UI
{
    /// <summary>
    /// Presentation helpers for money and dates.
    ///
    /// Amounts are formatted with the German group and decimal separators
    /// regardless of the request culture, because these are German business
    /// documents and the figure on the screen has to match the figure on the
    /// invoice. The application already accepts German decimal input on the way in
    /// (see FlexibleDecimalModelBinder); this is the same decision on the way out.
    /// </summary>
    public static class Format
    {
        private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

        /// <summary>1234.5 → "1.234,50 €"</summary>
        public static string Money(decimal amount, string currency = "EUR") =>
            $"{amount.ToString("N2", German)} {Symbol(currency)}";

        /// <summary>For headline figures, where the cents are noise: "1.235 €".</summary>
        public static string MoneyCompact(decimal amount, string currency = "EUR") =>
            $"{Math.Round(amount, 0).ToString("N0", German)} {Symbol(currency)}";

        public static string Symbol(string? currency) => (currency ?? "EUR").ToUpperInvariant() switch
        {
            "EUR" => "€",
            "USD" => "$",
            "GBP" => "£",
            "CHF" => "CHF",
            var other => other
        };

        public static string Date(DateOnly? date) =>
            date is null ? "—" : date.Value.ToString("dd.MM.yyyy", German);

        public static string Date(DateTimeOffset? moment) =>
            moment is null ? "—" : moment.Value.ToLocalTime().ToString("dd.MM.yyyy", German);

        public static string DateTime(DateTimeOffset? moment) =>
            moment is null ? "—" : moment.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", German);

        /// <summary>
        /// "today", "3 days ago", "in 12 days". Used where the age of something is
        /// the point — a quote nobody has answered, an invoice past its due date.
        /// </summary>
        public static string Relative(DateOnly? date)
        {
            if (date is null) return "—";

            var days = date.Value.DayNumber - DateOnly.FromDateTime(System.DateTime.UtcNow).DayNumber;

            return days switch
            {
                0 => "today",
                1 => "tomorrow",
                -1 => "yesterday",
                > 1 => $"in {days} days",
                _ => $"{-days} days ago"
            };
        }

        public static string Relative(DateTimeOffset? moment) =>
            moment is null ? "—" : Relative(DateOnly.FromDateTime(moment.Value.UtcDateTime));

        /// <summary>
        /// How many days late something is, or null when it is not late. Kept
        /// separate from <see cref="Relative(DateOnly?)"/> so a caller can decide
        /// to colour the row rather than only word it.
        /// </summary>
        public static int? DaysOverdue(DateOnly? due)
        {
            if (due is null) return null;

            var days = DateOnly.FromDateTime(System.DateTime.UtcNow).DayNumber - due.Value.DayNumber;
            return days > 0 ? days : null;
        }
    }
}
