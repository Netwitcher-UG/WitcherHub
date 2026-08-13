using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces;

namespace WitcherHub.Pages.Auth
{
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        private readonly IAuthService _auth;
        private readonly ILogger<LoginModel> _logger;
        private readonly IWebHostEnvironment _env;

        public LoginModel(IAuthService auth, ILogger<LoginModel> logger, IWebHostEnvironment env)
        {
            _auth = auth;
            _logger = logger;
            _env = env;
        }

        [BindProperty] public string Email { get; set; } = "";
        [BindProperty] public string Password { get; set; } = "";
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// True when sign-in failed for a reason other than the credentials, so the
        /// page can say "look at the server" instead of "check your password".
        /// </summary>
        public bool IsSystemError { get; private set; }

        /// <summary>
        /// Exception detail, populated in Development only.
        /// </summary>
        public string? Diagnostic { get; private set; }

        /// <summary>
        /// Set when the visitor was bounced here by an antiforgery failure — a page
        /// left open across a deploy, or a form restored by the browser.
        /// </summary>
        [BindProperty(SupportsGet = true, Name = "expired")]
        public bool SessionExpired { get; set; }

        public void OnGet()
        {
            if (SessionExpired)
            {
                IsSystemError = true;
                ErrorMessage = "That page had been open for a while and the form expired. " +
                               "Please enter your details again.";
            }
        }

        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }

        public async Task<IActionResult> OnPostAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Enter both an email address and a password.";
                return Page();
            }

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
            catch (AuthenticationFailedAppException)
            {
                // A genuine credential mismatch. The reason (unknown account versus
                // wrong password) is in the log, not here, so this page cannot be
                // used to discover which addresses have accounts.
                ErrorMessage = "Login failed. Check email/password.";
                return Page();
            }
            catch (Exception ex)
            {
                // Anything else — database unreachable, JWT key rejected, a role
                // lookup failing — used to be swallowed by a bare catch and reported
                // as bad credentials, which sent people to retype a password that was
                // never the problem. Say so plainly and record the cause.
                _logger.LogError(ex, "Sign-in could not be completed for {Email} due to a system error.", Email.Trim());

                IsSystemError = true;
                ErrorMessage = "Sign-in is currently unavailable. This is a server problem, not your password. " +
                               "The details are in the application log.";

                if (_env.IsDevelopment())
                    Diagnostic = $"{ex.GetType().Name}: {ex.GetBaseException().Message}";

                return Page();
            }
        }
    }
}
