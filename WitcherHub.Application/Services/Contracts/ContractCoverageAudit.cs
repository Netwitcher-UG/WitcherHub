using System.Globalization;

namespace WitcherHub.Application.Services.Contracts
{
    /// <summary>Why a ledger item is not accounted for.</summary>
    public enum CoverageGapReason
    {
        /// <summary>No section of the plan claimed it, so nothing was ever going to write it.</summary>
        NotPlanned = 0,

        /// <summary>A section claimed it, and that section came back with nothing in it.</summary>
        NotWritten = 1,

        /// <summary>
        /// It was written about, but the figure or date it turns on does not appear
        /// anywhere in the document. A price that has been paraphrased is a
        /// different price.
        /// </summary>
        EvidenceMissing = 2
    }

    public sealed record CoverageGap(CoverageItem Item, CoverageGapReason Reason)
    {
        /// <summary>
        /// A missing commercial fact is a defect. A missing topic is a thinner
        /// contract than it should have been. They are not the same thing and are
        /// not reported as the same thing.
        /// </summary>
        public bool IsCritical => Item.IsCommercial;
    }

    /// <summary>
    /// What the finished document actually accounts for, measured against the
    /// ledger rather than assumed.
    ///
    /// This is the step that was missing. The model was asked for a contract, and
    /// whatever came back was saved — so a run that dropped two thirds of the
    /// agreed scope produced a document that looked complete, was one page long,
    /// and gave nobody a reason to look closer.
    /// </summary>
    public sealed class ContractCoverageAudit
    {
        private ContractCoverageAudit(
            IReadOnlyList<CoverageGap> gaps,
            IReadOnlyList<CoverageItem> covered)
        {
            Gaps = gaps;
            Covered = covered;
        }

        public IReadOnlyList<CoverageGap> Gaps { get; }
        public IReadOnlyList<CoverageItem> Covered { get; }

        public int Total => Gaps.Count + Covered.Count;

        public bool IsComplete => Gaps.Count == 0;

        /// <summary>Gaps in things that may never be invented or dropped.</summary>
        public IReadOnlyList<CoverageGap> CriticalGaps =>
            Gaps.Where(g => g.IsCritical).ToList();

        /// <summary>0..1, for the log and the telemetry. Not shown to a customer.</summary>
        public double Ratio => Total == 0 ? 1d : (double)Covered.Count / Total;

        /// <summary>
        /// A one-line summary for the generation log: how much of the ledger the
        /// document accounts for, and which ids it does not.
        /// </summary>
        public string Summary =>
            $"{Covered.Count}/{Total} covered ({Ratio:P0})" +
            (Gaps.Count == 0
                ? ""
                : "; missing " + string.Join(", ", Gaps.Select(g => $"{g.Item.Id}:{g.Reason}")));

        /// <summary>
        /// Measures a document against the ledger.
        /// </summary>
        /// <param name="ledger">Everything the contract had to account for.</param>
        /// <param name="plan">
        /// The sections that were planned, each with the ledger ids it undertook to
        /// cover and whether it came back with any text.
        /// </param>
        /// <param name="documentText">The clause text as written, for the literal checks.</param>
        public static ContractCoverageAudit Measure(
            ContractCoverageLedger ledger,
            IReadOnlyList<PlannedSection> plan,
            string documentText)
        {
            var gaps = new List<CoverageGap>();
            var covered = new List<CoverageItem>();

            var normalised = Normalise(documentText);

            // Which section undertook each id, and whether that section produced
            // anything. A section that claimed six items and came back empty leaves
            // six gaps, not one.
            var claimedBy = new Dictionary<string, PlannedSection>(StringComparer.OrdinalIgnoreCase);

            foreach (var section in plan)
                foreach (var id in section.Covers)
                    claimedBy.TryAdd(id, section);

            foreach (var item in ledger.Items)
            {
                if (!claimedBy.TryGetValue(item.Id, out var section))
                {
                    gaps.Add(new CoverageGap(item, CoverageGapReason.NotPlanned));
                    continue;
                }

                if (!section.HasContent)
                {
                    gaps.Add(new CoverageGap(item, CoverageGapReason.NotWritten));
                    continue;
                }

                // Literal checks, where there is something literal to check. Any
                // one piece of evidence is enough: a position's net total appearing
                // in the Vergütung clause covers it whether or not the unit price
                // was also restated.
                if (item.Evidence.Count > 0 &&
                    !item.Evidence.Any(e => normalised.Contains(Normalise(e), StringComparison.Ordinal)))
                {
                    gaps.Add(new CoverageGap(item, CoverageGapReason.EvidenceMissing));
                    continue;
                }

                covered.Add(item);
            }

            return new ContractCoverageAudit(gaps, covered);
        }

        /// <summary>
        /// A section of the plan, with what it undertook to cover.
        ///
        /// The ids are the model's own declaration. That declaration is not taken
        /// on trust for anything with a figure in it — those are checked against
        /// the text — but for a topic it is the only statement of intent there is,
        /// and an unclaimed topic is a gap regardless of what was written.
        /// </summary>
        public sealed record PlannedSection(
            string Heading,
            IReadOnlyList<string> Covers,
            bool HasContent);

        /// <summary>
        /// Comparison that survives the formatting a document goes through:
        /// non-breaking spaces from the model, thin spaces in figures, the
        /// difference between "2.380,00 EUR" written with and without a space.
        /// </summary>
        private static string Normalise(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var chars = text
                .Replace(' ', ' ')
                .Replace(' ', ' ')
                .Replace(' ', ' ')
                .Where(c => !char.IsWhiteSpace(c))
                .Select(char.ToLowerInvariant);

            return string.Concat(chars);
        }

        /// <summary>
        /// What to tell the reviewer, in their words rather than in ids.
        ///
        /// The ids are internal and stay internal; a person looking at a contract
        /// needs to know which agreed thing is not in it, not that SRC-014 failed.
        /// </summary>
        public IReadOnlyList<string> ReviewNotes()
        {
            var notes = new List<string>();

            foreach (var gap in Gaps.OrderByDescending(g => g.IsCritical))
            {
                var what = gap.Item.Topic;

                notes.Add(gap.Reason switch
                {
                    CoverageGapReason.EvidenceMissing when gap.Item.IsCommercial =>
                        $"„{what}“ is discussed but the agreed figure does not appear — check it before sending.",

                    CoverageGapReason.EvidenceMissing =>
                        $"„{what}“ is not stated as agreed — check it before sending.",

                    _ when gap.Item.IsCommercial =>
                        $"„{what}“ is not covered by the contract text.",

                    _ => $"„{what}“ is not covered by the contract text."
                });
            }

            return notes;
        }

        /// <summary>
        /// Kept for the record beside the draft, so a version can be re-examined
        /// later without re-running anything.
        /// </summary>
        public object ToRecord() => new
        {
            covered = Covered.Count,
            total = Total,
            ratio = Math.Round(Ratio, 4),
            criticalGaps = CriticalGaps.Count,
            gaps = Gaps.Select(g => new
            {
                id = g.Item.Id,
                topic = g.Item.Topic,
                reason = g.Reason.ToString(),
                critical = g.IsCritical
            }),
            measuredAt = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        };
    }
}
