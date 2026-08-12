using Microsoft.AspNetCore.WebUtilities;
using System.Text;

namespace WitcherHub.Infrastructure.Authentication
{
    /// <summary>
    /// Identity's password reset tokens are opaque strings containing characters
    /// that do not survive a round trip through a query string intact. Base64Url
    /// encoding them keeps the link safe to copy, paste and click from an email
    /// client that may re-encode the URL.
    /// </summary>
    public static class PasswordResetTokenEncoder
    {
        public static string Encode(string token) =>
            WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        /// <summary>
        /// Returns false for anything that is not a token this class produced,
        /// so a mangled or hand-edited link fails as an invalid token rather than
        /// throwing out of the page handler.
        /// </summary>
        public static bool TryDecode(string? encoded, out string token)
        {
            token = "";

            if (string.IsNullOrWhiteSpace(encoded))
                return false;

            try
            {
                token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
                return token.Length > 0;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
