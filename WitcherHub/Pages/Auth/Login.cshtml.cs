using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Cryptography;
using System.Text;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces;

namespace WitcherHub.Pages.Auth
{
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        private readonly IAuthService _auth;
        private readonly ISignInDiagnostics _diagnostics;
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(IAuthService auth, ISignInDiagnostics diagnostics, ILogger<LoginModel> logger)
        {
            _auth = auth;
            _diagnostics = diagnostics;
            _logger = logger;
        }

        [BindProperty] public string Email { get; set; } = "";
        [BindProperty] public string Password { get; set; } = "";

        public string? ErrorMessage { get; private set; }

        /// <summary>
        /// True when sign-in failed for a reason other than the credentials, so the
        /// page can say "look at the server" instead of "check your password".
        /// </summary>
        public bool IsSystemError { get; private set; }

        /// <summary>
        /// Short stable code for what went wrong (AUTH-02, AUTH-03, …). Always
        /// rendered, because it is the one thing a person can read off the screen
        /// and quote without knowing anything about the system, and it gives away
        /// nothing to a stranger.
        /// </summary>
        public string? FailureCode { get; private set; }

        /// <summary>
        /// Random per-attempt identifier, printed on the page and written to the
        /// log, so a screenshot can be matched to the exact log entry.
        /// </summary>
        public string? Reference { get; private set; }

        public DateTime? FailedAtUtc { get; private set; }

        /// <summary>
        /// The administrator-facing explanation and the environment facts behind
        /// it. Populated only when sign-in diagnostics are switched on.
        /// </summary>
        public string? DiagnosticExplanation { get; private set; }
        public IReadOnlyList<SignInDiagnosticFact> DiagnosticFacts { get; private set; } = [];

        /// <summary>
        /// True when diagnostics are switched off, so the page can say how to turn
        /// them on rather than leaving the reader with a bare code.
        /// </summary>
        public bool DiagnosticsAvailable => _diagnostics.IsEnabled;

        /// <summary>Everything above as one block, ready for the copy button.</summary>
        public string CopyableReport { get; private set; } = "";

        /// <summary>
        /// Set when the visitor was bounced here by an antiforgery failure — a page
        /// left open across a deploy, or a form restored by the browser.
        /// </summary>
        [BindProperty(SupportsGet = true, Name = "expired")]
        public bool SessionExpired { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }

        public void OnGet()
        {
            if (SessionExpired)
            {
                IsSystemError = true;
                ErrorMessage = "That page had been open for a while and the form expired. " +
                               "Please enter your details again.";
            }
        }

        public async Task<IActionResult> OnPostAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Enter both an email address and a password.";
                return Page();
            }

            var reference = NewReference();

            try
            {
                var result = await _auth.LoginAsync(new LoginRequest(Email, Password), ct);

                Response.Cookies.Append("access_token", result.AccessToken, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = Request.IsHttps,
                    SameSite = SameSiteMode.Strict,
                    Expires = result.ExpiresAtUtc
                });

                _logger.LogInformation("Sign-in succeeded for {Email}.", Email.Trim());

                var target = string.IsNullOrWhiteSpace(ReturnUrl) ? "/Index" : ReturnUrl;
                if (!Url.IsLocalUrl(target)) target = "/Index";
                return LocalRedirect(target);
            }
            catch (AuthenticationFailedAppException ex)
            {
                // A credential check failed. Which one is on the exception, so the
                // page can print a code; the prose stays identical for everybody,
                // so this page cannot be used to discover which addresses exist.
                _logger.LogWarning(
                    "Sign-in failed for {Email}. Code {Code}, reference {Reference}.",
                    Email.Trim(), ex.Reason.ToCode(), reference);

                ErrorMessage = "Login failed. Check email/password.";

                await DescribeFailureAsync(
                    reference,
                    ex.Reason.ToCode(),
                    ex.Reason.ToAdministratorExplanation(),
                    ct);

                return Page();
            }
            catch (Exception ex)
            {
                // Anything else — database unreachable, JWT key rejected, a role
                // lookup failing — used to be swallowed by a bare catch and reported
                // as bad credentials, which sent people to retype a password that was
                // never the problem. Say so plainly and record the cause.
                _logger.LogError(
                    ex,
                    "Sign-in could not be completed for {Email} due to a system error. Reference {Reference}.",
                    Email.Trim(), reference);

                IsSystemError = true;
                ErrorMessage = "Sign-in is currently unavailable. This is a server problem, not your password.";

                await DescribeFailureAsync(
                    reference,
                    "AUTH-500",
                    $"{ex.GetType().Name}: {ex.GetBaseException().Message}",
                    ct);

                return Page();
            }
        }

        /// <summary>
        /// Fills in everything the page renders below the error message, and builds
        /// the copyable block in the same shape whether diagnostics are on or off.
        /// </summary>
        private async Task DescribeFailureAsync(
            string reference,
            string code,
            string explanation,
            CancellationToken ct)
        {
            FailureCode = code;
            Reference = reference;
            FailedAtUtc = DateTime.UtcNow;

            var report = new StringBuilder();
            report.AppendLine("WitcherHub sign-in failure");
            report.AppendLine($"Code: {code}");
            report.AppendLine($"Reference: {reference}");
            report.AppendLine($"Time (UTC): {FailedAtUtc:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine($"Email: {Email.Trim()}");

            if (_diagnostics.IsEnabled)
            {
                DiagnosticExplanation = explanation;
                report.AppendLine($"Explanation: {explanation}");

                var facts = await _diagnostics.DescribeAsync(Email, ct);
                DiagnosticFacts = facts.Facts;

                if (facts.Facts.Count > 0)
                {
                    report.AppendLine();
                    report.AppendLine(facts.ToPlainText());
                }
            }

            CopyableReport = report.ToString();
        }

        /// <summary>
        /// Short, unambiguous and case-insensitive to read aloud. Not a secret —
        /// it only has to be unique enough to find one line in a day of logs.
        /// </summary>
        private static string NewReference() =>
            Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
    }
}
