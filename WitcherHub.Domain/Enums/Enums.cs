using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace WitcherHub.Infrastructure.Data.Models
{
    public class Enums
    {
        public enum CustomerType { Individual, Company }
        public enum ProjectStatus { Draft, Active, Closed, Cancelled }

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
        public enum ServiceType { Website, Design, Video, Editing, Other }
        public enum PricingModel { Fixed, Unit, Tiered, Hourly }

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
