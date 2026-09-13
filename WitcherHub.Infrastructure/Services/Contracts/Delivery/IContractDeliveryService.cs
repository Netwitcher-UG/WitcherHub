using WitcherHub.Application.Services.Contracts.Delivery;
using WitcherHub.Infrastructure.Data.Models;

namespace WitcherHub.Infrastructure.Services.Contracts.Delivery
{
    /// <summary>
    /// Everything Project Details needs to show about sending a contract.
    /// </summary>
    public sealed record ContractDeliveryState
    {
        public Guid ContractId { get; init; }
        public string ContractNo { get; init; } = "";
        public string CustomerName { get; init; } = "";

        public int? ApprovedVersion { get; init; }
        public DateTimeOffset? ApprovedAt { get; init; }
        public bool IsSigned { get; init; }

        /// <summary>None when the contract may be sent.</summary>
        public DeliveryRefusal Refusal { get; init; }

        public IReadOnlyList<DeliveryRecipient> Recipients { get; init; } = [];

        public ContractSignatureRequestStatus? LatestRequestStatus { get; init; }
        public DateTimeOffset? LatestRequestSentAt { get; init; }
        public DateTimeOffset? LatestRequestViewedAt { get; init; }
        public DateTimeOffset? LatestRequestExpiresAt { get; init; }
        public string? LatestRequestRecipient { get; init; }
        public int? LatestRequestVersion { get; init; }
        public Guid? LatestRequestId { get; init; }

        public bool CanSend => Refusal == DeliveryRefusal.None;

        /// <summary>
        /// E-mail needs an address; a copied link does not. Disabled rather than
        /// hidden, so the reason can be shown next to it.
        /// </summary>
        public bool CanEmail => CanSend && Recipients.Count > 0;

        public static ContractDeliveryState Unknown { get; } =
            new() { Refusal = DeliveryRefusal.NotApproved };
    }

    /// <summary>
    /// Issues and tracks the links a customer signs through.
    ///
    /// Behind one interface because every one of these operations has to agree
    /// about the same three rules: only an approved version may be sent, what is
    /// sent is frozen at that moment, and a link that has been superseded,
    /// cancelled, used or has expired leads nowhere.
    /// </summary>
    public interface IContractDeliveryService
    {
        Task<ContractDeliveryState> DescribeAsync(Guid contractId, CancellationToken ct = default);

        /// <summary>
        /// Issues a request and returns the complete signing URL once. The URL is
        /// never stored or logged — only the hash of its token.
        /// </summary>
        Task<ContractDeliveryResult> CreateLinkAsync(
            Guid contractId,
            ContractDeliveryMethod method,
            string publicBaseUrl,
            Guid? actorUserId,
            string? recipientEmail = null,
            CancellationToken ct = default);

        /// <summary>Called only once the mail has actually gone out.</summary>
        Task MarkSentAsync(Guid requestId, CancellationToken ct = default);

        Task MarkDeliveryFailedAsync(Guid requestId, string reason, CancellationToken ct = default);

        Task<ContractDeliveryResult> CancelAsync(
            Guid requestId, Guid? actorUserId, CancellationToken ct = default);
    }
}
