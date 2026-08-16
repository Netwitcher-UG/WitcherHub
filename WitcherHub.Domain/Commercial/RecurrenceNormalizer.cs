namespace WitcherHub.Domain.Commercial
{
    /// <summary>
    /// Turns a stated repetition into the domain's own vocabulary.
    ///
    /// The system stores what a phrase *means*, not the phrase. "monatlich",
    /// "monthly", "per month" and "每月" are one billing concept, and storing the
    /// wording as the type would give four types that no calculation can use.
    ///
    /// This is normalisation, not interpretation. It recognises how periods are
    /// written across languages and returns Unknown for everything else, keeping
    /// the original phrase so a person can supply the meaning. It deliberately
    /// contains no knowledge of any particular contract, customer or service:
    /// the vocabulary here is calendar vocabulary, which is the same for every
    /// agreement anyone will ever paste in.
    ///
    /// Nothing downstream depends on this succeeding. A phrase it does not know
    /// leaves the recurrence Unknown, which the financial engine reports rather
    /// than guessing around.
    /// </summary>
    public static class RecurrenceNormalizer
    {
        /// <summary>
        /// Period words in the languages the system is likely to meet, mapped to
        /// the unit and the multiplier they mean. Extending this list is adding
        /// vocabulary, not adding a business rule.
        /// </summary>
        private static readonly (string[] Words, PeriodUnit Unit, int Interval)[] Periods =
        {
            (new[] { "daily", "täglich", "taeglich", "per day", "pro tag", "je tag", "quotidien" },
                PeriodUnit.Day, 1),

            (new[] { "weekly", "wöchentlich", "woechentlich", "per week", "pro woche", "je woche", "hebdomadaire" },
                PeriodUnit.Week, 1),

            (new[] { "fortnightly", "biweekly", "zweiwöchentlich", "alle zwei wochen", "every two weeks", "every 2 weeks" },
                PeriodUnit.Week, 2),

            (new[] { "monthly", "monatlich", "per month", "pro monat", "je monat", "p.m.", "mensuel" },
                PeriodUnit.Month, 1),

            (new[] { "bimonthly", "zweimonatlich", "alle zwei monate", "every two months", "every 2 months" },
                PeriodUnit.Month, 2),

            (new[] { "quarterly", "vierteljährlich", "vierteljaehrlich", "quartalsweise", "per quarter",
                     "pro quartal", "trimestriel" },
                PeriodUnit.Quarter, 1),

            (new[] { "semiannual", "semi-annual", "halbjährlich", "halbjaehrlich", "twice a year",
                     "zweimal jährlich", "every six months", "alle sechs monate" },
                PeriodUnit.Month, 6),

            (new[] { "annually", "annual", "yearly", "jährlich", "jaehrlich", "per year", "pro jahr",
                     "je jahr", "p.a.", "annuel" },
                PeriodUnit.Year, 1)
        };

        private static readonly string[] OneOffWords =
        {
            "one-time", "one time", "once", "single", "einmalig", "einmalige", "einmal",
            "pauschal", "flat fee", "lump sum", "unique"
        };

        private static readonly string[] UsageWords =
        {
            "per use", "per usage", "pro nutzung", "je nutzung", "usage-based", "usage based",
            "nutzungsabhängig", "nutzungsabhaengig", "verbrauchsabhängig", "as consumed",
            "nach aufwand", "pro einheit", "per unit consumed"
        };

        private static readonly string[] MilestoneWords =
        {
            "milestone", "meilenstein", "per milestone", "je meilenstein", "on completion",
            "nach abschluss", "bei erreichen"
        };

        /// <summary>
        /// The best reading of a phrase, always carrying the phrase itself.
        /// Returns Unknown rather than a plausible default when it does not know.
        /// </summary>
        public static Recurrence Normalize(string? phrase)
        {
            if (string.IsNullOrWhiteSpace(phrase))
                return Recurrence.Unknown();

            var text = phrase.Trim().ToLowerInvariant();

            // An explicit interval — "every 3 months", "alle 2 wochen" — before the
            // single-word forms, so the multiplier is not lost.
            var explicitInterval = ReadExplicitInterval(text, phrase);
            if (explicitInterval is not null) return explicitInterval;

            foreach (var (words, unit, interval) in Periods)
            {
                if (words.Any(w => text.Contains(w, StringComparison.Ordinal)))
                    return Recurrence.Every(unit, interval, phrase);
            }

            if (UsageWords.Any(w => text.Contains(w, StringComparison.Ordinal)))
                return Recurrence.PerUsage(sourcePhrase: phrase);

            if (MilestoneWords.Any(w => text.Contains(w, StringComparison.Ordinal)))
                return Recurrence.PerMilestone(sourcePhrase: phrase);

            if (OneOffWords.Any(w => text.Contains(w, StringComparison.Ordinal)))
                return Recurrence.Once(phrase);

            // Recognisably a condition, but not one this knows how to count.
            return Recurrence.Unknown(phrase);
        }

        private static Recurrence? ReadExplicitInterval(string text, string original)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                text,
                @"(?:every|alle|all|each|je|pro)\s+(\d+)\s*(day|days|tag|tage|tagen|week|weeks|woche|wochen|month|months|monat|monate|monaten|quarter|quarters|quartal|quartale|year|years|jahr|jahre|jahren)");

            if (!match.Success) return null;
            if (!int.TryParse(match.Groups[1].Value, out var interval) || interval <= 0) return null;

            var unit = match.Groups[2].Value switch
            {
                "day" or "days" or "tag" or "tage" or "tagen" => PeriodUnit.Day,
                "week" or "weeks" or "woche" or "wochen" => PeriodUnit.Week,
                "month" or "months" or "monat" or "monate" or "monaten" => PeriodUnit.Month,
                "quarter" or "quarters" or "quartal" or "quartale" => PeriodUnit.Quarter,
                "year" or "years" or "jahr" or "jahre" or "jahren" => PeriodUnit.Year,
                _ => (PeriodUnit?)null
            };

            return unit is null ? null : Recurrence.Every(unit.Value, interval, original);
        }

        /// <summary>
        /// The pricing basis a phrase describes, when it describes one clearly.
        /// Unknown otherwise — the caller keeps the phrase and asks a person.
        /// </summary>
        public static PricingModelKind NormalizePricingModel(string? phrase)
        {
            if (string.IsNullOrWhiteSpace(phrase)) return PricingModelKind.Unknown;

            var text = phrase.Trim().ToLowerInvariant();

            if (Contains(text, "no charge", "kostenlos", "free of charge", "unentgeltlich", "gratis"))
                return PricingModelKind.NoCharge;

            if (Contains(text, "credit", "gutschrift", "rebate", "refund", "erstattung"))
                return PricingModelKind.Credit;

            if (Contains(text, "per hour", "hourly", "pro stunde", "je stunde", "stundensatz",
                               "per day", "daily rate", "tagessatz", "time and materials", "nach aufwand"))
                return PricingModelKind.TimeAndMaterials;

            if (Contains(text, "usage", "consumption", "verbrauch", "nutzungsabhängig", "nutzungsabhaengig"))
                return PricingModelKind.UsageBased;

            if (Contains(text, "tier", "staffel", "volume discount", "mengenstaffel"))
                return PricingModelKind.Tiered;

            if (Contains(text, "percent", "prozent", "%", "commission", "provision"))
                return PricingModelKind.Percentage;

            if (Contains(text, "milestone", "meilenstein", "on completion", "nach abschluss"))
                return PricingModelKind.Milestone;

            if (Contains(text, "per unit", "pro einheit", "je einheit", "per piece", "pro stück", "pro stueck"))
                return PricingModelKind.PerUnit;

            if (Contains(text, "recurring", "subscription", "abonnement", "wiederkehrend", "laufend"))
                return PricingModelKind.RecurringAmount;

            if (Contains(text, "fixed", "pauschal", "flat", "festpreis", "lump sum"))
                return PricingModelKind.FixedAmount;

            return PricingModelKind.Unknown;
        }

        private static bool Contains(string text, params string[] needles) =>
            needles.Any(n => text.Contains(n, StringComparison.Ordinal));
    }
}
