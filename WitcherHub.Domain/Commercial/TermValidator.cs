namespace WitcherHub.Domain.Commercial
{
    public enum ValidationSeverity
    {
        /// <summary>Worth a person's attention; the value is still usable.</summary>
        Information = 0,

        /// <summary>Usable but incomplete, or internally odd.</summary>
        Warning = 1,

        /// <summary>Contradictory or impossible. The term is kept and flagged.</summary>
        Problem = 2
    }

    public sealed record TermIssue(
        string TermKey,
        string Field,
        ValidationSeverity Severity,
        string Message);

    public sealed record ValidationOutcome(
        IReadOnlyList<CommercialTerm> Terms,
        IReadOnlyList<TermIssue> Issues,
        IReadOnlyList<string> DiscardedReasons)
    {
        public bool HasProblems => Issues.Any(i => i.Severity is ValidationSeverity.Problem);

        public IReadOnlyList<TermIssue> For(string termKey) =>
            Issues.Where(i => i.TermKey == termKey).ToList();
    }

    /// <summary>
    /// Checks proposed terms without throwing any of them away.
    ///
    /// A response with one bad field is not a useless response. Discarding a
    /// whole extraction because a date could not be parsed means a person
    /// re-types everything that was right, so problems are recorded against the
    /// term and the term survives. Only something with no usable content at all
    /// is dropped, and the reason is reported.
    ///
    /// Nothing here is corrected automatically. A contradiction between two
    /// stated figures is a question for a person, not something to resolve by
    /// preferring one of them.
    /// </summary>
    public static class TermValidator
    {
        public static ValidationOutcome Validate(IReadOnlyCollection<CommercialTerm> terms)
        {
            ArgumentNullException.ThrowIfNull(terms);

            var issues = new List<TermIssue>();
            var kept = new List<CommercialTerm>();
            var discarded = new List<string>();
            var seen = new List<CommercialTerm>();

            foreach (var term in terms)
            {
                if (IsEmpty(term))
                {
                    discarded.Add("A proposed term had no name, no amount and no description, so there was nothing to review.");
                    continue;
                }

                var key = term.Key;

                if (string.IsNullOrWhiteSpace(term.Name))
                {
                    issues.Add(new TermIssue(key, nameof(term.Name), ValidationSeverity.Warning,
                        "This term has no name. It is kept so the figures are not lost — give it one before approving."));
                }

                // Money that is stated as a rate without anything to multiply it by.
                if (term.UnitRate.HasValue && term.Quantity is null && term.FixedAmount is null &&
                    term.PricingModel is PricingModelKind.PerUnit or PricingModelKind.TimeAndMaterials
                                       or PricingModelKind.UsageBased)
                {
                    issues.Add(new TermIssue(key, nameof(term.Quantity), ValidationSeverity.Information,
                        "A rate is stated but no quantity, so this cannot be totalled. That may be correct for " +
                        "usage-based work — confirm the quantity if one was agreed."));
                }

                if (term.PricingModel is PricingModelKind.Custom && string.IsNullOrWhiteSpace(term.CustomPricingModel))
                {
                    issues.Add(new TermIssue(key, nameof(term.CustomPricingModel), ValidationSeverity.Warning,
                        "The pricing is marked as custom but not described, so nothing records what was agreed."));
                }

                if (term.PricingModel is PricingModelKind.NoCharge &&
                    (term.UnitRate.HasValue || term.FixedAmount?.HasValue == true))
                {
                    issues.Add(new TermIssue(key, nameof(term.PricingModel), ValidationSeverity.Problem,
                        "This is marked as free of charge but carries an amount. One of the two is wrong."));
                }

                if (term.StartDate is { } start && term.EndDate is { } end && end < start)
                {
                    issues.Add(new TermIssue(key, nameof(term.EndDate), ValidationSeverity.Problem,
                        "The end date is before the start date. Both are kept as read — correct whichever is wrong."));
                }

                if (term.BillingRecurrence.IsRecurring && term.BillingRecurrence.IsCountable &&
                    term.LengthInMonths() is null)
                {
                    issues.Add(new TermIssue(key, "Duration", ValidationSeverity.Information,
                        "This recurs but has no end date or duration, so a contract total cannot be calculated " +
                        "from it. The monthly amount is shown instead."));
                }

                if (term.Commitment is Commitment.Unknown &&
                    (term.UnitRate.HasValue || term.FixedAmount?.HasValue == true))
                {
                    issues.Add(new TermIssue(key, nameof(term.Commitment), ValidationSeverity.Warning,
                        "Whether this amount is actually committed has not been established, so it is not counted " +
                        "in the contract value."));
                }

                if (term.MinimumCommitment?.Value is { } minimum && term.Cap?.Value is { } cap && cap < minimum)
                {
                    issues.Add(new TermIssue(key, nameof(term.Cap), ValidationSeverity.Problem,
                        "The cap is below the minimum commitment, which cannot both be true."));
                }

                ValidatePhases(term, issues);

                // Two terms that look like the same thing. Not merged: whether a
                // repeated charge is one term or two is a judgement about the
                // agreement, and guessing wrong changes the total either way.
                var duplicate = seen.FirstOrDefault(t =>
                    !string.IsNullOrWhiteSpace(t.Name) &&
                    string.Equals(t.Name.Trim(), term.Name?.Trim(), StringComparison.OrdinalIgnoreCase));

                if (duplicate is not null)
                {
                    issues.Add(new TermIssue(key, nameof(term.Name), ValidationSeverity.Warning,
                        $"Another term is also called \"{term.Name}\". Both are kept — merge them if they are the same charge."));
                }

                seen.Add(term);
                kept.Add(term);
            }

            return new ValidationOutcome(kept, issues, discarded);
        }

        private static void ValidatePhases(CommercialTerm term, List<TermIssue> issues)
        {
            if (!term.HasPhases) return;

            var ordered = term.Phases.OrderBy(p => p.Sequence).ToList();

            for (var i = 0; i < ordered.Count; i++)
            {
                var phase = ordered[i];
                var label = phase.Label ?? $"phase {i + 1}";

                if (!phase.Rate.HasValue && phase.PricingModel is not PricingModelKind.NoCharge)
                {
                    issues.Add(new TermIssue(term.Key, "Phases", ValidationSeverity.Warning,
                        $"The pricing for {label} has no amount, so that period cannot be totalled."));
                }

                if (phase.StartDate is { } start && phase.EndDate is { } end && end < start)
                {
                    issues.Add(new TermIssue(term.Key, "Phases", ValidationSeverity.Problem,
                        $"{label} ends before it starts."));
                }

                if (phase.LengthInMonths() is null &&
                    phase.BillingRecurrence.IsCountable &&
                    i < ordered.Count - 1)
                {
                    // Only the final phase may be open-ended; an unbounded phase in
                    // the middle leaves the ones after it undated too.
                    issues.Add(new TermIssue(term.Key, "Phases", ValidationSeverity.Warning,
                        $"{label} has no stated length but is followed by another phase, so when it ends is unclear."));
                }
            }

            var overlapping = ordered
                .Where(p => p.StartDate is not null && p.EndDate is not null)
                .OrderBy(p => p.StartDate)
                .ToList();

            for (var i = 1; i < overlapping.Count; i++)
            {
                if (overlapping[i].StartDate <= overlapping[i - 1].EndDate)
                {
                    issues.Add(new TermIssue(term.Key, "Phases", ValidationSeverity.Warning,
                        "Two pricing periods overlap. Both are kept as read — check which price applies when."));
                    break;
                }
            }
        }

        /// <summary>
        /// Nothing to show and nothing to correct. Anything with a name, a
        /// description or an amount is worth keeping.
        /// </summary>
        private static bool IsEmpty(CommercialTerm term) =>
            string.IsNullOrWhiteSpace(term.Name) &&
            string.IsNullOrWhiteSpace(term.Description) &&
            !term.UnitRate.HasValue &&
            term.FixedAmount?.HasValue != true &&
            term.Phases.Count == 0;
    }
}
