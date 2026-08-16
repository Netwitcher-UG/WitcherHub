namespace WitcherHub.Domain.Commercial
{
    public enum TermChangeKind
    {
        /// <summary>Analysis found something that was not there before.</summary>
        Added = 0,

        /// <summary>Analysis proposes different values for a term already present.</summary>
        Changed = 1,

        /// <summary>Analysis no longer finds a term that is present.</summary>
        Missing = 2,

        /// <summary>Present, reviewed by a person, and left exactly as it was.</summary>
        KeptReviewed = 3,

        /// <summary>Present and identical either way.</summary>
        Unchanged = 4
    }

    /// <summary>
    /// One difference between what is stored and what a fresh analysis proposes.
    /// A proposal, not an edit.
    /// </summary>
    public sealed record TermChange(
        TermChangeKind Kind,
        string Key,
        string Name,
        CommercialTerm? Existing,
        CommercialTerm? Proposed,
        IReadOnlyList<string> ChangedFields)
    {
        /// <summary>
        /// True when a person has to decide what happens.
        ///
        /// That is the case where reviewed work and a fresh proposal disagree —
        /// the proposal has been held back rather than applied, and only the user
        /// can say which is right. It also covers a reviewed term the new reading
        /// no longer finds, which has been kept.
        /// </summary>
        public bool NeedsUserDecision =>
            Existing?.IsHumanReviewed == true &&
            Kind is TermChangeKind.KeptReviewed or TermChangeKind.Missing;
    }

    public sealed record TermMergeResult(
        IReadOnlyList<CommercialTerm> Terms,
        IReadOnlyList<TermChange> Changes)
    {
        public IReadOnlyList<TermChange> RequiringDecision =>
            Changes.Where(c => c.NeedsUserDecision).ToList();

        public bool HasProposals => Changes.Any(c => c.Kind is not (TermChangeKind.Unchanged or TermChangeKind.KeptReviewed));
    }

    /// <summary>
    /// Reconciles a fresh analysis against what is already there.
    ///
    /// Running analysis a second time must not undo an afternoon of corrections.
    /// The safe default is therefore additive: new terms are proposed, changes to
    /// terms a person has reviewed are shown rather than applied, and nothing
    /// reviewed is replaced unless the caller names it. Terms the analysis no
    /// longer finds are kept — an agreement does not stop containing something
    /// because a second reading missed it.
    /// </summary>
    public static class TermMerge
    {
        public static TermMergeResult Merge(
            IReadOnlyCollection<CommercialTerm> existing,
            IReadOnlyCollection<CommercialTerm> proposed,
            IReadOnlyCollection<string>? fieldsToReplaceByKey = null)
        {
            ArgumentNullException.ThrowIfNull(existing);
            ArgumentNullException.ThrowIfNull(proposed);

            var replace = new HashSet<string>(
                fieldsToReplaceByKey ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            var changes = new List<TermChange>();
            var result = new List<CommercialTerm>();
            var matchedProposals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var current in existing)
            {
                var match = FindMatch(current, proposed, matchedProposals);

                if (match is null)
                {
                    // Kept. A second reading that misses something is a reason to
                    // look, not a reason to delete.
                    changes.Add(new TermChange(
                        TermChangeKind.Missing, current.Key, current.Name, current, null, Array.Empty<string>()));

                    result.Add(current);
                    continue;
                }

                matchedProposals.Add(match.Key);

                var differences = Compare(current, match);

                if (differences.Count == 0)
                {
                    changes.Add(new TermChange(
                        TermChangeKind.Unchanged, current.Key, current.Name, current, match, differences));

                    result.Add(current);
                    continue;
                }

                var mayReplace = !current.IsHumanReviewed || replace.Contains(current.Key);

                if (mayReplace)
                {
                    // The identity and the review flag stay with the row; only the
                    // values move.
                    result.Add(match with
                    {
                        Key = current.Key,
                        IsHumanReviewed = replace.Contains(current.Key) ? current.IsHumanReviewed : false
                    });

                    changes.Add(new TermChange(
                        TermChangeKind.Changed, current.Key, current.Name, current, match, differences));
                }
                else
                {
                    result.Add(current);

                    changes.Add(new TermChange(
                        TermChangeKind.KeptReviewed, current.Key, current.Name, current, match, differences));
                }
            }

            foreach (var candidate in proposed)
            {
                if (matchedProposals.Contains(candidate.Key)) continue;

                result.Add(candidate);

                changes.Add(new TermChange(
                    TermChangeKind.Added, candidate.Key, candidate.Name, null, candidate, Array.Empty<string>()));
            }

            return new TermMergeResult(result, changes);
        }

        /// <summary>
        /// Pairs a stored term with the proposal that is about the same thing.
        ///
        /// By key first, because a re-run of the same analysis keeps them. By name
        /// second, because a fresh analysis will not. Deliberately not by price or
        /// position: matching on a value that the proposal exists to change would
        /// pair the wrong rows precisely when it matters.
        /// </summary>
        private static CommercialTerm? FindMatch(
            CommercialTerm current,
            IReadOnlyCollection<CommercialTerm> proposed,
            HashSet<string> alreadyMatched)
        {
            var byKey = proposed.FirstOrDefault(p =>
                !alreadyMatched.Contains(p.Key) &&
                string.Equals(p.Key, current.Key, StringComparison.OrdinalIgnoreCase));

            if (byKey is not null) return byKey;

            if (string.IsNullOrWhiteSpace(current.Name)) return null;

            return proposed.FirstOrDefault(p =>
                !alreadyMatched.Contains(p.Key) &&
                string.Equals(p.Name?.Trim(), current.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Which fields differ, by name, so the user is shown what would change
        /// rather than being asked to accept an opaque replacement.
        /// </summary>
        internal static IReadOnlyList<string> Compare(CommercialTerm a, CommercialTerm b)
        {
            var differences = new List<string>();

            void Check(string field, object? left, object? right)
            {
                if (!Equals(left, right)) differences.Add(field);
            }

            Check("Name", a.Name?.Trim(), b.Name?.Trim());
            Check("Description", a.Description?.Trim(), b.Description?.Trim());
            Check("Category", a.Category, b.Category);
            Check("PricingModel", a.PricingModel, b.PricingModel);
            Check("CustomPricingModel", a.CustomPricingModel, b.CustomPricingModel);
            Check("Quantity", a.Quantity, b.Quantity);
            Check("QuantityUnit", a.QuantityUnit, b.QuantityUnit);
            Check("UnitRate", a.UnitRate.Value, b.UnitRate.Value);
            Check("Currency", a.UnitRate.Currency, b.UnitRate.Currency);
            Check("FixedAmount", a.FixedAmount?.Value, b.FixedAmount?.Value);
            Check("BillingRecurrence", Describe(a.BillingRecurrence), Describe(b.BillingRecurrence));
            Check("DeliveryRecurrence", Describe(a.DeliveryRecurrence), Describe(b.DeliveryRecurrence));
            Check("PaymentSchedule", a.PaymentSchedule, b.PaymentSchedule);
            Check("StartDate", a.StartDate, b.StartDate);
            Check("EndDate", a.EndDate, b.EndDate);
            Check("Duration", (a.DurationCount, a.DurationUnit), (b.DurationCount, b.DurationUnit));
            Check("PhaseCount", a.Phases.Count, b.Phases.Count);
            Check("MinimumCommitment", a.MinimumCommitment?.Value, b.MinimumCommitment?.Value);
            Check("Cap", a.Cap?.Value, b.Cap?.Value);
            Check("DiscountPercent", a.DiscountPercent, b.DiscountPercent);
            Check("DiscountAmount", a.DiscountAmount?.Value, b.DiscountAmount?.Value);
            Check("TaxRatePercent", a.TaxRatePercent, b.TaxRatePercent);
            Check("TaxTreatment", a.TaxTreatment, b.TaxTreatment);
            Check("IsMandatory", a.IsMandatory, b.IsMandatory);
            Check("Commitment", a.Commitment, b.Commitment);
            Check("Conditions", a.Conditions, b.Conditions);

            return differences;
        }

        private static string Describe(Recurrence r) =>
            $"{r.Kind}/{r.Unit?.ToString() ?? "-"}/{r.Interval}/{r.Condition ?? "-"}";
    }
}
