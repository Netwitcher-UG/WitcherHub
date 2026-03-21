using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;

namespace WitcherHub.Infrastructure.Services.Invoices
{
    public class InvoicePublicLinkService
    {
        private readonly AppDbContext _db;

        public InvoicePublicLinkService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<string> CreateAsync(
            Guid invoiceId,
            string? recipientEmail,
            int expiresInDays = 14,
            bool oneTimeUse = false,
            CancellationToken ct = default)
        {
            var exists = await _db.Invoices
                .AsNoTracking()
                .AnyAsync(x => x.Id == invoiceId, ct);

            if (!exists)
                throw new InvalidOperationException("Invoice not found.");

            var rawToken = GenerateUrlSafeToken(32);
            var tokenHash = InvoiceAccessLink.HashToken(rawToken);

            var link = new InvoiceAccessLink
            {
                InvoiceId = invoiceId,
                RecipientEmail = string.IsNullOrWhiteSpace(recipientEmail) ? null : recipientEmail.Trim(),
                TokenHash = tokenHash,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(expiresInDays <= 0 ? 14 : expiresInDays),
                OneTimeUse = oneTimeUse,
                RevokedAtUtc = null,
                FirstOpenedAtUtc = null,
                LastOpenedAtUtc = null,
                OpenCount = 0
            };

            _db.InvoiceAccessLinks.Add(link);
            await _db.SaveChangesAsync(ct);

            return rawToken;
        }

        public async Task<InvoiceAccessLink?> ValidateActiveLinkAsync(
            string rawToken,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(rawToken))
                return null;

            var tokenHash = InvoiceAccessLink.HashToken(rawToken);
            var now = DateTimeOffset.UtcNow;

            return await _db.InvoiceAccessLinks
                .Include(x => x.Invoice)
                .FirstOrDefaultAsync(x =>
                    x.TokenHash == tokenHash &&
                    x.RevokedAtUtc == null &&
                    x.ExpiresAt > now &&
                    (!x.OneTimeUse || x.FirstOpenedAtUtc == null),
                    ct);
        }

        public async Task MarkOpenedAsync(Guid linkId, CancellationToken ct = default)
        {
            var link = await _db.InvoiceAccessLinks.FirstOrDefaultAsync(x => x.Id == linkId, ct);
            if (link is null)
                return;

            var now = DateTimeOffset.UtcNow;

            if (link.FirstOpenedAtUtc == null)
                link.FirstOpenedAtUtc = now;

            link.LastOpenedAtUtc = now;
            link.OpenCount += 1;

            await _db.SaveChangesAsync(ct);
        }

        public async Task RevokeAsync(Guid linkId, CancellationToken ct = default)
        {
            var link = await _db.InvoiceAccessLinks.FirstOrDefaultAsync(x => x.Id == linkId, ct);
            if (link is null)
                return;

            if (link.RevokedAtUtc == null)
            {
                link.RevokedAtUtc = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }

        public async Task RevokeAllForInvoiceAsync(Guid invoiceId, CancellationToken ct = default)
        {
            var links = await _db.InvoiceAccessLinks
                .Where(x => x.InvoiceId == invoiceId && x.RevokedAtUtc == null)
                .ToListAsync(ct);

            if (links.Count == 0)
                return;

            var now = DateTimeOffset.UtcNow;
            foreach (var link in links)
                link.RevokedAtUtc = now;

            await _db.SaveChangesAsync(ct);
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
