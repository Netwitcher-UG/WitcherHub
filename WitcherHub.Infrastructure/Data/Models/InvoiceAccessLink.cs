
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;

namespace WitcherHub.Infrastructure.Data.Models
{
    public class InvoiceAccessLink
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid InvoiceId { get; set; }
        public Invoice Invoice { get; set; } = default!;

        [MaxLength(320)]
        public string? RecipientEmail { get; set; }

        [MaxLength(128)]
        public string TokenHash { get; set; } = default!;

        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset ExpiresAt { get; set; }

        public bool OneTimeUse { get; set; } = false;

        public DateTimeOffset? FirstOpenedAtUtc { get; set; }
        public DateTimeOffset? LastOpenedAtUtc { get; set; }

        public int OpenCount { get; set; } = 0;

        public DateTimeOffset? RevokedAtUtc { get; set; }

        public static string HashToken(string rawToken)
        {
            if (string.IsNullOrWhiteSpace(rawToken))
                throw new ArgumentException("Token is required.", nameof(rawToken));

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
            return Convert.ToHexString(bytes);
        }
    }
}
