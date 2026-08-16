namespace WitcherHub.Domain.Commercial
{
    /// <summary>
    /// Where a value came from, in order of authority.
    ///
    /// Lower is more authoritative. Source text is context, not a record: a
    /// document that names a company is evidence about that document, not an
    /// instruction to change who we are.
    /// </summary>
    public enum DataOrigin
    {
        /// <summary>A person typed or confirmed it here. Nothing overrides this.</summary>
        UserConfirmed = 0,

        /// <summary>Our own company record, or the customer's.</summary>
        MasterData = 1,

        /// <summary>Already stored against this project or contract.</summary>
        ExistingStructuredData = 2,

        /// <summary>Proposed by analysis of a document.</summary>
        AiSuggestion = 3,

        /// <summary>Read straight from the document with no interpretation.</summary>
        RawSourceText = 4
    }

    /// <summary>One candidate for a field, and where it came from.</summary>
    public sealed record FieldCandidate(string Field, string? Value, DataOrigin Origin, Provenance? Provenance = null);

    /// <summary>
    /// A difference between what a document says and what our records say.
    /// Presented, never applied on its own.
    /// </summary>
    public sealed record MasterDataConflict(
        string Field,
        string? MasterValue,
        string? DetectedValue,
        DataOrigin DetectedFrom,
        Provenance? Provenance = null)
    {
        /// <summary>
        /// Whether adopting the detected value would change a record outside this
        /// contract. Those need an explicit decision, because the contract screen
        /// is not where a customer's address is maintained.
        /// </summary>
        public bool RequiresMasterDataUpdate { get; init; } = true;
    }

    /// <summary>
    /// Decides which value wins, and reports what it did not take.
    ///
    /// The rule is fixed and one-directional: a document can raise a question
    /// about master data but can never answer it. Our own company details in
    /// particular are never changed through a contract workflow at all —
    /// analysing a document somebody sent us is not a reason to rewrite who we
    /// are.
    /// </summary>
    public static class MasterDataPrecedence
    {
        /// <summary>
        /// The winning value for a field, given every candidate for it.
        /// </summary>
        public static FieldCandidate? Resolve(IEnumerable<FieldCandidate> candidates)
        {
            ArgumentNullException.ThrowIfNull(candidates);

            return candidates
                .Where(c => !string.IsNullOrWhiteSpace(c.Value))
                .OrderBy(c => (int)c.Origin)
                .FirstOrDefault();
        }

        /// <summary>
        /// What a document claims that our records disagree with.
        ///
        /// <paramref name="isOwnCompanyData"/> marks fields describing us rather
        /// than the customer. Those are reported so a person can see the document
        /// is out of date, and are never offered as an update to our own record.
        /// </summary>
        public static IReadOnlyList<MasterDataConflict> FindConflicts(
            IReadOnlyDictionary<string, string?> masterData,
            IReadOnlyDictionary<string, string?> detected,
            Func<string, bool>? isOwnCompanyData = null)
        {
            ArgumentNullException.ThrowIfNull(masterData);
            ArgumentNullException.ThrowIfNull(detected);

            var conflicts = new List<MasterDataConflict>();

            foreach (var (field, detectedValue) in detected)
            {
                if (string.IsNullOrWhiteSpace(detectedValue)) continue;
                if (!masterData.TryGetValue(field, out var masterValue)) continue;
                if (string.IsNullOrWhiteSpace(masterValue)) continue;
                if (Equivalent(masterValue, detectedValue)) continue;

                conflicts.Add(new MasterDataConflict(
                    field, masterValue, detectedValue, DataOrigin.AiSuggestion)
                {
                    // Our own details are never updated from a document. The
                    // conflict is still worth showing — it usually means the
                    // document was written against an older version of us.
                    RequiresMasterDataUpdate = !(isOwnCompanyData?.Invoke(field) ?? false)
                });
            }

            return conflicts;
        }

        /// <summary>
        /// Ignores the differences that are not differences: spacing, casing, and
        /// trailing punctuation. Anything else is a real disagreement and is
        /// reported rather than smoothed over.
        /// </summary>
        public static bool Equivalent(string a, string b)
        {
            static string Normalise(string value) =>
                System.Text.RegularExpressions.Regex
                    .Replace(value, @"\s+", " ")
                    .Trim()
                    .TrimEnd('.', ',', ';')
                    .ToLowerInvariant();

            return Normalise(a) == Normalise(b);
        }
    }
}
