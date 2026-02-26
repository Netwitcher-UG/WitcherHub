using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WitcherHub.Pages.Auth
{
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
