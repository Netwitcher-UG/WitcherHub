using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using WitcherHub.Application.Interfaces.Email;
using WitcherHub.Application.Models.Email;
using WitcherHub.Infrastructure.Services.Email_Sender.Options;

namespace WitcherHub.Infrastructure.Services.Email_Sender.Sender
{
    public sealed class MailKitEmailSender : IEmailSender
    {
        private readonly SmtpOptions _opt;
        private readonly ILogger<MailKitEmailSender> _logger;

        public MailKitEmailSender(IOptions<SmtpOptions> opt, ILogger<MailKitEmailSender> logger)
        {
            _opt = opt.Value;
            _logger = logger;
        }

        public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            var mime = BuildMimeMessage(message);

            using var client = new SmtpClient
            {
                Timeout = Math.Max(1, _opt.TimeoutSeconds) * 1000
            };

            await client.ConnectAsync(_opt.Host, _opt.Port, ResolveSocketOptions(), ct);

            if (!string.IsNullOrWhiteSpace(_opt.UserName))
                await client.AuthenticateAsync(_opt.UserName, _opt.Password, ct);

            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);

            var attCount = message.Attachments?.Count ?? 0;
            var attBytes = message.Attachments?.Sum(a => a.Content?.Length ?? 0) ?? 0;

            _logger.LogInformation(
                "Email sent (BCC count: {BccCount}). Attachments: {AttCount} (bytes: {AttBytes}). Subject: {Subject}",
                message.Bcc?.Count ?? 0, attCount, attBytes, message.Subject);
        }

        private MimeMessage BuildMimeMessage(EmailMessage msg)
        {
            if (msg.Bcc is null || msg.Bcc.Count == 0)
                throw new InvalidOperationException("BCC recipients are required.");

            var mime = new MimeMessage();

            // From: من الإعدادات (يتجاهل placeholder القادم من Application)
            var from = new MailboxAddress(_opt.FromName, _opt.FromEmail);
            mime.From.Add(from);

            // كثير من السيرفرات لا تحب To فاضي، نضع To شكلي
            mime.To.Add(new MailboxAddress("Undisclosed Recipients", _opt.FromEmail));

            // BCC الحقيقي
            foreach (var b in msg.Bcc)
                mime.Bcc.Add(ToMailbox(b));

            if (msg.ReplyTo is not null)
                mime.ReplyTo.Add(ToMailbox(msg.ReplyTo));

            mime.Subject = msg.Subject;

            var builder = new BodyBuilder
            {
                HtmlBody = msg.HtmlBody,
                TextBody = msg.TextBody
            };

            if (msg.Attachments is not null)
            {
                foreach (var att in msg.Attachments)
                    builder.Attachments.Add(att.FileName, att.Content, ContentType.Parse(att.ContentType));
            }

            mime.Body = builder.ToMessageBody();
            return mime;

            static MailboxAddress ToMailbox(EmailAddress a)
                => string.IsNullOrWhiteSpace(a.Name)
                    ? new MailboxAddress(a.Email, a.Email)
                    : new MailboxAddress(a.Name, a.Email);
        }

        private SecureSocketOptions ResolveSocketOptions()
        {
            if (_opt.UseSsl) return SecureSocketOptions.SslOnConnect;
            if (_opt.UseStartTls) return SecureSocketOptions.StartTls;
            return SecureSocketOptions.None;
        }
    }
}
