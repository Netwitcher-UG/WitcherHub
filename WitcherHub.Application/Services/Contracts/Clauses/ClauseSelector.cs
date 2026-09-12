namespace WitcherHub.Application.Services.Contracts.Clauses
{
    /// <summary>
    /// Why a clause module did not make it into the contract.
    /// </summary>
    public sealed record ClauseRejection(string ModuleId, string Reason);

    /// <summary>
    /// What a validated plan comes to: the modules that will actually be
    /// rendered, what was refused and why, and whether the result may be
    /// approved.
    /// </summary>
    public sealed record ClauseSelection
    {
        public required IReadOnlyList<ClauseModule> Modules { get; init; }
        public required IReadOnlyList<ClauseRejection> Rejected { get; init; }
        public required IReadOnlyList<string> BlockingIssues { get; init; }
        public required IReadOnlyList<string> Warnings { get; init; }

        /// <summary>
        /// True only when nothing blocks and every rendered module has been
        /// released by a lawyer. A contract may be drafted and read without
        /// this; it may not be approved or signed.
        /// </summary>
        public bool CanApprove => BlockingIssues.Count == 0;
    }

    /// <summary>
    /// Decides which clause modules a contract actually gets.
    ///
    /// The model proposes; this disposes. Everything it can get wrong is checked
    /// here rather than trusted:
    ///
    ///   * an id that is not in the library — the plan is not partially salvaged,
    ///     because a model inventing ids has stopped following the schema;
    ///   * a module whose nature or recurrence does not match the contract, which
    ///     is how an Abnahme clause lands on advisory work;
    ///   * a module whose structured inputs are missing, which is how a notice
    ///     period nobody agreed to gets a number;
    ///   * two modules that contradict each other;
    ///   * a module no lawyer has released.
    ///
    /// The defaults are added first and are not the model's to remove. It can
    /// only add to them, and only from the allowed list.
    /// </summary>
    public static class ClauseSelector
    {
        public static ClauseSelection Select(
            ContractGenerationPlan plan,
            IReadOnlyDictionary<string, string?> structuredFields)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(structuredFields);

            var blocking = new List<string>();
            var warnings = new List<string>();
            var rejected = new List<ClauseRejection>();

            var nature = plan.Classification.Nature;
            var recurrence = plan.Classification.RecurrenceKind;

            // The model's choices, on top of the standing ones. Order matters
            // only for de-duplication; rendering order comes from SectionOrder.
            var requested = ContractClauseLibrary.DefaultSelectedIds
                .Concat(plan.SelectedClauseModuleIds ?? [])
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var accepted = new List<ClauseModule>();

            foreach (var id in requested)
            {
                var module = ContractClauseLibrary.Find(id);

                if (module is null)
                {
                    // Not a warning. A clause id the library does not contain is
                    // either a hallucination or a library that has moved on
                    // without the caller, and neither should quietly produce a
                    // contract with a hole where a clause was meant to be.
                    blocking.Add($"Unbekanntes Klauselmodul „{id}“ wurde vorgeschlagen.");
                    rejected.Add(new ClauseRejection(id, "Nicht in der Klauselbibliothek enthalten."));
                    continue;
                }

                if (module.ReviewStatus == ClauseReviewStatus.Retired)
                {
                    rejected.Add(new ClauseRejection(id, "Zurückgezogen."));
                    continue;
                }

                if (module.AppliesToNatures.Count > 0 && !module.AppliesToNatures.Contains(nature))
                {
                    rejected.Add(new ClauseRejection(
                        id, $"Nicht anwendbar auf Vertragsart {nature}."));
                    continue;
                }

                if (module.AppliesToRecurrences.Count > 0 &&
                    !module.AppliesToRecurrences.Contains(recurrence))
                {
                    rejected.Add(new ClauseRejection(
                        id, $"Nicht anwendbar auf Leistungsrhythmus {recurrence}."));
                    continue;
                }

                var missing = module.RequiredFields
                    .Where(f => !structuredFields.TryGetValue(f, out var v) || string.IsNullOrWhiteSpace(v))
                    .ToList();

                if (missing.Count > 0)
                {
                    // Not rendered, and said out loud. This is the difference
                    // between a contract that omits a notice period and one that
                    // states a notice period nobody chose.
                    rejected.Add(new ClauseRejection(
                        id, "Fehlende Vertragsdaten: " + string.Join(", ", missing)));

                    blocking.Add(
                        $"„{module.Title}“ kann nicht ausgegeben werden. Fehlende Angaben: " +
                        string.Join(", ", missing) + ".");
                    continue;
                }

                accepted.Add(module);
            }

            // Contradictions, after the survivors are known.
            foreach (var module in accepted.ToList())
            {
                foreach (var other in module.IncompatibleWith)
                {
                    if (!accepted.Any(m => string.Equals(m.Id, other, StringComparison.Ordinal)))
                        continue;

                    blocking.Add(
                        $"Die Module „{module.Id}“ und „{other}“ schließen einander aus.");
                }
            }

            // A module nobody has released can be read in a draft and cannot be
            // approved. Reported once, listing what is waiting.
            var unreviewed = accepted
                .Where(m => m.ReviewStatus == ClauseReviewStatus.PendingLegalReview)
                .Select(m => m.Id)
                .ToList();

            if (unreviewed.Count > 0)
            {
                blocking.Add(
                    "Folgende Klauselmodule sind noch nicht anwaltlich freigegeben: " +
                    string.Join(", ", unreviewed) + ".");
            }

            foreach (var item in plan.MissingInformation ?? [])
            {
                var text = string.IsNullOrWhiteSpace(item.Reason)
                    ? item.Field
                    : $"{item.Field}: {item.Reason}";

                if (item.IsBlocking) blocking.Add(text);
                else warnings.Add(text);
            }

            if (!plan.CanGenerateFinalContract)
            {
                blocking.Add(
                    "Die Analyse hat den Vertrag nicht als abschlussfähig eingestuft.");
            }

            return new ClauseSelection
            {
                Modules = accepted
                    .OrderBy(m => m.SectionOrder)
                    .ThenBy(m => m.Id, StringComparer.Ordinal)
                    .ToList(),
                Rejected = rejected,
                BlockingIssues = blocking.Distinct(StringComparer.Ordinal).ToList(),
                Warnings = warnings.Distinct(StringComparer.Ordinal).ToList()
            };
        }

        /// <summary>
        /// A module's text with its placeholders filled from the structured
        /// values. Only reached for modules whose required fields are all
        /// present, so nothing here can invent a value.
        /// </summary>
        public static string Render(
            ClauseModule module,
            IReadOnlyDictionary<string, string?> structuredFields)
        {
            var text = module.Text;

            foreach (var name in module.Placeholders)
            {
                structuredFields.TryGetValue(name, out var value);
                text = text.Replace("{{" + name + "}}", value ?? "", StringComparison.Ordinal);
            }

            return text;
        }
    }
}
