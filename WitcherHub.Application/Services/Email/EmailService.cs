using WitcherHub.Application.Interfaces.BackgroundTasks;
using WitcherHub.Application.Interfaces.Email;
using WitcherHub.Application.Models.Email;

namespace WitcherHub.Application.Services.Email
{
    public interface IEmailService
    {
        Task SendNowAsync(EmailMessage message, CancellationToken ct = default);
        Task QueueNowAsync(EmailMessage message, CancellationToken ct = default);

        Task QueueTemplateAsync(
            string templateName,
            object model,
            EmailAddress to,
            string subject,
            CancellationToken ct = default);
    }

    public sealed class EmailService : IEmailService
    {
        private static readonly EmailAddress PlaceholderFrom =
            new("no-reply@invalid.local", "WitcherHub"); // سيتم استبداله في Infrastructure من Options

        private readonly IEmailSender _sender;
        private readonly IEmailTemplateRenderer _renderer;
        private readonly IBackgroundTaskQueue _queue;

        public EmailService(IEmailSender sender, IEmailTemplateRenderer renderer, IBackgroundTaskQueue queue)
        {
            _sender = sender;
            _renderer = renderer;
            _queue = queue;
        }

        public Task SendNowAsync(EmailMessage message, CancellationToken ct = default)
        {
            var snapshot = SnapshotAsBccOnly(message);
            return _sender.SendAsync(snapshot, ct);
        }

        public async Task QueueNowAsync(EmailMessage message, CancellationToken ct = default)
        {
            var snapshot = SnapshotAsBccOnly(message);

            await _queue.QueueAsync(async token =>
            {
                await _sender.SendAsync(snapshot, token);
            }, ct);
        }

        public async Task QueueTemplateAsync(
            string templateName,
            object model,
            EmailAddress to,
            string subject,
            CancellationToken ct = default)
        {
            await _queue.QueueAsync(async token =>
            {
                var html = await _renderer.RenderAsync(templateName, model, token);

                var msg = new EmailMessage
                {
                    From = PlaceholderFrom,
                    Subject = subject,
                    HtmlBody = html,
                    Bcc = [to],
                    To = []
                };

                await _sender.SendAsync(msg, token);
            }, ct);
        }


        /// <summary>
        /// يطبق سياسة "BCC only":
        /// - ينقل To -> Bcc
        /// - يفرغ To
        /// - يعمل نسخة (Snapshot) لتفادي أي تعديل لاحق على نفس الـ instance
        /// </summary>
        private static EmailMessage SnapshotAsBccOnly(EmailMessage source)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));

            var bcc = new List<EmailAddress>();

            // existing BCC
            if (source.Bcc is not null && source.Bcc.Count > 0)
                bcc.AddRange(source.Bcc);

            // move TO -> BCC
            if (source.To is not null && source.To.Count > 0)
                bcc.AddRange(source.To);

            // optional: remove duplicates by email (case-insensitive)
            bcc = bcc
                .Where(x => !string.IsNullOrWhiteSpace(x.Email))
                .GroupBy(x => x.Email.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (bcc.Count == 0)
                throw new InvalidOperationException("Email must have at least one recipient in To or Bcc.");

            var attachments = (source.Attachments ?? [])
                .Select(a => new EmailAttachment(a.FileName, a.ContentType, a.Content))
                .ToList();

            return new EmailMessage
            {
                From = source.From,
                Subject = source.Subject,
                HtmlBody = source.HtmlBody,
                TextBody = source.TextBody,
                ReplyTo = source.ReplyTo,
                Attachments = attachments,

                To = [],     // ✅ BCC only
                Bcc = bcc
            };
        }
    }
}
