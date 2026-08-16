using System.Globalization;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Domain.Commercial;

namespace WitcherHub.Application.Services.Contracts
{
    /// <summary>
    /// Turns what the analyser answered into the domain's own model.
    ///
    /// The boundary between a tolerant shape and a strict one. Everything the
    /// analyser sends is text or a nullable number, because a model that cannot
    /// express a value must be able to say what it saw; everything the domain
    /// holds is typed, because arithmetic needs types. This is where the one
    /// becomes the other, and where anything that will not convert is recorded as
    /// an open question rather than forced.
    ///
    /// Two rules run through all of it. Nothing is invented: a value that does
    /// not convert stays null and says why. Nothing is normalised destructively:
    /// the phrase that produced a normalised value is kept beside it.
    /// </summary>
    public static class ProposedTermMapper
    {
        public static IReadOnlyList<CommercialTerm> ToDomain(
            IEnumerable<ProposedTermDto> proposals, string? fallbackCurrency = null)
        {
            ArgumentNullException.ThrowIfNull(proposals);

            return proposals.Select(p => ToDomain(p, fallbackCurrency)).ToList();
        }

        public static CommercialTerm ToDomain(ProposedTermDto p, string? fallbackCurrency = null)
        {
            ArgumentNullException.ThrowIfNull(p);

            var questions = new List<string>(p.OpenQuestions);
            var currency = NormaliseCurrency(p.Currency) ?? fallbackCurrency;

            var billing = RecurrenceNormalizer.Normalize(p.BillingRecurrence);
            var delivery = RecurrenceNormalizer.Normalize(p.DeliveryRecurrence);

            // A stated recurrence that does not normalise is not silently dropped:
            // the phrase is kept on the Recurrence and asked about here.
            if (billing.Kind is RecurrenceKind.Unknown && !string.IsNullOrWhiteSpace(p.BillingRecurrence))
            {
                questions.Add(
                    $"The billing frequency is stated as \"{p.BillingRecurrence}\", which could not be read as a " +
                    "period. Set it so the totals can be calculated.");
            }

            var pricingModel = ReadPricingModel(p.PricingModel, out var customModel);

            if (pricingModel is PricingModelKind.Unknown && !string.IsNullOrWhiteSpace(p.PricingModel))
            {
                // Recorded as a custom arrangement rather than approximated to the
                // nearest familiar one, which would misstate what was agreed.
                pricingModel = PricingModelKind.Custom;
                customModel = p.PricingModel;

                questions.Add(
                    $"The pricing is described as \"{p.PricingModel}\", which does not match a known basis. " +
                    "It is recorded as stated and not totalled automatically.");
            }

            var commitment = ReadCommitment(p.Commitment);

            // The judgement that governs the contract value. Asked for explicitly,
            // and left Unknown when the answer was not usable — never defaulted to
            // Committed, which would put an unagreed number into the total.
            if (commitment is Commitment.Unknown && (p.UnitRate is not null || p.FixedAmount is not null))
            {
                questions.Add(
                    "Whether this amount is actually committed was not established, so it does not count " +
                    "towards the contract value yet.");
            }

            var start = ReadDate(p.StartDate, out var startProblem);
            var end = ReadDate(p.EndDate, out var endProblem);

            if (startProblem is not null) questions.Add($"Start date: {startProblem}");
            if (endProblem is not null) questions.Add($"End date: {endProblem}");

            var provenance = new Provenance(
                p.SourceSnippet,
                Confidence: p.Confidence,
                Reasoning: p.Reasoning,
                IsAmbiguous: p.IsAmbiguous,
                Ambiguity: p.Ambiguity).Clamped();

            return new CommercialTerm
            {
                Key = string.IsNullOrWhiteSpace(p.Key) ? Guid.NewGuid().ToString("n") : p.Key,
                Name = (p.Name ?? "").Trim(),
                Description = Blank(p.Description),
                Category = Blank(p.Category),

                PricingModel = pricingModel,
                CustomPricingModel = Blank(customModel),

                Quantity = p.Quantity,
                QuantityUnit = Blank(p.QuantityUnit),

                UnitRate = new MoneyAmount(p.UnitRate, currency, commitment),
                FixedAmount = p.FixedAmount is null ? null : new MoneyAmount(p.FixedAmount, currency, commitment),

                BillingRecurrence = billing,
                DeliveryRecurrence = delivery,
                PaymentSchedule = Blank(p.PaymentSchedule),

                StartDate = start,
                EndDate = end,
                DurationCount = p.DurationCount,
                DurationUnit = ReadPeriodUnit(p.DurationUnit),

                Phases = p.Phases
                    .OrderBy(x => x.Sequence)
                    .Select((x, index) => ToDomain(x, index, currency))
                    .ToList(),

                MinimumCommitment = p.MinimumCommitment is null
                    ? null
                    : new MoneyAmount(p.MinimumCommitment, currency, Commitment.Committed),

                Cap = p.Cap is null ? null : new MoneyAmount(p.Cap, currency),

                DiscountPercent = p.DiscountPercent,
                DiscountAmount = p.DiscountAmount is null ? null : new MoneyAmount(p.DiscountAmount, currency),

                TaxRatePercent = p.TaxRatePercent,
                TaxTreatment = Blank(p.TaxTreatment),

                IsMandatory = p.IsMandatory ?? true,
                Conditions = Blank(p.Conditions),
                Notes = Blank(p.Notes),

                Commitment = commitment,
                Provenance = provenance,
                OpenQuestions = questions
            };
        }

