using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Domain.Projects
{
    /// <summary>
    /// How far the documents inside a project have got.
    ///
    /// Deliberately not a project status. A project can be Active with nothing
    /// quoted, or Draft with a draft contract in it — those are two independent
    /// facts and squashing them into one field is what made the project list and
    /// the project page disagree.
    /// </summary>
    public enum DocumentProgress
    {
        /// <summary>None exists.</summary>
        NotCreated = 0,

        /// <summary>Exists, not yet sent.</summary>
        Draft = 1,

        /// <summary>Sent to the customer, awaiting their answer.</summary>
        Awaiting = 2,

        /// <summary>Accepted, signed, or paid.</summary>
        Settled = 3,

        /// <summary>Declined, cancelled or written off.</summary>
        Closed = 4
    }

    /// <summary>
    /// A project's own state, and the state of the documents in it, side by side.
    ///
    /// The project status is the project's alone. It is set by a person deciding
    /// where the project stands, never by a document changing hands. Creating a
    /// draft contract used to rewrite the project's status to Waiting, which made
    /// the project undeletable and made two screens report different things about
    /// the same project — the status was being used to mean two incompatible
    /// things at once.
    /// </summary>
    public readonly record struct ProjectWorkflowState
    {
        public ProjectWorkflowState(
            ProjectStatus status,
            bool isArchived,
            DocumentProgress quotes,
            DocumentProgress contracts,
            DocumentProgress invoices,
            DocumentProgress payments)
        {
            Status = status;
            IsArchived = isArchived;
            Quotes = quotes;
            Contracts = contracts;
            Invoices = invoices;
            Payments = payments;
        }

        /// <summary>The project's own lifecycle. Never derived from a document.</summary>
        public ProjectStatus Status { get; }

        public bool IsArchived { get; }

        public DocumentProgress Quotes { get; }
        public DocumentProgress Contracts { get; }
        public DocumentProgress Invoices { get; }
        public DocumentProgress Payments { get; }

        /// <summary>
        /// The one thing most worth doing next, from what is actually there.
        /// A suggestion for the screen, not a rule the domain enforces.
        /// </summary>
        public string NextAction => (Quotes, Contracts, Invoices) switch
        {
            (DocumentProgress.NotCreated, DocumentProgress.NotCreated, _) =>
                "Create a quote, or go straight to a contract.",

            (_, DocumentProgress.NotCreated, _) when Quotes is DocumentProgress.Settled =>
                "The quote is accepted. Create the contract.",

            (_, DocumentProgress.NotCreated, _) =>
                "Create the contract when you are ready.",

            (_, DocumentProgress.Draft, _) =>
                "The contract is a draft. Finish it and send it for signature.",

            (_, DocumentProgress.Awaiting, _) =>
                "The contract is with the customer, awaiting signature.",

            (_, DocumentProgress.Settled, DocumentProgress.NotCreated) =>
                "The contract is signed. Raise the first invoice.",

            (_, DocumentProgress.Settled, _) when Payments is not DocumentProgress.Settled =>
                "Invoiced. Waiting for payment.",

            _ => "Everything is up to date."
        };
    }

    /// <summary>
    /// What stands in the way of deleting a project, if anything.
    ///
    /// Deletion used to be refused on the project's status, which a contract
    /// could change behind the user's back. What actually matters is whether
    /// deleting would destroy records that have to be kept — so that is what is
    /// checked, and the answer says exactly what was found.
    /// </summary>
    public sealed record ProjectDeletionImpact(
        int Quotes,
        int Contracts,
        int Invoices,
        int Payments,
        int Milestones,
        bool HasSignedContract,
        bool HasIssuedInvoice)
    {
        public int TotalRecords => Quotes + Contracts + Invoices + Payments + Milestones;

        /// <summary>
        /// True when permanent deletion would destroy a financial or legal
        /// record. Those are never cascade-deleted: a signed contract and an
        /// issued invoice have to survive the project they were created under.
        /// </summary>
        public bool IsBlocked => HasSignedContract || HasIssuedInvoice || Invoices > 0 || Payments > 0;

        /// <summary>
        /// True when there is nothing to lose, so deletion needs no more than an
        /// ordinary confirmation.
        /// </summary>
        public bool IsClean => TotalRecords == 0;

        public string? BlockingReason
        {
            get
            {
                if (!IsBlocked) return null;

                var reasons = new List<string>();

                if (HasSignedContract) reasons.Add("a signed contract");
                if (HasIssuedInvoice) reasons.Add("an issued invoice");
                if (Payments > 0) reasons.Add($"{Payments} recorded payment(s)");
                else if (Invoices > 0) reasons.Add($"{Invoices} invoice(s)");

                return
                    "This project cannot be deleted permanently because it holds " +
                    string.Join(", ", reasons) +
                    ". Records like these have to be kept. Archive the project instead — " +
                    "it disappears from the active list and everything in it is preserved.";
            }
        }

        /// <summary>What a person is about to destroy, so they can see it before agreeing.</summary>
        public IReadOnlyList<string> WhatWillBeDeleted
        {
            get
            {
                var items = new List<string>();

                if (Quotes > 0) items.Add($"{Quotes} quote(s)");
                if (Contracts > 0) items.Add($"{Contracts} contract(s) and their versions");
                if (Milestones > 0) items.Add($"{Milestones} milestone(s)");

                return items;
            }
        }
    }
}
