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
