using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WitcherHub.Pages.Invoices
{
    [Authorize]
    public class CreateModel : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public Guid ProjectId { get; set; }

        public IActionResult OnGet()
        {
            TempData["Toast.Type"] = "info";
            TempData["Toast.Title"] = "Invoices";
            TempData["Toast.Message"] = "Manual invoice creation is disabled. Invoices are created in Lexware.";
            return RedirectToPage("/Projects", new { openProjectId = ProjectId, openTab = "invoices" });
        }

        public IActionResult OnPost()
        {
            TempData["Toast.Type"] = "info";
            TempData["Toast.Title"] = "Invoices";
            TempData["Toast.Message"] = "Manual invoice creation is disabled. Invoices are created in Lexware.";
            return RedirectToPage("/Projects", new { openProjectId = ProjectId, openTab = "invoices" });
        }
    }
}
