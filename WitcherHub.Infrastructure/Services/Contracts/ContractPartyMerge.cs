using System.Text.RegularExpressions;

namespace WitcherHub.Infrastructure.Services.Contracts
{
    /// <summary>
    /// Who the parties are, as they stood when a contract was prepared.
    /// </summary>
    public sealed record PartyDetails(
        string? CompanyName,
        string? CompanyAddress,
        string? CustomerName,
        string? CustomerAddress,
        DateOnly? ContractDate)
    {
        /// <summary>
        /// The fields a contract cannot sensibly be prepared without. Reported by
        /// name so the user is told exactly what to go and fill in, rather than
        /// being told that something is missing.
        /// </summary>
        public IReadOnlyList<string> MissingFields
        {
            get
            {
                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(CompanyName)) missing.Add("Company name");
                if (string.IsNullOrWhiteSpace(CompanyAddress)) missing.Add("Company address");
                if (string.IsNullOrWhiteSpace(CustomerName)) missing.Add("Customer name");
                if (string.IsNullOrWhiteSpace(CustomerAddress)) missing.Add("Customer address");
                return missing;
            }
        }
    }

    /// <summary>One replacement the user is asked to confirm.</summary>
    public sealed record PartyReplacement(
        string Field,
        string? OldValue,
        string? ProposedValue,
        bool IsPlaceholder)
    {
        /// <summary>
        /// A placeholder is unambiguous and applied without asking. Text that
        /// merely looks like an old company name is a guess, and replacing a
        /// party name in a contract on a guess is not acceptable — so it is shown
        /// and left to the user.
        /// </summary>
        public bool RequiresConfirmation => !IsPlaceholder;
    }

    public sealed record PartyMergeResult(
        string Document,
        IReadOnlyList<PartyReplacement> Applied,
        IReadOnlyList<PartyReplacement> Proposed,
        IReadOnlyList<string> MissingFields);

    /// <summary>
    /// Puts the current parties into a supplied contract, without touching the
    /// original.
    ///
    /// Two different jobs, deliberately kept apart. Placeholders such as
    /// [CUSTOMER_NAME] mean exactly one thing, so they are filled in. Text that
    /// merely resembles an old company name is a guess, and a guess that rewrites
    /// who a contract is between is not something to do silently — those are
    /// returned as proposals for a person to accept.
    ///
    /// Nothing here is written back over the source: the caller stores the result
    /// as a new version.
    /// </summary>
    public static class ContractPartyMerge
    {
        /// <summary>
        /// Recognised placeholder spellings. [CUSTOMER_NAME], {{customer_name}}
        /// and «CUSTOMER NAME» all appear in documents people actually supply.
        /// </summary>
        private static readonly (string Field, string[] Names)[] Placeholders =
        {
            ("Company name",     new[] { "COMPANY_NAME", "COMPANY NAME", "PROVIDER_NAME", "PROVIDER NAME", "FIRMENNAME" }),
            ("Company address",  new[] { "COMPANY_ADDRESS", "COMPANY ADDRESS", "PROVIDER_ADDRESS", "FIRMENANSCHRIFT" }),
            ("Customer name",    new[] { "CUSTOMER_NAME", "CUSTOMER NAME", "CLIENT_NAME", "KUNDENNAME" }),
            ("Customer address", new[] { "CUSTOMER_ADDRESS", "CUSTOMER ADDRESS", "CLIENT_ADDRESS", "KUNDENANSCHRIFT" }),
            ("Contract date",    new[] { "CONTRACT_DATE", "CONTRACT DATE", "DATE", "VERTRAGSDATUM", "DATUM" })
        };

        public static PartyMergeResult Merge(
            string document,
            PartyDetails parties,
            IEnumerable<PartyReplacement>? confirmedReplacements = null)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(parties);

            var applied = new List<PartyReplacement>();
            var result = document;

            foreach (var (field, names) in Placeholders)
            {
                var replacement = ValueFor(field, parties);
                if (string.IsNullOrWhiteSpace(replacement)) continue;

                foreach (var name in names)
                {
                    var pattern = BuildPlaceholderPattern(name);
                    if (!Regex.IsMatch(result, pattern, RegexOptions.IgnoreCase)) continue;

                    result = Regex.Replace(result, pattern, replacement.Replace("$", "$$"), RegexOptions.IgnoreCase);

                    applied.Add(new PartyReplacement(field, $"[{name}]", replacement, IsPlaceholder: true));
                }
            }

            // Replacements the user already accepted are applied literally.
            foreach (var confirmed in confirmedReplacements ?? Array.Empty<PartyReplacement>())
            {
                if (string.IsNullOrWhiteSpace(confirmed.OldValue) ||
                    string.IsNullOrWhiteSpace(confirmed.ProposedValue))
                    continue;

                if (!result.Contains(confirmed.OldValue, StringComparison.Ordinal)) continue;

                result = result.Replace(confirmed.OldValue, confirmed.ProposedValue, StringComparison.Ordinal);
                applied.Add(confirmed with { IsPlaceholder = false });
            }

            return new PartyMergeResult(
                result,
                applied,
                Array.Empty<PartyReplacement>(),
                parties.MissingFields);
        }

        /// <summary>
        /// Party details in the document that do not match our records, as
        /// proposals. Never applied here — the caller shows them and applies only
        /// what comes back confirmed.
        /// </summary>
        public static IReadOnlyList<PartyReplacement> ProposeReplacements(
            string document,
            PartyDetails parties,
            string? documentCompanyName,
            string? documentCompanyAddress,
            string? documentCustomerName,
            string? documentCustomerAddress)
        {
            var proposals = new List<PartyReplacement>();

            void Propose(string field, string? inDocument, string? ours)
            {
                if (string.IsNullOrWhiteSpace(inDocument) || string.IsNullOrWhiteSpace(ours)) return;
                if (Same(inDocument, ours)) return;
                if (!document.Contains(inDocument.Trim(), StringComparison.OrdinalIgnoreCase)) return;

                proposals.Add(new PartyReplacement(field, inDocument.Trim(), ours.Trim(), IsPlaceholder: false));
            }

            Propose("Company name", documentCompanyName, parties.CompanyName);
            Propose("Company address", documentCompanyAddress, parties.CompanyAddress);
            Propose("Customer name", documentCustomerName, parties.CustomerName);
            Propose("Customer address", documentCustomerAddress, parties.CustomerAddress);

            return proposals;
        }

        private static bool Same(string a, string b) =>
            string.Equals(
                Regex.Replace(a, @"\s+", " ").Trim(),
                Regex.Replace(b, @"\s+", " ").Trim(),
                StringComparison.OrdinalIgnoreCase);

        private static string? ValueFor(string field, PartyDetails p) => field switch
        {
            "Company name" => p.CompanyName,
            "Company address" => p.CompanyAddress,
            "Customer name" => p.CustomerName,
            "Customer address" => p.CustomerAddress,
            "Contract date" => p.ContractDate?.ToString("dd.MM.yyyy"),
            _ => null
        };

        /// <summary>
        /// Matches [NAME], {NAME}, {{NAME}}, &lt;NAME&gt; and «NAME» around the same token.
        /// </summary>
        private static string BuildPlaceholderPattern(string name)
        {
            var escaped = Regex.Escape(name).Replace(@"\ ", @"[\s_]+");

            return $@"(\[{escaped}\]|\{{\{{{escaped}\}}\}}|\{{{escaped}\}}|<{escaped}>|«{escaped}»)";
        }
    }
}
