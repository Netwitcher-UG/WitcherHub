using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace WitcherHub.Infrastructure.Data.Models
{
    public class Enums
    {
        public enum CustomerType { Individual, Company }
        /// <summary>
        /// A project's own lifecycle, and nothing else's.
        ///
        /// This is set by a person deciding where the project stands. It is never
        /// written by a quote or a contract changing state: those have their own
        /// statuses, and a draft contract inside a draft project is two draft
        /// things, not one ambiguous one.
        ///
        /// Waiting is gone. It meant "a document exists but has not been agreed",
        /// which is a fact about the document, and using it as a project status is
        /// what made the project list and the project page disagree — and what
        /// made a project the user had just created refuse to be deleted.
        /// </summary>
        public enum ProjectStatus
        {
            /// <summary>Being set up. Nothing has been committed to the customer.</summary>
            Draft = 0,

            /// <summary>Work is live.</summary>
            Active = 1,

            /// <summary>Finished.</summary>
            Closed = 2,

            /// <summary>Abandoned.</summary>
            Cancelled = 3,

            /// <summary>Paused, expected to resume. Replaces the old Waiting.</summary>
            OnHold = 4
        }

        // حالة الوثائق (نستخدمها للـ Quote/Contract/Invoice)
        public enum DocumentStatus
        {
            Draft = 0,
            Sent = 1,
            Accepted = 2,
            Rejected = 3,
            Signed = 4,
            Terminated = 5,
            Issued = 6,
            Paid = 7,
            Void = 8,
            Open = 9,
            Overdue = 10,
            Cancelled = 11
        }
        public enum QuoteAfterSignAction
        {
            Contract = 0,
            Invoice = 1
        }
        public enum ServiceType {
            Website = 0,
            Design = 1,
            Video = 2,
            Editing = 3,
            Marketing = 4,
            SEO = 5,
            Other = 6
        }
        public enum PricingModel { Fixed, Unit, Tiered, Hourly }

        /// <summary>
        /// Where a contract position came from. Manual positions carry no
        /// ServiceCatalogItem reference at all — the link stays null rather than
        /// pointing at a placeholder catalog record.
        /// </summary>
        public enum ContractItemSource
        {
            Catalog = 0,
            Manual = 1,
            Quote = 2,

            /// <summary>
            /// Read out of contract text the user supplied. Like a manual position
            /// it has no catalog service behind it, and it additionally remembers
            /// which supplied version it was read from.
            /// </summary>
            ExtractedFromContractText = 3
        }

        /// <summary>
        /// What a contract is built from.
        ///
        /// Recorded on the contract rather than guessed from how many positions
        /// happen to exist: a contract whose wording is a document the customer
        /// supplied is legitimately a contract with no positions, and counting
        /// rows cannot tell that apart from an unfinished one.
        /// </summary>
        public enum ContractSourceMode
        {
            Positions = 0,
            SuppliedText = 1,
            Hybrid = 2
        }

        /// <summary>
        /// What a stored contract version is: text the system wrote, text a person
        /// supplied from outside, or a supplied text a person then edited.
        /// </summary>
        public enum ContractDraftKind
        {
            Generated = 0,
            Supplied = 1,
            HumanEdited = 2,

            /// <summary>
            /// A supplied document with the confirmed parties and terms merged
            /// into it. Distinct from Generated because no model wrote it, and
            /// distinct from Supplied because it is not the original.
            /// </summary>
            Prepared = 3
        }

        /// <summary>
        /// Which long-running assistant action a job is.
        ///
        /// Reading a supplied document was moved off the request thread when it
        /// started returning HTTP 502; writing the contract and tidying the
        /// positions were left on it, and both call the model. Generation became
        /// several calls rather than one, which turned "sometimes too slow" into
        /// "always too slow".
        /// </summary>
        public enum ContractAiJobKind
        {
            /// <summary>Writing the contract wording from the positions and the record.</summary>
            Generation = 0,

            /// <summary>Tidying rough positions into proper ones.</summary>
            Organize = 1,

            /// <summary>
            /// Rewriting the contract from wording a person edited by hand on the
            /// override screen.
            ///
            /// That screen was the last one still calling the model on the request
            /// that asked for it, long after the other two were moved off. It is
            /// the same work with a different starting point, so it is the same
            /// kind of job.
            ///
            /// Stored as text in an unconstrained column, so this value costs no
            /// schema change.
            /// </summary>
            Override = 2
        }

        /// <summary>Where a background assistant job has got to.</summary>
        public enum ContractAiJobStatus
        {
            /// <summary>Queued or running. The page polls while this stands.</summary>
            Running = 0,

            /// <summary>Finished with something to show.</summary>
            Succeeded = 1,

            /// <summary>Finished without it, for a reason the user should read.</summary>
            Failed = 2
        }

        /// <summary>
        /// How far a supplied contract text has got through analysis. Only
        /// <see cref="Confirmed"/> means a person has agreed the extracted values.
        /// </summary>
        public enum ContractExtractionStatus
        {
            NotAnalysed = 0,
            Analysed = 1,
            Confirmed = 2,
            Failed = 3,

            /// <summary>
            /// Started and still running. Reading a long contract takes longer
            /// than a proxy will hold a connection, so the work happens off the
            /// request and the page asks how it is getting on.
            /// </summary>
            Analysing = 4
        }

        /// <summary>
        /// Where a stored version stands, separately from what kind of thing it
        /// is. A version is never edited in place once approved; a newer approval
        /// supersedes it and both stay in the history, so which text a signature
        /// applied to remains answerable.
        /// </summary>
        public enum ContractDraftStatus
        {
            Draft = 0,
            PendingReview = 1,
            Approved = 2,

            /// <summary>Was approved, and a later version has since been approved.</summary>
            Superseded = 3,

            Signed = 4
        }

        /// <summary>How far the supplied source document has got.</summary>
        public enum ContractSourceState
        {
            None = 0,
            SuppliedTextSaved = 1,
            AnalysisPending = 2,
            AnalysisFailed = 3,
            Analysed = 4
        }

        /// <summary>Whether a person has agreed the values read out of it.</summary>
        public enum ContractReviewState
        {
            NotRequired = 0,
            RequiresReview = 1,
            PartiallyConfirmed = 2,
            Confirmed = 3
        }

        /// <summary>Whether a customer-specific draft has been produced.</summary>
        public enum ContractPreparationState
        {
            NoPreparedDraft = 0,
            Preparing = 1,
            PreparedDraft = 2,
            PreparationFailed = 3
        }

        /// <summary>
        /// When the agreed service starts running for the customer.
        /// </summary>
        public enum ActivationMethod
        {
            NotApplicable = 0,
            AfterSignature = 1,
            AfterInitialPayment = 2,
            OnSpecifiedDate = 3,
            ManualActivation = 4
        }

        public enum RuleAction { Add, Multiply, SetUnit, SetTotal, Discount }

        public enum DiscountType { Percent, Amount,Fixed}

        public enum PaymentMethod { Cash, Bank, Card, Online, Other }
        public enum PaymentStatus { Pending, Success, Failed, Refunded }

        public enum MilestoneStatus { Planned, InProgress, Done, Cancelled }

        public enum AttachmentOwnerType { Quote, Contract, Invoice, Project, Customer }
        public enum LexwareType { Imported, Exported ,  NotExported }
        public enum BillingCycle { OneTime = 0,Monthly = 1, Quarterly = 2,SemiAnnual = 3,Annual = 4}
        public enum InvoiceSendMode{Automatic = 0,Manual = 1}
        public enum InvoiceOriginType
        {
            Manual = 0,
            ContractOneTime = 1,
            ContractRecurring = 2,
            QuoteOneTime = 3,
            QuoteRecurring = 4
        }

        public enum InvoiceDispatchStatus
        {
            NotRequired = 0,
            PendingManualSend = 1,
            SentAutomatically = 2,
            SentManually = 3,
            Failed = 4
        }
        public enum ServiceUnitType
        {
            Custom = 0,
            Piece = 1,
            Hour = 2,
            Day = 3,
            Month = 4,
            FlatRate = 5,
            Package = 6,
            Project = 7
        }
    }
}
