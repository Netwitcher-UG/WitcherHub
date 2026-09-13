using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WitcherHub.Application.Services.Contracts.Delivery;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Infrastructure.Services.Contracts.Delivery
{
    /// <summary>
    /// Sends an approved contract to a customer, and freezes what was sent.
    ///
    /// One thing here matters more than the rest: the wording the customer reads
    /// is captured at the moment the link is issued and never read live again.
    /// Before this, the signing page rendered the contract's current terms and,
    /// when those were empty, rebuilt the document from whichever draft happened
    /// to be approved — so a link sent on Monday showed Tuesday's wording, with
    /// nothing on the page to say it had changed. A signature obtained that way
    /// is a signature on a document nobody sent.
    ///
    /// Everything else follows from that. The link carries a token that exists
    /// only in the e-mail; the database keeps its hash. The snapshot carries its
    /// own hash, recomputed before a signature is accepted. Approving different
    /// wording revokes outstanding links rather than quietly changing what they
    /// lead to.
    /// </summary>
    public sealed class ContractDeliveryService : IContractDeliveryService
    {
        private readonly AppDbContext _db;
        private readonly ContractTermsOptions _terms;
        private readonly ILogger<ContractDeliveryService> _logger;

        public ContractDeliveryService(
            AppDbContext db,
            IOptions<ContractTermsOptions> terms,
            ILogger<ContractDeliveryService> logger)
        {
            _db = db;
            _terms = terms.Value;
            _logger = logger;
        }

        // ============================================================ eligibility

        /// <summary>
        /// What the delivery section on Project Details needs to draw itself:
        /// whether this contract can be sent, to whom, and what the outstanding
        /// request (if any) is doing.
        /// </summary>
        public async Task<ContractDeliveryState> DescribeAsync(Guid contractId, CancellationToken ct = default)
        {
            var contract = await LoadAsync(contractId, ct);

            if (contract is null)
                return ContractDeliveryState.Unknown;

            var approved = ApprovedDraft(contract);
            var recipients = RecipientsOf(contract);

            var request = await _db.Set<ContractAccessLink>()
                .AsNoTracking()
                .Where(l => l.ContractId == contractId)
                .OrderByDescending(l => l.CreatedAtUtc)
                .FirstOrDefaultAsync(ct);

            return new ContractDeliveryState
            {
                ContractId = contract.Id,
                ContractNo = contract.ContractNo ?? "",
                CustomerName = contract.Project?.Customer?.Name ?? "",
                ApprovedVersion = approved?.Version,
                ApprovedAt = approved?.ApprovedAt,
                IsSigned = contract.Status == DocumentStatus.Signed,
                Refusal = Eligibility(contract, approved),
                Recipients = recipients,
                LatestRequestStatus = request?.Status,
                LatestRequestSentAt = request?.SentAtUtc,
                LatestRequestViewedAt = request?.ViewedAtUtc,
                LatestRequestExpiresAt = request?.RevokedAtUtc is null ? request?.ExpiresAt : null,
                LatestRequestRecipient = request?.RecipientEmail,
                LatestRequestVersion = request?.IssuedForDraftVersion,
                LatestRequestId = request?.Id
            };
        }

        /// <summary>
        /// Whether this contract may be sent at all, and if not, which of the
        /// several different reasons applies.
        /// </summary>
        private static DeliveryRefusal Eligibility(Contract contract, ContractDraft? approved)
        {
            if (contract.Status == DocumentStatus.Signed) return DeliveryRefusal.AlreadySigned;

            if (approved is null) return DeliveryRefusal.NotApproved;

            if (approved.Status == ContractDraftStatus.Superseded) return DeliveryRefusal.Superseded;

            if (string.IsNullOrWhiteSpace(approved.DocumentMarkdown)) return DeliveryRefusal.NoContent;

            return DeliveryRefusal.None;
        }

        // ================================================================ issuing

        /// <summary>
        /// Issues a signing link for the contract's approved wording.
        ///
        /// The complete URL comes back exactly once, to this caller. Nothing
        /// stores it and nothing logs it: what is kept is the SHA-256 of the
        /// token, so a copy of the database is not a set of signing links.
        /// </summary>
        public async Task<ContractDeliveryResult> CreateLinkAsync(
            Guid contractId,
            ContractDeliveryMethod method,
            string publicBaseUrl,
            Guid? actorUserId,
            string? recipientEmail = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(publicBaseUrl))
            {
                // Building it from the request's Host header is how a forged
                // header sends a customer's signing link to somebody else's site.
                return ContractDeliveryResult.Refused(
                    DeliveryRefusal.NoPublicUrl,
                    "Es ist keine öffentliche Basis-URL konfiguriert, daher kann kein " +
                    "gültiger Signaturlink erzeugt werden.");
            }

            var contract = await LoadAsync(contractId, ct);

            if (contract is null)
                return ContractDeliveryResult.Refused(DeliveryRefusal.NotApproved, "Vertrag nicht gefunden.");

            var approved = ApprovedDraft(contract);
            var refusal = Eligibility(contract, approved);

            if (refusal != DeliveryRefusal.None)
                return ContractDeliveryResult.Refused(refusal, Explain(refusal));

            var recipient = ChooseRecipient(contract, recipientEmail, out var choiceNeeded, out var choices);

            if (choiceNeeded) return ContractDeliveryResult.NeedsChoice(choices);

            // Only e-mail delivery needs a recipient. A copied link is handed
            // over by the person who copied it, and demanding an address they
            // are not going to use would block a perfectly ordinary case.
            if (method == ContractDeliveryMethod.Email && recipient is null)
            {
                return ContractDeliveryResult.Refused(
                    recipientEmail is null ? DeliveryRefusal.NoRecipient : DeliveryRefusal.UnknownRecipient,
                    Explain(recipientEmail is null ? DeliveryRefusal.NoRecipient : DeliveryRefusal.UnknownRecipient));
            }

            var snapshot = approved!.DocumentMarkdown!.Replace("\r\n", "\n");

            var token = NewToken();
            var now = DateTimeOffset.UtcNow;

            var request = new ContractAccessLink
            {
                Id = Guid.NewGuid(),
                ContractId = contract.Id,
                TokenHash = ContractAccessLink.HashToken(token),
                RecipientEmail = recipient?.Address ?? "",
                ExpiresAt = now.AddDays(Math.Clamp(_terms.LinkValidDays, 1, 365)),
                CreatedAtUtc = now,
                IssuedForDraftVersion = approved.Version,

                // Frozen here. Nothing downstream reads the contract's live
                // wording again for this request.
                SnapshotMarkdown = snapshot,
                SnapshotHash = Sha256(snapshot),

                DeliveryMethod = method,
                Status = ContractSignatureRequestStatus.Created,
                CreatedByUserId = actorUserId,

                TermsUrl = _terms.Url,
                TermsVersion = string.IsNullOrWhiteSpace(_terms.Version) ? null : _terms.Version,
                TermsHash = string.IsNullOrWhiteSpace(_terms.Hash) ? null : _terms.Hash
            };

            // Any link still outstanding for this contract is replaced, not left
            // beside the new one: two live links to one contract is two documents
            // somebody could sign.
            await SupersedeOutstandingAsync(contract.Id, request.Id, ct);

            _db.Add(request);

            await AuditAsync(contract, request, actorUserId,
                method == ContractDeliveryMethod.Email ? "SignatureEmailRequested" : "SignatureLinkCreated", ct);

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Signature request {RequestId} created for contract {ContractNo} version {Version}, " +
                "method {Method}, expires {Expires}. Snapshot {Hash}.",
                request.Id, contract.ContractNo, approved.Version, method, request.ExpiresAt,
                request.SnapshotHash);

            return new ContractDeliveryResult
            {
                Succeeded = true,
                RequestId = request.Id,
                SentVersion = approved.Version,
                ExpiresAt = request.ExpiresAt,

                // The one and only time this string exists outside the customer's
                // mailbox.
                SigningUrl = BuildUrl(publicBaseUrl, contract.Id, token)
            };
        }

        /// <summary>
        /// Marks a request as e-mailed. Called only after the mail actually went
        /// out — a request whose delivery failed stays Created, so the page does
        /// not tell the owner a customer has been written to when nobody has.
        /// </summary>
        public async Task MarkSentAsync(Guid requestId, CancellationToken ct = default)
        {
            var request = await _db.Set<ContractAccessLink>().FirstOrDefaultAsync(l => l.Id == requestId, ct);

            if (request is null) return;

            request.Status = ContractSignatureRequestStatus.Sent;
            request.SentAtUtc = DateTimeOffset.UtcNow;

            await AuditAsync(null, request, request.CreatedByUserId, "SignatureEmailSent", ct);
            await _db.SaveChangesAsync(ct);
        }

        /// <summary>
        /// Records that delivery failed and leaves the request unsent.
        /// </summary>
        public async Task MarkDeliveryFailedAsync(
            Guid requestId, string reason, CancellationToken ct = default)
        {
            var request = await _db.Set<ContractAccessLink>().FirstOrDefaultAsync(l => l.Id == requestId, ct);

            if (request is null) return;

            // Revoked rather than merely unsent: a token that went nowhere is a
            // token nobody should be able to use, and the owner will issue a new
            // one when they retry.
            request.RevokedAtUtc = DateTimeOffset.UtcNow;
            request.Status = ContractSignatureRequestStatus.Cancelled;
            request.CancelledAtUtc = DateTimeOffset.UtcNow;

            await AuditAsync(null, request, request.CreatedByUserId, "SignatureEmailFailed", ct, reason);
            await _db.SaveChangesAsync(ct);
        }

        /// <summary>Cancels an outstanding request at the owner's request.</summary>
        public async Task<ContractDeliveryResult> CancelAsync(
            Guid requestId, Guid? actorUserId, CancellationToken ct = default)
        {
            var request = await _db.Set<ContractAccessLink>().FirstOrDefaultAsync(l => l.Id == requestId, ct);

            if (request is null)
                return ContractDeliveryResult.Refused(DeliveryRefusal.NotApproved, "Anfrage nicht gefunden.");

            if (request.ConsumedAtUtc is not null)
            {
                return ContractDeliveryResult.Refused(
                    DeliveryRefusal.AlreadySigned,
                    "Dieser Vertrag wurde bereits unterzeichnet und kann nicht storniert werden.");
            }

            var now = DateTimeOffset.UtcNow;

            request.RevokedAtUtc ??= now;
            request.CancelledAtUtc ??= now;
            request.Status = ContractSignatureRequestStatus.Cancelled;

            await AuditAsync(null, request, actorUserId, "SignatureRequestCancelled", ct);
            await _db.SaveChangesAsync(ct);

            return new ContractDeliveryResult { Succeeded = true, RequestId = request.Id };
        }

        // ============================================================== recipients

        /// <summary>
        /// The customer's e-mail addresses, as Project Details holds them.
        ///
        /// The owner is never asked to retype an address the record already has.
        /// Where there are several, one is not picked for them: a contract sent
        /// to the wrong address at a customer is a contract somebody else read.
        /// </summary>
        public static IReadOnlyList<DeliveryRecipient> RecipientsOf(Contract contract)
        {
            var customer = contract.Project?.Customer;

            if (customer is null) return [];

            var found = new List<DeliveryRecipient>();

            foreach (var contact in customer.Contacts ?? [])
            {
                if (!IsEmail(contact.Email)) continue;

                found.Add(new DeliveryRecipient(
                    contact.Email!.Trim(),
                    string.IsNullOrWhiteSpace(contact.Name) ? "Kontakt" : contact.Name,
                    contact.IsPrimary));
            }

            foreach (var address in customer.EmailAddresses ?? [])
            {
                if (!IsEmail(address.Email)) continue;

                found.Add(new DeliveryRecipient(
                    address.Email!.Trim(),
                    string.IsNullOrWhiteSpace(address.Kind) ? "E-Mail" : address.Kind!,
                    false));
            }

            return found
                .GroupBy(r => r.Address, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(r => r.IsPrimary).First())
                .OrderByDescending(r => r.IsPrimary)
                .ThenBy(r => r.Address, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// The address to send to: the one asked for, the only one on file, or
        /// none — with "choose" reported separately from "there are none",
        /// because those need different things from the person.
        /// </summary>
        private static DeliveryRecipient? ChooseRecipient(
            Contract contract,
            string? requested,
            out bool choiceNeeded,
            out IReadOnlyList<DeliveryRecipient> choices)
        {
            choices = RecipientsOf(contract);
            choiceNeeded = false;

            if (!string.IsNullOrWhiteSpace(requested))
            {
                // Only an address the customer actually has. Accepting a typed
                // one would make this a way to send a contract anywhere.
                return choices.FirstOrDefault(
                    r => string.Equals(r.Address, requested.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (choices.Count == 1) return choices[0];

            if (choices.Count > 1)
            {
                var primary = choices.Where(r => r.IsPrimary).ToList();

                if (primary.Count == 1) return primary[0];

                choiceNeeded = true;
                return null;
            }

            return null;
        }

        internal static bool IsEmail(string? value)
        {
            var text = (value ?? "").Trim();

            if (text.Length < 5 || text.Length > 320) return false;

            var at = text.IndexOf('@');

            if (at <= 0 || at != text.LastIndexOf('@') || at == text.Length - 1) return false;

            var domain = text[(at + 1)..];

            return domain.Contains('.', StringComparison.Ordinal) &&
                   !domain.StartsWith('.') && !domain.EndsWith('.') &&
                   !text.Any(char.IsWhiteSpace);
        }

        // ================================================================= helpers

        private async Task<Contract?> LoadAsync(Guid contractId, CancellationToken ct) =>
            await _db.Contracts
                .Include(c => c.Project).ThenInclude(p => p.Customer).ThenInclude(cu => cu.Contacts)
                .Include(c => c.Project).ThenInclude(p => p.Customer).ThenInclude(cu => cu.EmailAddresses)
                .Include(c => c.Drafts)
                .AsSplitQuery()
                .FirstOrDefaultAsync(c => c.Id == contractId, ct);

        private static ContractDraft? ApprovedDraft(Contract contract) =>
            contract.Drafts?
                .Where(d => d.IsApproved)
                .OrderByDescending(d => d.ApprovedAt)
                .FirstOrDefault();

        private async Task SupersedeOutstandingAsync(Guid contractId, Guid keep, CancellationToken ct)
        {
            var outstanding = await _db.Set<ContractAccessLink>()
                .Where(l => l.ContractId == contractId && l.Id != keep && l.RevokedAtUtc == null)
                .ToListAsync(ct);

            var now = DateTimeOffset.UtcNow;

            foreach (var link in outstanding)
            {
                link.RevokedAtUtc = now;
                link.Status = ContractSignatureRequestStatus.Superseded;
            }
        }

        /// <summary>
        /// A token with 256 bits of cryptographically secure randomness, in a
        /// form that survives a URL and a mail client without being re-encoded.
        /// </summary>
        private static string NewToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);

            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        private static string BuildUrl(string baseUrl, Guid contractId, string token) =>
            $"{baseUrl.TrimEnd('/')}/contracts/sign/{contractId}?t={Uri.EscapeDataString(token)}";

        public static string Sha256(string value)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? ""));
            var text = new StringBuilder(bytes.Length * 2);

            foreach (var b in bytes) text.Append(b.ToString("x2"));

            return text.ToString();
        }

        private static string Explain(DeliveryRefusal refusal) => refusal switch
        {
            DeliveryRefusal.NotApproved =>
                "Dieser Vertrag hat keine freigegebene Fassung. Bitte geben Sie zuerst eine Version frei.",
            DeliveryRefusal.Superseded =>
                "Die freigegebene Fassung wurde inzwischen ersetzt. Bitte geben Sie die aktuelle Version frei.",
            DeliveryRefusal.AlreadySigned =>
                "Dieser Vertrag ist bereits unterzeichnet.",
            DeliveryRefusal.NoContent =>
                "Die freigegebene Fassung enthält keinen Vertragstext.",
            DeliveryRefusal.NoRecipient =>
                "Für diesen Kunden ist keine gültige E-Mail-Adresse hinterlegt.",
            DeliveryRefusal.UnknownRecipient =>
                "Die gewählte E-Mail-Adresse gehört nicht zu diesem Kunden.",
            DeliveryRefusal.NoPublicUrl =>
                "Es ist keine öffentliche Basis-URL konfiguriert.",
            DeliveryRefusal.DeliveryFailed =>
                "Die E-Mail konnte nicht zugestellt werden. Es wurde nichts versendet.",
            _ => "Der Vertrag kann derzeit nicht versendet werden."
        };

        /// <summary>
        /// Appends an audit event.
        ///
        /// The token never appears here. What is recorded is the request, the
        /// version, the snapshot hash and the recipient — enough to reconstruct
        /// what happened without holding anything that could be used to sign.
        /// </summary>
        private async Task AuditAsync(
            Contract? contract,
            ContractAccessLink request,
            Guid? actorUserId,
            string action,
            CancellationToken ct,
            string? note = null)
        {
            _db.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorUserId = actorUserId,
                EntityType = "contract",
                EntityId = request.ContractId,
                Action = action,
                AfterData = JsonSerializer.SerializeToDocument(new
                {
                    requestId = request.Id,
                    contractNo = contract?.ContractNo,
                    version = request.IssuedForDraftVersion,
                    snapshotHash = request.SnapshotHash,
                    recipient = request.RecipientEmail,
                    method = request.DeliveryMethod.ToString(),
                    status = request.Status.ToString(),
                    termsVersion = request.TermsVersion,
                    expiresAt = request.ExpiresAt,
                    at = DateTimeOffset.UtcNow,
                    note
                })
            });

            await Task.CompletedTask;
        }
    }
}
