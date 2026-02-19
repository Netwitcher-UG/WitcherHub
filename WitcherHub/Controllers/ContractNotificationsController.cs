using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using WitcherHub.Application.Interfaces.Email;
using WitcherHub.Application.Models.Email;
using WitcherHub.Infrastructure.Data.Context;
using WitcherHub.Infrastructure.Data.Models;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Controllers
{
    [Route("contracts")]
    public sealed class ContractNotificationsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IEmailSender _emailSender;
        private readonly IEmailTemplateRenderer _renderer;

        public ContractNotificationsController(
            AppDbContext db,
            IEmailSender emailSender,
            IEmailTemplateRenderer renderer)
        {
            _db = db;
            _emailSender = emailSender;
            _renderer = renderer;
        }

        // POST /contracts/send?projectId=...
        [HttpPost("send")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Send([FromQuery] Guid projectId, CancellationToken ct)
        {
            if (projectId == Guid.Empty)
                return BadRequest(new { ok = false, toast = Toast("error", "Error", "Invalid project id.") });

            // ✅ اجلب آخر عقد للمشروع (أو غيره حسب منطقك)
            var contract = await _db.Contracts
                .Include(c => c.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(cu => cu.Contacts)
                .Include(c => c.Project)
                    .ThenInclude(p => p.Customer)
                        .ThenInclude(cu => cu.EmailAddresses)
                .Include(c => c.Items)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync(c => c.ProjectId == projectId, ct);

            // ✅ شرطك الأساسي: يجب وجود عقد
            if (contract is null)
                return NotFound(new { ok = false, toast = Toast("warning", "Not found", "No contract exists for this project.") });

            // احترافي: لا ترسل إذا Signed
            if (contract.Status == DocumentStatus.Signed || contract.SignedAt != null)
                return Conflict(new { ok = false, toast = Toast("warning", "Locked", "Contract is already signed.") });

            // احترافي: لا ترسل بدون line items
            if (contract.Items is null || contract.Items.Count == 0)
                return BadRequest(new { ok = false, toast = Toast("warning", "Missing items", "Please add at least one Position before sending.") });

            var customer = contract.Project.Customer;

            var recipientEmail =
                customer.Contacts?.OrderByDescending(x => x.IsPrimary).Select(x => x.Email).FirstOrDefault(e => !string.IsNullOrWhiteSpace(e))
                ?? customer.EmailAddresses?.Where(ea => ea.Kind == "business").Select(ea => ea.Email).FirstOrDefault(e => !string.IsNullOrWhiteSpace(e))
                ?? customer.EmailAddresses?.Select(ea => ea.Email).FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));

            if (string.IsNullOrWhiteSpace(recipientEmail))
                return BadRequest(new { ok = false, toast = Toast("error", "No email", "Customer has no email address.") });

            // ✅ أنشئ Token آمن
            var token = CreateUrlSafeToken(32);
            var tokenHash = ContractAccessLink.HashToken(token);

            // ✅ (اختياري محترف) Revocation لأي روابط فعالة سابقة لنفس العقد + نفس الإيميل
            await _db.ContractAccessLinks
                .Where(x => x.ContractId == contract.Id && x.RecipientEmail == recipientEmail && x.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.RevokedAtUtc, DateTimeOffset.UtcNow), ct);

            var access = new ContractAccessLink
            {
                ContractId = contract.Id,
                TokenHash = tokenHash,
                RecipientEmail = recipientEmail.Trim(),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(14)
            };

            _db.ContractAccessLinks.Add(access);

            // ✅ (اختياري) غيّر الحالة إلى Sent
            if (contract.Status == DocumentStatus.Draft)
                contract.Status = DocumentStatus.Sent;

            await _db.SaveChangesAsync(ct);

            // ✅ رابط المشاهدة (يجب يكون Absolute)
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var actionUrl = $"{baseUrl}/contracts/sign/{contract.Id}?t={Uri.EscapeDataString(token)}";

            var subject = $"Contract {contract.ContractNo}";

            // ✅ Render template
            var html = await _renderer.RenderAsync(
                templateName: "ContractReady",
                model: new
                {
                    Subject = subject,
                    CustomerName = customer.Name,
                    ContractNo = contract.ContractNo,
                    ProjectTitle = contract.Project.Title,
                    ActionUrl = actionUrl
                },
                ct);

            // ✅ بناء EmailMessage حسب نظامك (MailKitEmailSender يعتمد BCC)
            var msg = new EmailMessage
            {
                From = new EmailAddress("placeholder@local", "WitcherHub"), // سيتم تجاهلها داخل MailKitEmailSender
                Subject = subject,
                HtmlBody = html,
                TextBody = $"Your contract is ready: {actionUrl}",
                Bcc = new List<EmailAddress> { new EmailAddress(recipientEmail, customer.Name) }
            };

            await _emailSender.SendAsync(msg, ct);

            return Ok(new { ok = true, toast = Toast("success", "Sent", "Contract email has been sent to the customer.") });
        }

        private static object Toast(string type, string title, string message)
            => new { type, title, message };

        private static string CreateUrlSafeToken(int bytesLen)
        {
            var bytes = RandomNumberGenerator.GetBytes(bytesLen);
            return Convert.ToBase64String(bytes)
                .Replace("+", "-").Replace("/", "_").Replace("=", "");
        }
    }
}