        private static PricingPhase ToDomain(ProposedPhaseDto p, int index, string? currency)
        {
            var model = ReadPricingModel(p.PricingModel, out var custom);

            if (model is PricingModelKind.Unknown && !string.IsNullOrWhiteSpace(p.PricingModel))
            {
                model = PricingModelKind.Custom;
                custom = p.PricingModel;
            }

            return new PricingPhase
            {
                Label = Blank(p.Label),
                Sequence = p.Sequence == 0 ? index + 1 : p.Sequence,

                StartDate = ReadDate(p.StartDate, out _),
                EndDate = ReadDate(p.EndDate, out _),
                StartCondition = Blank(p.StartCondition),
                EndCondition = Blank(p.EndCondition),

                DurationCount = p.DurationCount,
                DurationUnit = ReadPeriodUnit(p.DurationUnit),

                PricingModel = model,
                CustomPricingModel = Blank(custom),

                Rate = new MoneyAmount(p.Rate, NormaliseCurrency(p.Currency) ?? currency),
                Quantity = p.Quantity,
                QuantityUnit = Blank(p.QuantityUnit),

                BillingRecurrence = RecurrenceNormalizer.Normalize(p.BillingRecurrence),

                DiscountPercent = p.DiscountPercent,
                DiscountAmount = p.DiscountAmount is null
                    ? null
                    : new MoneyAmount(p.DiscountAmount, NormaliseCurrency(p.Currency) ?? currency),

                Conditions = Blank(p.Conditions),

                Provenance = new Provenance(p.SourceSnippet, Confidence: p.Confidence).Clamped()
            };
        }

        // -------------------------------------------------------------------

        public static PricingModelKind ReadPricingModel(string? value, out string? custom)
        {
            custom = null;
            if (string.IsNullOrWhiteSpace(value)) return PricingModelKind.Unknown;

            // The enum's own names first, so a well-behaved answer round-trips.
            if (Enum.TryParse<PricingModelKind>(value.Replace(" ", ""), ignoreCase: true, out var parsed))
            {
                if (parsed is PricingModelKind.Custom) custom = value;
                return parsed;
            }

            return RecurrenceNormalizer.NormalizePricingModel(value);
        }

        public static Commitment ReadCommitment(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Commitment.Unknown;

            if (Enum.TryParse<Commitment>(value.Replace(" ", ""), ignoreCase: true, out var parsed))
                return parsed;

            var text = value.Trim().ToLowerInvariant();

            if (text.Contains("commit") || text.Contains("fest") || text.Contains("verbindlich") || text.Contains("agreed"))
                return Commitment.Committed;

            if (text.Contains("estimat") || text.Contains("schätz") || text.Contains("schaetz") || text.Contains("ca."))
                return Commitment.Estimated;

            if (text.Contains("variab") || text.Contains("usage") || text.Contains("aufwand"))
                return Commitment.Variable;

            if (text.Contains("option") || text.Contains("optional"))
                return Commitment.Optional;

            if (text.Contains("condition") || text.Contains("bedingt") || text.Contains("sofern"))
                return Commitment.Conditional;

            return Commitment.Unknown;
        }

        internal static PeriodUnit? ReadPeriodUnit(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            if (Enum.TryParse<PeriodUnit>(value, ignoreCase: true, out var parsed)) return parsed;

            var normalised = RecurrenceNormalizer.Normalize("every 1 " + value.Trim());
            return normalised.Unit;
        }

        /// <summary>
        /// Reads a date written the way documents write them, and reports rather
        /// than guesses when it cannot. An unreadable date must not become a real
        /// one — a contract that starts on a date nobody agreed is worse than a
        /// contract with a missing start date.
        /// </summary>
        public static DateOnly? ReadDate(string? value, out string? problem)
        {
            problem = null;
            if (string.IsNullOrWhiteSpace(value)) return null;

            var text = value.Trim();

            string[] formats =
            {
                "yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "MM/dd/yyyy",
                "yyyy/MM/dd", "d MMMM yyyy", "MMMM d, yyyy"
            };

            foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("de-DE") })
            {
                if (DateOnly.TryParseExact(text, formats, culture, DateTimeStyles.None, out var exact))
                    return exact;

                if (DateOnly.TryParse(text, culture, DateTimeStyles.None, out var loose))
                    return loose;
            }

            problem = $"\"{text}\" could not be read as a date, so it has been left unset.";
            return null;
        }

        /// <summary>
        /// A three-letter code, or nothing. Storing "Euro" in a field the rest of
        /// the system formats as a code breaks every amount after it.
        /// </summary>
        public static string? NormaliseCurrency(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var trimmed = value.Trim();

            var known = trimmed.ToUpperInvariant() switch
            {
                "EUR" or "EURO" or "€" => "EUR",
                "USD" or "US$" or "$" or "DOLLAR" => "USD",
                "GBP" or "£" or "POUND" => "GBP",
                "CHF" or "FRANKEN" => "CHF",
                _ => null
            };

            if (known is not null) return known;

            return trimmed.Length == 3 && trimmed.All(char.IsLetter)
                ? trimmed.ToUpperInvariant()
                : null;
        }

        private static string? Blank(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
