using System.Globalization;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Infrastructure.Data.Models;

namespace WitcherHub.Infrastructure.Services.Contracts
{
    /// <summary>
    /// The structured values the clause library fills its placeholders from.
    ///
    /// Everything here comes out of the record or the configuration. Nothing is
    /// defaulted to a plausible number: a payment term of fourteen days is a
    /// perfectly ordinary figure and a contract that states one nobody agreed to
    /// is a contract that says something false. A value that is not known is
    /// absent, its clause is not rendered, and the omission is reported — which
    /// is the behaviour the owner asked for in place of invention.
    ///
    /// Several of these have no column yet. They are listed by name so the
    /// review screen can say exactly what has to be decided before a contract
    /// can be approved, rather than reporting that "something is missing".
    /// </summary>
    public static class ContractStructuredFields
    {
        private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

        /// <summary>
        /// Fields the library may ask for. Named here so a module referring to
        /// one that is never supplied is a bug that a test can catch.
        /// </summary>
        public static readonly string[] Known =
        [
            "PaymentDueDays",
            "ServiceStartDate",
            "ServiceEndDate",
            "MinimumTermMonths",
            "RenewalTermMonths",
            "NoticePeriodDays",
            "RevisionRounds",
            "AcceptancePeriodDays",
            "ProviderSeat",
            "FormRequirement",
            "DocumentPrecedence"
        ];

        public static IReadOnlyDictionary<string, string?> From(
            Contract contract,
            ContractTemplateOptions options,
            IReadOnlyList<ManualPositionDto> positions)
        {
            ArgumentNullException.ThrowIfNull(contract);
            ArgumentNullException.ThrowIfNull(options);

            var fields = new Dictionary<string, string?>(StringComparer.Ordinal);

            // From the contract record.
            fields["ServiceStartDate"] = contract.StartDate?.ToString("dd.MM.yyyy", De);
            fields["ServiceEndDate"] = contract.EndDate?.ToString("dd.MM.yyyy", De);

            // From configuration: the provider's seat decides the jurisdiction
            // clause, and getting it from settings is the difference between
            // stating a court and guessing one.
            fields["ProviderSeat"] = string.IsNullOrWhiteSpace(options.ProviderSeat)
                ? null
                : options.ProviderSeat;

            fields["FormRequirement"] = string.IsNullOrWhiteSpace(options.FormRequirement)
                ? null
                : options.FormRequirement;

            fields["DocumentPrecedence"] = string.IsNullOrWhiteSpace(options.DocumentPrecedence)
                ? null
                : options.DocumentPrecedence;

            // The house standard, stated by the owner in configuration rather
            // than chosen by anything here. Unset leaves the payment clause out
            // and says so, which is the same rule as everything else on this
            // list — it is on this side of it only because the owner has
            // somewhere to write it down.
            fields["PaymentDueDays"] = options.PaymentDueDays is > 0
                ? options.PaymentDueDays.Value.ToString(CultureInfo.InvariantCulture)
                : null;

            // Deliberately left unset until the contract carries them. Each one
            // is a term of the agreement, and the clause that needs it stays out
            // of the document while it is unknown.
            fields["MinimumTermMonths"] = null;
            fields["RenewalTermMonths"] = null;
            fields["NoticePeriodDays"] = null;
            fields["RevisionRounds"] = null;
            fields["AcceptancePeriodDays"] = null;

            return fields;
        }

        /// <summary>
        /// The fields a contract still needs, in the order a person would fill
        /// them in. Shown beside the draft so "not approvable" comes with a list
        /// of what to do about it.
        /// </summary>
        public static IReadOnlyList<string> Missing(IReadOnlyDictionary<string, string?> fields) =>
            Known.Where(k => !fields.TryGetValue(k, out var v) || string.IsNullOrWhiteSpace(v)).ToList();
    }
}
