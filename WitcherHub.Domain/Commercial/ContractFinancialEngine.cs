namespace WitcherHub.Domain.Commercial
{
    /// <summary>Why an amount could not be resolved into a committed figure.</summary>
    public sealed record UnresolvedAmount(string TermKey, string TermName, string Reason);

    /// <summary>
    /// What a set of commercial terms adds up to, kept in separate buckets.
    ///
    /// One number cannot honestly describe a contract that mixes a fixed fee, an
    /// hourly rate with no committed hours, and an optional add-on. Collapsing
    /// them produces a total the contract does not state, so they stay apart and
    /// the screen shows each for what it is.
    /// </summary>
    public sealed record ContractFinancials
    {
        public string Currency { get; init; } = "";

        /// <summary>Agreed, calculable, one-off.</summary>
        public decimal CommittedOneTime { get; init; }

        /// <summary>Agreed, calculable, across the whole term.</summary>
        public decimal CommittedRecurringTotal { get; init; }

        /// <summary>Agreed recurring charge normalised to one month, for comparison.</summary>
        public decimal CommittedMonthlyEquivalent { get; init; }

        /// <summary>Given as an expectation rather than a promise.</summary>
        public decimal Estimated { get; init; }

        /// <summary>Depends on usage, hours or volume that is not fixed.</summary>
        public decimal VariableRateTotal { get; init; }

        /// <summary>Only charged if taken up.</summary>
        public decimal Optional { get; init; }

        /// <summary>Reductions applied to committed amounts.</summary>
        public decimal Discounts { get; init; }

        /// <summary>
        /// Tax on the committed amounts, and only where a rate is actually
        /// stated. A term whose agreement says "plus statutory VAT" without
        /// naming a rate contributes nothing here and appears in
        /// <see cref="Unresolved"/> instead.
        /// </summary>
        public decimal CommittedTax { get; init; }

        /// <summary>
        /// The number that can be defended: committed one-off plus committed
        /// recurring, net of discounts, before tax.
        /// </summary>
        public decimal CommittedNet => CommittedOneTime + CommittedRecurringTotal;

        public decimal CommittedGross => CommittedNet + CommittedTax;

        /// <summary>
        /// Terms that carry money but could not be turned into a committed
        /// figure, each with the reason. Never silently dropped and never
        /// silently added.
        /// </summary>
        public IReadOnlyList<UnresolvedAmount> Unresolved { get; init; } = Array.Empty<UnresolvedAmount>();

        /// <summary>
        /// True when at least one term's money could not be resolved, so the
        /// committed total is a floor rather than the whole picture.
        /// </summary>
        public bool IsPartial => Unresolved.Count > 0;

        /// <summary>
        /// True when nothing at all could be committed. Distinct from a total of
        /// zero, which would be a claim that the contract is free.
        /// </summary>
        public bool HasNoCommittedValue => CommittedNet == 0m && Unresolved.Count > 0;
    }

    /// <summary>
    /// Turns commercial terms into money.
    ///
    /// Deterministic and pure: same terms in, same figures out, no clock, no
    /// database, no model. The assistant's job is to work out what the agreement
    /// means; arithmetic is not a thing to ask a language model for, because a
    /// total that cannot be reproduced cannot be defended.
    ///
    /// The engine adds only what it can justify. Anything else is reported as
    /// unresolved with its reason rather than approximated, dropped, or counted
    /// as zero.
    /// </summary>
    public static class ContractFinancialEngine
    {
        public static ContractFinancials Calculate(
            IReadOnlyCollection<CommercialTerm> terms,
            string fallbackCurrency = "EUR",
            int? contractMonths = null)
        {
            ArgumentNullException.ThrowIfNull(terms);

            var unresolved = new List<UnresolvedAmount>();

            decimal committedOneTime = 0m;
            decimal committedRecurring = 0m;
            decimal committedMonthly = 0m;
            decimal estimated = 0m;
            decimal variable = 0m;
            decimal optional = 0m;
            decimal discounts = 0m;
            decimal tax = 0m;

            var currency = terms
                .Select(t => t.UnitRate.Currency ?? t.FixedAmount?.Currency)
                .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))
                ?? fallbackCurrency;

            foreach (var term in terms)
            {
                var evaluation = Evaluate(term, contractMonths);

                if (evaluation.Reason is not null)
                {
                    unresolved.Add(new UnresolvedAmount(term.Key, term.Name, evaluation.Reason));

                    // A term can be both partly resolvable and partly not — a
                    // committed setup fee alongside an uncommitted hourly rate.
                    // The resolvable part still counts.
                }

                // Where a term is not mandatory, its money is an option however
                // firmly the rate is stated.
                var commitment = term.IsMandatory ? term.Commitment : Commitment.Optional;

                switch (commitment)
                {
                    case Commitment.Committed:
                        committedOneTime += evaluation.OneTime;
                        committedRecurring += evaluation.RecurringTotal;
                        committedMonthly += evaluation.MonthlyEquivalent;
                        discounts += evaluation.Discount;

                        if (term.TaxRatePercent is { } rate)
                        {
                            tax += Round((evaluation.OneTime + evaluation.RecurringTotal) * (rate / 100m));
                        }
                        else if (evaluation.OneTime + evaluation.RecurringTotal > 0m &&
                                 !string.IsNullOrWhiteSpace(term.TaxTreatment))
                        {
                            unresolved.Add(new UnresolvedAmount(
                                term.Key, term.Name,
                                "The agreement names a tax treatment but no rate, so tax on this term cannot be calculated."));
                        }
                        break;

                    case Commitment.Estimated:
                        estimated += evaluation.OneTime + evaluation.RecurringTotal;
                        break;

                    case Commitment.Optional:
                        optional += evaluation.OneTime + evaluation.RecurringTotal;
                        break;

                    case Commitment.Variable:
                    case Commitment.Conditional:
                        variable += evaluation.OneTime + evaluation.RecurringTotal;
                        break;

                    case Commitment.Unknown:
                        if (evaluation.Reason is null && (evaluation.OneTime + evaluation.RecurringTotal) > 0m)
                        {
                            unresolved.Add(new UnresolvedAmount(
                                term.Key, term.Name,
                                "How firm this amount is has not been established, so it is not counted as committed."));
                        }
                        break;
                }
            }

            return new ContractFinancials
            {
                Currency = currency,
                CommittedOneTime = Round(committedOneTime),
                CommittedRecurringTotal = Round(committedRecurring),
                CommittedMonthlyEquivalent = Round(committedMonthly),
                Estimated = Round(estimated),
                VariableRateTotal = Round(variable),
                Optional = Round(optional),
                Discounts = Round(discounts),
                CommittedTax = Round(tax),
                Unresolved = unresolved
            };
        }

        private readonly record struct Evaluation(
            decimal OneTime,
            decimal RecurringTotal,
            decimal MonthlyEquivalent,
            decimal Discount,
            string? Reason);

        /// <summary>
        /// What one term is worth. Phased terms are the sum of their phases, so
        /// a price that changes partway through is not averaged into something
        /// the agreement never said.
        /// </summary>
        private static Evaluation Evaluate(CommercialTerm term, int? contractMonths)
        {
            if (term.PricingModel is PricingModelKind.NoCharge)
                return new Evaluation(0m, 0m, 0m, 0m, null);

            if (term.HasPhases)
                return EvaluatePhases(term);

            return EvaluateFlat(
                term.PricingModel,
                term.CustomPricingModel,
                term.FixedAmount,
                term.UnitRate,
                term.Quantity,
                term.BillingRecurrence,
                term.LengthInMonths() ?? contractMonths,
                term.DiscountPercent,
                term.DiscountAmount,
                term.Name);
        }

        private static Evaluation EvaluatePhases(CommercialTerm term)
        {
            decimal oneTime = 0m, recurring = 0m, monthly = 0m, discount = 0m;
            var reasons = new List<string>();

            foreach (var phase in term.Phases.OrderBy(p => p.Sequence))
            {
                var evaluation = EvaluateFlat(
                    phase.PricingModel is PricingModelKind.Unknown ? term.PricingModel : phase.PricingModel,
                    phase.CustomPricingModel ?? term.CustomPricingModel,
                    fixedAmount: null,
                    unitRate: phase.Rate,
                    quantity: phase.Quantity ?? term.Quantity,
                    recurrence: phase.BillingRecurrence.Kind is RecurrenceKind.Unknown
                        ? term.BillingRecurrence
                        : phase.BillingRecurrence,
                    months: phase.LengthInMonths(),
                    discountPercent: phase.DiscountPercent,
                    discountAmount: phase.DiscountAmount,
                    name: phase.Label ?? term.Name);

                oneTime += evaluation.OneTime;
                recurring += evaluation.RecurringTotal;
                discount += evaluation.Discount;

                // The monthly equivalent of a phased term is the last phase's,
                // because that is the rate the contract runs at once the earlier
                // phases have passed.
                if (evaluation.MonthlyEquivalent > 0m) monthly = evaluation.MonthlyEquivalent;

                if (evaluation.Reason is not null) reasons.Add(evaluation.Reason);
            }

            return new Evaluation(
                oneTime, recurring, monthly, discount,
                reasons.Count == 0 ? null : string.Join(" ", reasons.Distinct()));
        }

        private static Evaluation EvaluateFlat(
            PricingModelKind model,
            string? customModel,
            MoneyAmount? fixedAmount,
            MoneyAmount unitRate,
            decimal? quantity,
            Recurrence recurrence,
            int? months,
            decimal? discountPercent,
            MoneyAmount? discountAmount,
            string name)
        {
            // Pricing bases that charge per something. Without a quantity there
            // is nothing to multiply, and treating a missing quantity as one is
            // inventing a commitment: "62 per sample" is not "62". The rate may
            // be perfectly definite while the amount is not knowable at all.
            if (model is PricingModelKind.UsageBased or PricingModelKind.TimeAndMaterials
                      or PricingModelKind.Tiered or PricingModelKind.Percentage
                      or PricingModelKind.Milestone or PricingModelKind.PerUnit)
            {
                if (quantity is null || !unitRate.HasValue)
                {
                    return new Evaluation(0m, 0m, 0m, 0m,
                        $"{Describe(model, customModel)} pricing without a committed quantity cannot be totalled.");
                }
            }

            if (model is PricingModelKind.Custom)
            {
                return new Evaluation(0m, 0m, 0m, 0m,
                    "A custom pricing arrangement is recorded as stated and is not totalled automatically.");
            }

            if (model is PricingModelKind.Unknown && fixedAmount is null && !unitRate.HasValue)
                return new Evaluation(0m, 0m, 0m, 0m, "No pricing basis and no amount were stated.");

            // The gross for a single occurrence.
            decimal? perOccurrence =
                fixedAmount is { Value: { } fixedValue } ? fixedValue
                : unitRate.Value is { } rate ? rate * (quantity ?? 1m)
                : null;

            if (perOccurrence is null)
                return new Evaluation(0m, 0m, 0m, 0m, "No amount was stated.");

            var gross = perOccurrence.Value;

            var discount = 0m;
            if (discountPercent is { } percent) discount += gross * (percent / 100m);
            if (discountAmount?.Value is { } amount) discount += amount;
            discount = Math.Clamp(discount, 0m, gross);

            var net = gross - discount;

            if (model is PricingModelKind.Credit)
                return new Evaluation(-net, 0m, 0m, 0m, null);

            if (!recurrence.IsRecurring)
            {
                var reason = recurrence.Kind is RecurrenceKind.Unknown
                    ? "How often this is charged was not stated; it has been counted once."
                    : null;

                return new Evaluation(net, 0m, 0m, discount, reason);
            }

            if (!recurrence.IsCountable)
            {
                return new Evaluation(0m, 0m, 0m, 0m,
                    "This recurs on usage or on a condition, so the number of occurrences is not known in advance.");
            }

            if (months is null)
            {
                var monthlyOnly = recurrence.OccurrencesInMonths(1) ?? 0m;

                return new Evaluation(0m, 0m, Round(net * monthlyOnly), 0m,
                    "This recurs but the agreement states no end date or duration, so a total cannot be calculated.");
            }

            var occurrences = recurrence.OccurrencesInMonths(months.Value);

            if (occurrences is null)
                return new Evaluation(0m, 0m, 0m, 0m, "The billing period could not be interpreted.");

            var total = net * occurrences.Value;
            var monthlyEquivalent = net * (recurrence.OccurrencesInMonths(1) ?? 0m);

            return new Evaluation(0m, Round(total), Round(monthlyEquivalent), Round(discount * occurrences.Value), null);
        }

        private static string Describe(PricingModelKind model, string? custom) =>
            model is PricingModelKind.Custom && !string.IsNullOrWhiteSpace(custom)
                ? custom!
                : model.ToString();

        private static decimal Round(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
