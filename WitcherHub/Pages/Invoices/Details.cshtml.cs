using System;
using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Interfaces.ManageData;
using WitcherHub.Application.Models.View.Invoices;

namespace WitcherHub.Pages.Invoices
{
    [Authorize]
    public class DetailsModel : PageModel
    {
        private readonly IInvoice _invoices;
        private readonly IWebHostEnvironment _env;

        public DetailsModel(IInvoice invoices, IWebHostEnvironment env)
        {
            _invoices = invoices;
            _env = env;
        }

        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; }

        public InvoiceViews.InvoiceDetailsView? Invoice { get; private set; }

        // ✅ إذا في PDF: حوّل مباشرة للـ Pdf handler (بدون Page/Layout)
        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (Id == Guid.Empty) return NotFound();

            Invoice = await _invoices.GetInvoiceAsync(Id, ct);
            if (Invoice is null) return NotFound();

            if (!string.IsNullOrWhiteSpace(Invoice.LexwarePdfPath))
                return RedirectToPage("./Details", "Pdf", new { id = Id });

            return Page(); // fallback إذا ما في PDF
        }

        // /Invoices/Details?id=...&handler=Pdf
        public async Task<IActionResult> OnGetPdfAsync(bool download = false, CancellationToken ct = default)
        {
            if (Id == Guid.Empty) return NotFound();

            var inv = await _invoices.GetInvoiceAsync(Id, ct);
            if (inv is null) return NotFound();

            var path = inv.LexwarePdfPath;
            if (string.IsNullOrWhiteSpace(path))
                return Content("PDF not available yet.", "text/plain");

            // ✅ Security: allow only serving from App_Data/LexwareInvoices
            var baseDir = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "App_Data", "LexwareInvoices"));
            var full = Path.GetFullPath(path);

            if (!full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                return Forbid();

            if (!System.IO.File.Exists(full))
                return NotFound();

            var fileName = $"Invoice-{(string.IsNullOrWhiteSpace(inv.InvoiceNo) ? inv.Id.ToString() : inv.InvoiceNo)}.pdf";

            var result = PhysicalFile(full, "application/pdf", download ? fileName : null);
            result.EnableRangeProcessing = true;
            return result;
        }
    }
}
