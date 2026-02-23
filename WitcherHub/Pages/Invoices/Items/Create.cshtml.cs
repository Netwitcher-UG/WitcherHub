using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WitcherHub.Pages.Invoices.Items
{
    [Authorize]
    public class CreateModel : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public Guid InvoiceId { get; set; }

        public IActionResult OnGet()
        {
            TempData["Toast.Type"] = "info";
            TempData["Toast.Title"] = "Invoices";
            TempData["Toast.Message"] = "Manual invoice item editing is disabled. Invoices are managed in Lexware.";
            return RedirectToPage("/Invoices/Details", new { id = InvoiceId });
        }

        public IActionResult OnPost()
        {
            TempData["Toast.Type"] = "info";
            TempData["Toast.Title"] = "Invoices";
            TempData["Toast.Message"] = "Manual invoice item editing is disabled.";
            return RedirectToPage("/Invoices/Details", new { id = InvoiceId });
        }
    }
}
