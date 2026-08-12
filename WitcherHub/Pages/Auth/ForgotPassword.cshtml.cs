using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using WitcherHub.Application.Interfaces;

namespace WitcherHub.Pages.Auth
{
    [AllowAnonymous]
    public class ForgotPasswordModel : PageModel
    {
        private readonly IAuthService _auth;
        private readonly ILogger<ForgotPasswordModel> _logger;

        public ForgotPasswordModel(IAuthService auth, ILogger<ForgotPasswordModel> logger)
        {
            _auth = auth;
            _logger = logger;
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
    }
}
