using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using WitcherHub.Domain.Commen;

namespace WitcherHub.Infrastructure.Data.Models
{
    /// <summary>How a signing link reached the customer.</summary>
    public enum ContractDeliveryMethod
    {
        /// <summary>An authorised user copied it and sent it themselves.</summary>
        CopiedLink = 0,

        /// <summary>The application e-mailed it to the stored customer address.</summary>
        Email = 1
    }

    /// <summary>
    /// Where a signature request is in its life.
    ///
    /// Stored as a string so a row read years from now still says what it means,
    /// and so inserting a state does not renumber the existing ones.
    /// </summary>
    public enum ContractSignatureRequestStatus
    {
        /// <summary>Issued. The link exists and has not been sent by the system.</summary>
        Created = 0,

        /// <summary>E-mailed successfully. Never set when delivery failed.</summary>
        Sent = 1,

        /// <summary>The customer has opened it.</summary>
        Viewed = 2,

        /// <summary>Opened, terms accepted, signature not yet submitted.</summary>
        AwaitingSignature = 3,

        /// <summary>Signed. The request is consumed and cannot be used again.</summary>
        Signed = 4,

        Expired = 5,
        Cancelled = 6,

        /// <summary>The wording it was issued for is no longer the contract's.</summary>
        Superseded = 7
    }

    public class ContractAccessLink : BaseEntity
    {
        public Guid ContractId { get; set; }
        public Contract Contract { get; set; } = default!;

        [MaxLength(128)]
        public string TokenHash { get; set; } = default!; // SHA256 hex

        [MaxLength(320)]
        public string RecipientEmail { get; set; } = default!;

        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset? LastOpenedAtUtc { get; set; }
        public DateTimeOffset? RevokedAtUtc { get; set; }

        /// <summary>
        /// The approved version of the wording this link was issued against.
        ///
        /// A signing link points at the contract, not at a document, so it shows
        /// whatever the contract says when it is opened. That was fine while the
        /// wording could not change underneath it — and it can: approving a new
        /// version replaces the text the link leads to. The customer who was sent
        /// "please sign this" for one document would then sign a different one,
        /// having been told nothing.
        ///
        /// Recording the version is what makes that detectable. When a different
        /// version is approved, every outstanding link issued for the old one is
        /// revoked, and the person is asked for a fresh link rather than shown
        /// wording they were never sent.
        ///
        /// Null on links issued before this existed: their version is unknown, so
        /// they are left alone rather than revoked on a guess.
        /// </summary>
        public int? IssuedForDraftVersion { get; set; }

        /// <summary>
        /// Set when the link was revoked because the wording it was issued for is
        /// no longer the contract's wording — as opposed to being revoked because
        /// a newer link was sent to the same person. The signing page says
        /// different things about the two, because they need different answers.
        /// </summary>
        public bool RevokedBecauseWordingChanged { get; set; }

        // ═══════════════════════════════════════════════════════════════════
        // The signature request
        //
        // This row is what was sent to a customer, and it has to stay answerable
        // long after the contract has moved on. Recording the version was not
        // enough: the signing page read the contract's current wording and, when
        // that was empty, rebuilt it from whichever draft happened to be approved
        // — so the document a customer opened was the document as it stood at
        // that moment, not the one the link was issued for. A link sent on Monday
        // could show Tuesday's wording with nothing on the page to say so.
        //
        // The wording is therefore frozen here, at the moment of sending, and the
        // page renders this and nothing else.
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// The approved wording, exactly as it stood when the link was issued.
        ///
        /// Immutable once written. Everything the customer reads comes from here,
        /// so a later approval, translation or regeneration cannot change what
        /// they were asked to sign — it produces a new version, revokes this link,
        /// and needs sending again.
        /// </summary>
        public string? SnapshotMarkdown { get; set; }

        /// <summary>
        /// SHA-256 of <see cref="SnapshotMarkdown"/>, so "this is the text that
        /// was sent" is checkable rather than asserted. Recomputed and compared
        /// before a signature is accepted.
        /// </summary>
        [MaxLength(64)]
        public string? SnapshotHash { get; set; }

        /// <summary>How the link reached the customer.</summary>
        public ContractDeliveryMethod DeliveryMethod { get; set; } = ContractDeliveryMethod.CopiedLink;

        /// <summary>Where this request is in its life.</summary>
        public ContractSignatureRequestStatus Status { get; set; } = ContractSignatureRequestStatus.Created;

        public DateTimeOffset? SentAtUtc { get; set; }
        public DateTimeOffset? ViewedAtUtc { get; set; }

        /// <summary>When the link was used to sign. A request is single-use.</summary>
        public DateTimeOffset? ConsumedAtUtc { get; set; }

        public DateTimeOffset? CancelledAtUtc { get; set; }

        public Guid? CreatedByUserId { get; set; }

        // ── Terms evidence ──────────────────────────────────────────────────
        //
        // A URL on its own proves nothing: the page behind it can change the day
        // after somebody signs, and then nobody can say what was accepted. The
        // version and the hash of the wording that was in force when the request
        // was created are recorded with it.

        [MaxLength(500)]
        public string? TermsUrl { get; set; }

        [MaxLength(40)]
        public string? TermsVersion { get; set; }

        [MaxLength(64)]
        public string? TermsHash { get; set; }

        public bool IsRevoked => RevokedAtUtc != null;

        /// <summary>
        /// Whether this request may still be signed against.
        ///
        /// Every condition is checked on the server before the signing form is
        /// rendered and again before a signature is accepted. Hiding the form in
        /// the view would leave the endpoint open.
        /// </summary>
        public bool AllowsSigning(DateTimeOffset now) =>
            RevokedAtUtc is null &&
            ConsumedAtUtc is null &&
            CancelledAtUtc is null &&
            ExpiresAt > now &&
            Status is ContractSignatureRequestStatus.Created
                   or ContractSignatureRequestStatus.Sent
                   or ContractSignatureRequestStatus.Viewed
                   or ContractSignatureRequestStatus.AwaitingSignature;

        public static string HashToken(string token)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(token));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
