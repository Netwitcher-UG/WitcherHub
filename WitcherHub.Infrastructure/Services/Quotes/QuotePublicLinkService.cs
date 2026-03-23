using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;

namespace WitcherHub.Infrastructure.Services.Quotes
{
    public class QuotePublicLinkService
    {
        private readonly AppDbContext _db;

        public QuotePublicLinkService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<string> CreateAsync(
            Guid quoteId,
            string? recipientEmail,
            int expiresInDays = 14,
            CancellationToken ct = default)
        {
            var exists = await _db.Quotes
                .AsNoTracking()
                .AnyAsync(x => x.Id == quoteId, ct);

            if (!exists)
                throw new InvalidOperationException("Quote not found.");

            var rawToken = GenerateUrlSafeToken(32);
            var tokenHash = QuoteAccessLink.HashToken(rawToken);

            var link = new QuoteAccessLink
            {
                QuoteId = quoteId,
                RecipientEmail = string.IsNullOrWhiteSpace(recipientEmail) ? string.Empty : recipientEmail.Trim(),
                TokenHash = tokenHash,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(expiresInDays <= 0 ? 14 : expiresInDays),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                RevokedAtUtc = null,
                LastOpenedAtUtc = null
            };

            _db.QuoteAccessLinks.Add(link);
            await _db.SaveChangesAsync(ct);

            return rawToken;
        }

        public async Task<QuoteAccessLink?> ValidateActiveLinkAsync(
            string rawToken,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(rawToken))
                return null;

            var tokenHash = QuoteAccessLink.HashToken(rawToken.Trim());
            var now = DateTimeOffset.UtcNow;

            return await _db.QuoteAccessLinks
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.TokenHash == tokenHash &&
                    x.RevokedAtUtc == null &&
                    x.ExpiresAt > now,
                    ct);
        }

        public async Task MarkOpenedAsync(Guid linkId, CancellationToken ct = default)
        {
            var link = await _db.QuoteAccessLinks.FirstOrDefaultAsync(x => x.Id == linkId, ct);
            if (link is null)
                return;

            link.LastOpenedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        public async Task RevokeAsync(Guid linkId, CancellationToken ct = default)
        {
            var link = await _db.QuoteAccessLinks.FirstOrDefaultAsync(x => x.Id == linkId, ct);
            if (link is null)
                return;

            if (link.RevokedAtUtc == null)
            {
                link.RevokedAtUtc = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }

        private static string GenerateUrlSafeToken(int byteLength)
        {
            var bytes = RandomNumberGenerator.GetBytes(byteLength);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}