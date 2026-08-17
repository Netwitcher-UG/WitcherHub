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

        /// <summary>
        /// For headline figures, where the cents are noise: "1.235 €".
        ///
        /// Rounds half away from zero, which is what a reader expects of a figure on
        /// a dashboard tile. <see cref="Math.Round(decimal, int)"/> on its own rounds
        /// half to even — nobody chose that here, it is the default — and it meant
        /// 1.234,50 was displayed as "1.234", which looks like the cents had been
        /// cut off rather than rounded. Display only: nothing is calculated from this.
        /// </summary>
        public static string MoneyCompact(decimal amount, string currency = "EUR") =>
            $"{Math.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("N0", German)} {Symbol(currency)}";

        /// <summary>
        /// An amount with no currency symbol, for table columns where the currency
        /// is already stated once in the header: 1234.5 → "1.234,50".
        ///
        /// This exists so those columns stop calling <c>ToString("0.00")</c>, which
        /// formats in the request culture — and the default request culture here is
        /// English. A German invoice line was rendering as "1234.50": a dot for a
        /// decimal comma, and no group separator at all, next to totals elsewhere
        /// on the same screen that came out as "1.234,50 €". Which shape you got
        /// depended on which helper the page happened to call, and switching the UI
        /// language changed the figures under you.
        /// </summary>
        public static string Amount(decimal amount) => amount.ToString("N2", German);

        public static string Amount(decimal? amount) => amount is null ? "—" : Amount(amount.Value);

        /// <summary>
        /// A quantity: whole where it is whole, up to two decimals where it is not,
        /// and German separators either way. 2 → "2", 2.5 → "2,5".
        /// </summary>
        public static string Quantity(decimal quantity) => quantity.ToString("0.##", German);

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
        /// The value for an <c>&lt;input type="date"&gt;</c>, which HTML defines as
        /// ISO <c>yyyy-MM-dd</c> regardless of the user's locale — the browser
        /// handles displaying it in local form.
        ///
        /// Deliberately not <see cref="Date(DateTimeOffset?)"/>: passing a German
        /// "31.12.2025" to a date input makes the browser discard it and show an
        /// empty field, so a date the user had already saved silently disappears the
        /// next time they open the form. Named for where it goes so the two are not
        /// confused, and empty rather than "—" because that is the input's own empty
        /// value.
        /// </summary>
        public static string DateInput(DateTimeOffset? moment) =>
            moment?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

        public static string DateInput(DateOnly? date) =>
            date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

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
