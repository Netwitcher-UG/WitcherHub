using WitcherHub.Infrastructure.Data.Models;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.Services.Contracts
{
    /// <summary>
    /// Whether a contract may still be changed.
    ///
    /// Once a customer has signed, the wording on the paper they signed is the
    /// agreement. Editing it afterwards does not amend anything — it silently
    /// replaces the record of what was agreed with something nobody agreed to,
    /// and the signed PDF the customer holds no longer matches ours.
    ///
    /// The rule lives here rather than in each page because it is asked twice
    /// about the same contract: once by the view, to decide what to draw, and
    /// once by every handler, to decide what to accept. Those two answers have
    /// to be the same answer. A disabled button is a courtesy; it is not a
    /// control, because nothing stops a POST being sent anyway.
    /// </summary>
    public static class ContractLock
    {
        /// <summary>
        /// True when the contract's terms, parties and positions are settled and
        /// must not move.
        ///
        /// Terminated is included alongside Signed: a contract that was signed
        /// and then ended is still a record of what was agreed.
        /// </summary>
        public static bool IsLocked(DocumentStatus status) =>
            status is DocumentStatus.Signed or DocumentStatus.Terminated;

        /// <inheritdoc cref="IsLocked(DocumentStatus)"/>
        public static bool IsLocked(Contract? contract) =>
            contract is not null && IsLocked(contract.Status);

        /// <summary>
        /// What to tell someone who tried anyway, in their words rather than the
        /// enum's. Also the tooltip on the buttons that are drawn disabled, so
        /// the reason is given before the attempt as well as after it.
        /// </summary>
        public static string Reason(DocumentStatus status) =>
            status == DocumentStatus.Terminated
                ? "This contract has been terminated and can no longer be changed."
                : "This contract has been signed and can no longer be changed.";

        /// <inheritdoc cref="Reason(DocumentStatus)"/>
        public static string Reason(Contract contract) => Reason(contract.Status);
    }
}
