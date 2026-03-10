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
    [AllowAnonymous]
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

            var storedPath = inv.LexwarePdfPath;
            if (string.IsNullOrWhiteSpace(storedPath))
                return Content("PDF not available yet.", "text/plain");

            var baseDir = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "App_Data", "LexwareInvoices"));
            var full = ResolvePdfFullPath(storedPath, baseDir);

            if (string.IsNullOrWhiteSpace(full))
                return Content("PDF path is invalid.", "text/plain");

            if (!full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                return Forbid();

            if (!System.IO.File.Exists(full))
                return NotFound($"PDF file not found: {Path.GetFileName(full)}");

            var downloadName = $"Invoice-{(string.IsNullOrWhiteSpace(inv.InvoiceNo) ? inv.Id.ToString() : inv.InvoiceNo)}.pdf";

            var result = PhysicalFile(full, "application/pdf", download ? downloadName : null);
            result.EnableRangeProcessing = true;
            return result;
        }

        private static string? ResolvePdfFullPath(string storedPath, string baseDir)
        {
            if (string.IsNullOrWhiteSpace(storedPath))
                return null;

            string fullPath;

            if (Path.IsPathRooted(storedPath))
            {
                // دعم السجلات القديمة التي خزنت path كامل
                fullPath = Path.GetFullPath(storedPath);
            }
            else
            {
                // دعم التخزين الجديد: file name فقط
                fullPath = Path.GetFullPath(Path.Combine(baseDir, storedPath));
            }

            return fullPath;
        }

    }
}
