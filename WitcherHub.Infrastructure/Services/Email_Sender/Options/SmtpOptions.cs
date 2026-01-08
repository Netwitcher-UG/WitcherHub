namespace WitcherHub.Infrastructure.Services.Email_Sender.Options
{
    public sealed class SmtpOptions
    {
        public string Host { get; init; } = "";
        public int Port { get; init; } = 587;

        public bool UseSsl { get; init; } = false;      // SslOnConnect
        public bool UseStartTls { get; init; } = true;  // StartTls

        public string UserName { get; init; } = "";
        public string Password { get; init; } = "";

        public string FromEmail { get; init; } = "";
        public string FromName { get; init; } = "";

        public int TimeoutSeconds { get; init; } = 30;
    }
}
