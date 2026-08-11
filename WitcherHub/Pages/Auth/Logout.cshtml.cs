using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WitcherHub.Pages.Auth
{
    // Anonymous so an expired session can still clear its cookie.
    [AllowAnonymous]
    public class LogoutModel : PageModel
    {
        public IActionResult OnGet()
        {
            return RedirectToPage("/Auth/Login");
        }
        public IActionResult OnPost()
        {
            Response.Cookies.Delete("access_token");
            return RedirectToPage("/Auth/Login");
        }
    }
}
