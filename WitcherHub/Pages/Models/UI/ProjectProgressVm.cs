using WitcherHub.Domain.Projects;

namespace WitcherHub.Pages.Models.UI
{
    /// <summary>
    /// One document kind's progress, for the small badges beside a project.
    ///
    /// <see cref="SettledLabel"/> exists because "settled" is the same idea with
    /// a different word depending on what it is: a quote is accepted, a contract
    /// is signed, an invoice is paid. Reporting all three as "Settled" would be
    /// accurate and useless.
    /// </summary>
    public sealed record ProjectProgressVm(string Kind, DocumentProgress Progress, string SettledLabel)
    {
        public static ProjectProgressVm Quote(DocumentProgress progress) =>
            new("Quote", progress, "Accepted");

        public static ProjectProgressVm Contract(DocumentProgress progress) =>
            new("Contract", progress, "Signed");

        public static ProjectProgressVm Invoice(DocumentProgress progress) =>
            new("Invoice", progress, "Paid");
    }
}
