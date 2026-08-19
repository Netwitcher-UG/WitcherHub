using System.Text.Json;
using System.Text.Json.Serialization;

namespace WitcherHub.Application.Services.Contracts
{
    /// <summary>
    /// The plan for one contract: its sections, in order, and which agreed thing
    /// each of them is responsible for.
    ///
    /// This is what replaced the fixed list of seven headings. Seven headings
    /// produced a seven-clause contract from three positions and a seven-clause
    /// contract from thirty, which is why every generated document fitted on one
    /// page regardless of what had been agreed. A plan has as many sections as the
    /// content needs, and — because every section names the ledger entries it
    /// undertakes to cover — it can be checked before a word is written and again
    /// after.
    /// </summary>
    public sealed class ContractOutline
    {
        /// <summary>"Dienstleistungsvertrag", "Wartungsvertrag", and so on.</summary>
        [JsonPropertyName("contractType")]
        public string? ContractType { get; set; }

        [JsonPropertyName("preamble")]
        public string? Preamble { get; set; }

        [JsonPropertyName("sections")]
        public List<PlannedSection> Sections { get; set; } = new();

        public bool HasSections => Sections.Any(s => !string.IsNullOrWhiteSpace(s.Heading));

        public sealed class PlannedSection
        {
            [JsonPropertyName("heading")]
            public string Heading { get; set; } = "";

            /// <summary>What this § has to establish. Guidance for the writing stage, never printed.</summary>
            [JsonPropertyName("intent")]
            public string? Intent { get; set; }

            [JsonPropertyName("covers")]
            public List<string> Covers { get; set; } = new();
        }

        /// <summary>
        /// Ledger entries the plan forgot.
        ///
        /// Caught here rather than at the audit, because a plan that never
        /// mentions a position is going to produce a contract that never mentions
        /// it, and the cheap fix is to assign it before anything is written.
        /// </summary>
        public IReadOnlyList<CoverageItem> Unassigned(ContractCoverageLedger ledger)
        {
            var planned = Sections
                .SelectMany(s => s.Covers)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return ledger.Items.Where(i => !planned.Contains(i.Id)).ToList();
        }

        /// <summary>
        /// Puts forgotten entries somewhere rather than letting them fall out.
        ///
        /// Appended to a section of their own so they are written about at all;
        /// the audit still reports on them, so a plan that routinely needs this is
        /// visible rather than papered over.
        /// </summary>
        public void AssignLeftovers(IReadOnlyList<CoverageItem> leftovers, string heading)
        {
            if (leftovers.Count == 0) return;

            var section = Sections.FirstOrDefault(s =>
                string.Equals(s.Heading, heading, StringComparison.OrdinalIgnoreCase));

            if (section is null)
            {
                section = new PlannedSection { Heading = heading, Intent = "Weitere vereinbarte Punkte." };
                Sections.Add(section);
            }

            section.Covers.AddRange(leftovers.Select(i => i.Id));
        }

        /// <summary>
        /// The plan in the shape the writing stage consumes: batches small enough
        /// that one answer is not asked to carry the whole contract.
        /// </summary>
        public IEnumerable<IReadOnlyList<PlannedSection>> InBatches(int size)
        {
            var usable = Sections.Where(s => !string.IsNullOrWhiteSpace(s.Heading)).ToList();

            for (var i = 0; i < usable.Count; i += size)
                yield return usable.GetRange(i, Math.Min(size, usable.Count - i));
        }

        // ---------------------------------------------------------------

        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        public static bool TryParse(string? raw, out ContractOutline outline, out string? error)
        {
            outline = new ContractOutline();
            error = null;

            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "The assistant returned nothing.";
                return false;
            }

            var json = GeneratedContractContent.ExtractJson(raw);

            if (json is null)
            {
                error = "No JSON object was found in the plan.";
                return false;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<ContractOutline>(json, ReadOptions);

                if (parsed is null || !parsed.HasSections)
                {
                    error = "The plan contained no sections.";
                    return false;
                }

                outline = parsed;
                return true;
            }
            catch (JsonException ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    /// <summary>
    /// What a model found in a pasted document.
    ///
    /// Kept as its own step, and its own type, so that "what did it think the
    /// document said?" is answerable without re-running anything. The ids are
    /// assigned by <see cref="ContractCoverageLedger"/> afterwards — a model that
    /// renumbers its own list between calls makes the audit meaningless.
    /// </summary>
    public sealed class SourceAnalysisResult
    {
        [JsonPropertyName("topics")]
        public List<Topic> Topics { get; set; } = new();

        public sealed class Topic
        {
            [JsonPropertyName("topic")]
            public string? Name { get; set; }

            [JsonPropertyName("detail")]
            public string? Detail { get; set; }
        }

        public IEnumerable<ContractCoverageLedger.SourceTopic> AsCoverageTopics() =>
            Topics
                .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                .Select(t => new ContractCoverageLedger.SourceTopic(t.Name!, t.Detail));

        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        public static bool TryParse(string? raw, out SourceAnalysisResult result, out string? error)
        {
            result = new SourceAnalysisResult();
            error = null;

            var json = string.IsNullOrWhiteSpace(raw) ? null : GeneratedContractContent.ExtractJson(raw);

            if (json is null)
            {
                error = "No JSON object was found in the source analysis.";
                return false;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<SourceAnalysisResult>(json, ReadOptions);

                if (parsed is null)
                {
                    error = "The source analysis could not be read.";
                    return false;
                }

                result = parsed;
                return true;
            }
            catch (JsonException ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
