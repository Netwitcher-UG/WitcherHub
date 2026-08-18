using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Pages.Models.UI
{
    /// <summary>
    /// How a document status is shown: wording, colour and icon.
    ///
    /// One place, because <see cref="DocumentStatus"/> is shared by quotes,
    /// contracts and invoices while meaning something different in each — a quote
    /// that is <c>Accepted</c> is won, an invoice that is <c>Open</c> is money owed,
    /// and <c>Sent</c> on a quote is waiting on the customer while on an invoice it
    /// is waiting on payment. Pages used to each decide their own colours, so the
    /// same status appeared grey on one screen and green on the next.
    /// </summary>
    public sealed record StatusPresentation(string Label, string Tone, string Icon)
    {
        /// <summary>Bootstrap contextual name: success, danger, warning, info, secondary.</summary>
        public string Tone { get; } = Tone;

        /// <summary>The theme's badge classes for this tone.</summary>
        public string BadgeClass => Tone switch
        {
            "success" => "bg-success-focus text-success-main border border-success-main",
            "danger" => "bg-danger-focus text-danger-main border border-danger-main",
            "warning" => "bg-warning-focus text-warning-main border border-warning-main",
            "info" => "bg-info-focus text-info-main border border-info-main",
            "primary" => "bg-primary-50 text-primary-600 border border-primary-600",
            _ => "bg-neutral-200 text-neutral-600 border border-neutral-400"
        };
    }

    /// <summary>
    /// Renders a badge, once, for everything that is not a document status.
    ///
    /// Two badge idioms had grown up side by side: the theme's
    /// <c>bg-success-focus text-success-main border …</c> on the newer pages, and
    /// plain Bootstrap <c>bg-success bg-opacity-10 text-success</c> on the older
    /// ones. The same green meant two different shades depending on which screen
    /// you were looking at, which is most of what "it looks like several
    /// products" amounts to.
    ///
    /// Anything with a status — active or not, a company or a person, exported or
    /// not — comes through here and comes out looking the same.
    /// </summary>
    public static class Badge
    {
        /// <summary>
        /// A badge in the theme's own markup. <paramref name="tone"/> takes the
        /// contextual names: success, danger, warning, info, primary, or anything
        /// else for neutral.
        /// </summary>
        public static Microsoft.AspNetCore.Html.IHtmlContent Html(string label, string tone = "neutral")
        {
            var classes = new StatusPresentation(label, tone, "").BadgeClass;

            return new Microsoft.AspNetCore.Html.HtmlString(
                $"<span class=\"badge {classes} px-16 py-4 radius-4\">" +
                System.Text.Encodings.Web.HtmlEncoder.Default.Encode(label) +
                "</span>");
        }

        /// <summary>A yes/no state, green when true and quiet when not.</summary>
        public static Microsoft.AspNetCore.Html.IHtmlContent Toggle(bool on, string whenOn, string whenOff) =>
            Html(on ? whenOn : whenOff, on ? "success" : "neutral");
    }

    public static class DocumentStatusPresentation
    {
        /// <summary>
        /// A quote is a proposal: it is waiting, won, or lost.
        /// </summary>
        public static StatusPresentation ForQuote(DocumentStatus status) => status switch
        {
            DocumentStatus.Draft => new("Draft", "secondary", "ri-draft-line"),
            DocumentStatus.Sent => new("Awaiting customer", "warning", "ri-send-plane-line"),
            DocumentStatus.Accepted => new("Accepted", "success", "ri-check-double-line"),
            DocumentStatus.Signed => new("Signed", "success", "ri-quill-pen-line"),
            DocumentStatus.Rejected => new("Rejected", "danger", "ri-close-circle-line"),
            DocumentStatus.Cancelled => new("Cancelled", "secondary", "ri-close-circle-line"),
            DocumentStatus.Void => new("Void", "secondary", "ri-forbid-line"),
            _ => new(status.ToString(), "secondary", "ri-file-list-3-line")
        };

        /// <summary>
        /// A contract is an agreement: it is being prepared, live, or finished.
        /// </summary>
        public static StatusPresentation ForContract(DocumentStatus status) => status switch
        {
            DocumentStatus.Draft => new("Draft", "secondary", "ri-draft-line"),
            DocumentStatus.Sent => new("Awaiting signature", "warning", "ri-send-plane-line"),
            DocumentStatus.Signed => new("Signed", "success", "ri-quill-pen-line"),
            DocumentStatus.Accepted => new("Accepted", "success", "ri-check-double-line"),
            DocumentStatus.Rejected => new("Rejected", "danger", "ri-close-circle-line"),
            DocumentStatus.Terminated => new("Terminated", "danger", "ri-stop-circle-line"),
            DocumentStatus.Cancelled => new("Cancelled", "secondary", "ri-close-circle-line"),
            DocumentStatus.Void => new("Void", "secondary", "ri-forbid-line"),
            _ => new(status.ToString(), "secondary", "ri-file-list-3-line")
        };

        /// <summary>
        /// An invoice is money: it is unbilled, owed, late, or settled.
        /// </summary>
        public static StatusPresentation ForInvoice(DocumentStatus status) => status switch
        {
            DocumentStatus.Draft => new("Draft", "secondary", "ri-draft-line"),
            DocumentStatus.Issued => new("Issued", "info", "ri-file-paper-2-line"),
            DocumentStatus.Sent => new("Sent", "info", "ri-send-plane-line"),
            DocumentStatus.Open => new("Open", "warning", "ri-time-line"),
            DocumentStatus.Overdue => new("Overdue", "danger", "ri-alarm-warning-line"),
            DocumentStatus.Paid => new("Paid", "success", "ri-checkbox-circle-line"),
            DocumentStatus.Cancelled => new("Cancelled", "secondary", "ri-close-circle-line"),
            DocumentStatus.Void => new("Void", "secondary", "ri-forbid-line"),
            _ => new(status.ToString(), "secondary", "ri-file-list-3-line")
        };

        public static StatusPresentation ForProject(ProjectStatus status) => status switch
        {
            ProjectStatus.Draft => new("Draft", "secondary", "ri-draft-line"),
            ProjectStatus.Active => new("Active", "success", "ri-play-circle-line"),
            ProjectStatus.OnHold => new("On hold", "warning", "ri-pause-circle-line"),
            ProjectStatus.Closed => new("Closed", "info", "ri-archive-line"),
            ProjectStatus.Cancelled => new("Cancelled", "danger", "ri-close-circle-line"),
            _ => new(status.ToString(), "secondary", "ri-folder-line")
        };

        public static StatusPresentation ForPayment(PaymentStatus status) => status switch
        {
            PaymentStatus.Success => new("Received", "success", "ri-checkbox-circle-line"),
            PaymentStatus.Pending => new("Pending", "warning", "ri-time-line"),
            PaymentStatus.Failed => new("Failed", "danger", "ri-close-circle-line"),
            PaymentStatus.Refunded => new("Refunded", "info", "ri-refund-2-line"),
            _ => new(status.ToString(), "secondary", "ri-money-euro-circle-line")
        };

        public static StatusPresentation ForPaymentMethod(PaymentMethod method) => method switch
        {
            PaymentMethod.Bank => new("Bank transfer", "info", "ri-bank-line"),
            PaymentMethod.Cash => new("Cash", "info", "ri-money-euro-box-line"),
            PaymentMethod.Card => new("Card", "info", "ri-bank-card-line"),
            PaymentMethod.Online => new("Online", "info", "ri-global-line"),
            _ => new("Other", "secondary", "ri-money-euro-circle-line")
        };

        /// <summary>
        /// Which statuses mean a document is still in play, per document kind. Used
        /// by the dashboard so "open quote value" and "outstanding invoices" are
        /// defined in one place rather than repeated in each query.
        /// </summary>
        public static readonly DocumentStatus[] QuoteAwaitingDecision =
            [DocumentStatus.Sent];

        public static readonly DocumentStatus[] QuoteWon =
            [DocumentStatus.Accepted, DocumentStatus.Signed];

        public static readonly DocumentStatus[] ContractLive =
            [DocumentStatus.Signed, DocumentStatus.Accepted];

        public static readonly DocumentStatus[] ContractAwaitingSignature =
            [DocumentStatus.Sent];

        public static readonly DocumentStatus[] InvoiceOwed =
            [DocumentStatus.Issued, DocumentStatus.Sent, DocumentStatus.Open, DocumentStatus.Overdue];
    }
}
