using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WitcherHub.Pages.Invoices
{
    [Authorize]
    public class EditModel : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; }

        public IActionResult OnGet()
        {
            if (Id == Guid.Empty) return RedirectToPage("/Projects");

            TempData["Toast.Type"] = "info";
            TempData["Toast.Title"] = "Invoices";
            TempData["Toast.Message"] = "Manual invoice editing is disabled. Open the invoice details instead.";
            return RedirectToPage("./Details", new { id = Id });
        }

        public IActionResult OnPost()
        {
            if (Id == Guid.Empty) return RedirectToPage("/Projects");

            TempData["Toast.Type"] = "info";
            TempData["Toast.Title"] = "Invoices";
            TempData["Toast.Message"] = "Manual invoice editing is disabled.";
            return RedirectToPage("./Details", new { id = Id });
        }
    }
}
