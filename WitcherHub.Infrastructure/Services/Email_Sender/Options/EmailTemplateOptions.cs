namespace WitcherHub.Infrastructure.Services.Email_Sender.Options
{
    public sealed class EmailTemplateOptions
    {
        public string TemplatesFolder { get; init; } = "EmailTemplates";
        public string MessagesFolder { get; init; } = "Messages";
        public string LayoutFileName { get; init; } = "_Layout.html";

        // Branding (ثابتة للكل)
        public string AppName { get; init; } = "WitcherHub";
        public string BrandLine { get; init; } = "System Notification";
        public string FooterText { get; init; } = "هذه رسالة تلقائية — لا تقم بالرد عليها.";
        public string LegalLine { get; init; } = "© {{Year}} WitcherHub. All rights reserved.";
    }
}
