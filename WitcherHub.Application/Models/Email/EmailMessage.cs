namespace WitcherHub.Application.Models.Email
{
    public sealed record EmailAddress(string Email, string? Name = null);

    public sealed record EmailAttachment(
        string FileName,
        string ContentType,
        byte[] Content
    );

    public sealed class EmailMessage
    {
        public required EmailAddress From { get; init; }
        public List<EmailAddress> To { get; init; } = [];
        public List<EmailAddress> Bcc { get; init; } = [];

        public required string Subject { get; init; }
        public string? HtmlBody { get; init; }
        public string? TextBody { get; init; }

        public List<EmailAttachment> Attachments { get; init; } = [];
        public EmailAddress? ReplyTo { get; init; }
    }
}
