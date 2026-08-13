using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using WitcherHub.Application.Interfaces;
using WitcherHub.Infrastructure.Authentication;

namespace WitcherHub.Pages.Auth
{
    [AllowAnonymous]
    public class ForgotPasswordModel : PageModel
    {
        private readonly IAuthService _auth;
        private readonly ILogger<ForgotPasswordModel> _logger;
        private readonly IConfiguration _configuration;

        public ForgotPasswordModel(
            IAuthService auth,
            ILogger<ForgotPasswordModel> logger,
            IConfiguration configuration)
        {
            _auth = auth;
            _logger = logger;
            _configuration = configuration;
        }

        [BindProperty]
        [Required(ErrorMessage = "Please enter your email address.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        public string Email { get; set; } = "";

        /// <summary>
        /// Set once the request has been accepted. The same confirmation is shown
        /// whether or not the address has an account.
        /// </summary>
        public bool Submitted { get; private set; }

        public string? ErrorMessage { get; private set; }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync(CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return Page();

            WarnIfLinkWillLeaveThisEnvironment();

            try
            {
                await _auth.RequestPasswordResetAsync(Email, ct);
            }
            catch (InvalidOperationException ex)
            {
                // Raised when the public base URL is not configured, which would
                // otherwise produce an email containing an unusable link.
                _logger.LogError(ex, "Could not build a password reset link.");
                ErrorMessage = "Password reset is not configured on this environment. Please contact an administrator.";
                return Page();
            }

            Submitted = true;
            return Page();
        }

        /// <summary>
        /// Records when the reset link will point at a different host than the one
        /// the request arrived on — a dev environment still carrying the production
        /// base URL will email production links, which otherwise only shows up when
        /// somebody clicks one and lands on the wrong site.
        ///
        /// The configured value still wins: deriving the host from the request would
        /// let a forged Host header capture the reset token.
        /// </summary>
        private void WarnIfLinkWillLeaveThisEnvironment()
        {
            var configuredHost = PublicBaseUrl.HostOf(_configuration);

            if (configuredHost is null)
                return;

            var requestHost = Request.Host.Host;

            if (string.Equals(configuredHost, requestHost, StringComparison.OrdinalIgnoreCase))
                return;

            _logger.LogWarning(
                "Password reset requested on {RequestHost} but the emailed link points at {ConfiguredHost}, " +
                "because {Variable} is set to that host. Update it for this environment if unintended.",
                requestHost, configuredHost, PublicBaseUrl.ConfigurationKey);
        }
    }
}
