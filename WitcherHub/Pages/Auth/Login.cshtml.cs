using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Interfaces;

namespace WitcherHub.Pages.Auth
{
    public class LoginModel : PageModel
    {
        private readonly IAuthService _auth;

        public LoginModel(IAuthService auth)
        {
            _auth = auth;
        }

        [BindProperty] public string Email { get; set; } = "";
        [BindProperty] public string Password { get; set; } = "";
        public string? ErrorMessage { get; set; }

        public void OnGet() { }
        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }
        public async Task<IActionResult> OnPostAsync(CancellationToken ct)
        {
            try
            {
                var result = await _auth.LoginAsync(new LoginRequest(Email, Password), ct);

                Response.Cookies.Append("access_token", result.AccessToken, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    Expires = result.ExpiresAtUtc,
                    Path = "/"
                });

                var target = string.IsNullOrWhiteSpace(ReturnUrl) ? "/Index" : ReturnUrl;
                if (!Url.IsLocalUrl(target)) target = "/Index";
                return LocalRedirect(target);

            }
            catch
            {
                ErrorMessage = "Login failed. Check email/password.";
                return Page();
            }
        }
    }
}
