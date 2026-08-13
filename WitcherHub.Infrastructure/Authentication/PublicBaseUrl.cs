using Microsoft.Extensions.Configuration;

namespace WitcherHub.Infrastructure.Authentication
{
    /// <summary>
    /// Resolves the absolute base URL used to build links that leave the
    /// application — password resets, quote and invoice links.
    ///
    /// The value is deliberately taken from configuration rather than from the
    /// incoming request: building it from the Host header would let a forged
    /// header send a password reset link, token and all, to an attacker's site.
    /// The trade-off is that a wrong value points users at the wrong environment
    /// with no visible symptom, so callers log the resolved value.
    /// </summary>
    public static class PublicBaseUrl
    {
        public const string ConfigurationKey = "WITCHERHUB_PUBLIC_BASE_URL";

        /// <summary>
        /// Returns the normalised base URL, or null when it is not configured.
        /// </summary>
        public static string? Resolve(IConfiguration configuration)
        {
            var raw = configuration[ConfigurationKey]
                      ?? Environment.GetEnvironmentVariable(ConfigurationKey);

            return Normalise(raw);
        }

        /// <summary>
        /// Trims trailing slashes and supplies a scheme when one is missing.
        /// Railway shows hostnames without a scheme ("myapp.up.railway.app"), and
        /// pasting that form in produces a link no mail client can follow, so it
        /// is promoted to https rather than silently accepted.
        /// </summary>
        internal static string? Normalise(string? raw)
        {
            var value = (raw ?? "").Trim().TrimEnd('/');

            if (value.Length == 0)
                return null;

            if (!value.Contains("://", StringComparison.Ordinal))
                value = "https://" + value;

            return Uri.TryCreate(value, UriKind.Absolute, out var uri)
                   && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? value
                : null;
        }

        /// <summary>
        /// The host of the configured base URL, for comparison against the host a
        /// request actually arrived on. Null when unset or unparseable.
        /// </summary>
        public static string? HostOf(IConfiguration configuration)
        {
            var resolved = Resolve(configuration);

            return resolved is not null && Uri.TryCreate(resolved, UriKind.Absolute, out var uri)
                ? uri.Host
                : null;
        }
    }
}
