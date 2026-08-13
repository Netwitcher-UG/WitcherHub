using WitcherHub.Infrastructure.Authentication;

namespace WitcherHub.Configuration.Extensions
{
    /// <summary>
    /// Fail-fast validation of required configuration, plus a start-up report of
    /// optional integrations that are not configured.
    ///
    /// Secrets are never read from source control: they come from user-secrets
    /// (local) or environment variables (Railway / CI). Missing values must
    /// therefore produce an actionable message naming the variable, instead of a
    /// null reference somewhere deep in a request.
    /// </summary>
    public static class StartupValidationExtensions
    {
        private const int MinimumJwtKeyLength = 32;

        /// <summary>
        /// Throws when a value the application cannot run without is missing.
        /// </summary>
        public static void ValidateRequiredConfiguration(this IConfiguration configuration)
        {
            var missing = new List<string>();

            if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
                missing.Add("ConnectionStrings__DefaultConnection");

            var jwtKey = configuration["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(jwtKey))
                missing.Add("Jwt__Key");
            else if (jwtKey.Length < MinimumJwtKeyLength)
                missing.Add($"Jwt__Key (must be at least {MinimumJwtKeyLength} characters, got {jwtKey.Length})");

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    "WitcherHub cannot start because required configuration is missing: " +
                    string.Join(", ", missing) +
                    ". Set these as environment variables (hosted) or via 'dotnet user-secrets' (local). " +
                    "See docs/CONFIGURATION.md.");
            }
        }

        /// <summary>
        /// Logs which optional integrations are disabled because they have no
        /// credentials. The values themselves are never logged.
        /// </summary>
        public static void LogConfigurationReport(this WebApplication app)
        {
            var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("WitcherHub.Startup");
            var configuration = app.Configuration;

            void Report(string feature, string variable, string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    logger.LogWarning("{Feature} is disabled: {Variable} is not configured.", feature, variable);
                else
                    logger.LogInformation("{Feature} is configured.", feature);
            }

            Report("Lexware integration", "Lexware__AccessToken", configuration["Lexware:AccessToken"]);
            Report("AI contract drafting", "OpenAI__ApiKey", configuration["OpenAI:ApiKey"]);
            Report("Outgoing email", "Smtp__Password", configuration["Smtp:Password"]);

            // Not a secret, and worth stating plainly: every link this environment
            // emails — password resets, quote and contract signing, invoices — is
            // built from this value. A dev environment carrying the production URL
            // sends users to production, which is easy to miss otherwise.
            var publicBaseUrl = PublicBaseUrl.Resolve(configuration);

            if (publicBaseUrl is null)
            {
                logger.LogWarning(
                    "{Variable} is not configured. Password reset will refuse to send, and links in " +
                    "other emails may be unusable.", PublicBaseUrl.ConfigurationKey);
            }
            else
            {
                logger.LogInformation(
                    "Links emailed by this environment will point at {PublicBaseUrl} ({Environment}).",
                    publicBaseUrl, app.Environment.EnvironmentName);
            }

            var verifyWebhooks = configuration.GetValue<bool>("LexwareWebhooks:VerifySignature");
            var webhookKey = configuration["LexwareWebhooks:PublicKeyPem"];
            if (verifyWebhooks && string.IsNullOrWhiteSpace(webhookKey))
            {
                logger.LogWarning(
                    "Lexware webhooks will all be rejected: signature verification is on but " +
                    "LexwareWebhooks__PublicKeyPem is not configured.");
            }
        }
    }
}
