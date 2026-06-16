namespace WitcherHub.Infrastructure.Services.Lexware
{
    public sealed class LexwareWebhookOptions
    {
        public const string SectionName = "LexwareWebhooks";

        public bool VerifySignature { get; set; } = true;

        // ضع هنا الـ public key الرسمي من توثيق Lexware
        public string PublicKeyPem { get; set; } = string.Empty;
    }
}
