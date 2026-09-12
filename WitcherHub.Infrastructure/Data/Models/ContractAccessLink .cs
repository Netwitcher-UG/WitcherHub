using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using WitcherHub.Domain.Commen;

namespace WitcherHub.Infrastructure.Data.Models
{
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

        public bool IsRevoked => RevokedAtUtc != null;

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
