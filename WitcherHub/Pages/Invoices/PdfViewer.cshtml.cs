using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Interfaces.ManageData;

namespace WitcherHub.Pages.Invoices
{
    public class PdfViewerModel : PageModel
    {
        private readonly IInvoice _invoices;
        private readonly IWebHostEnvironment _env;

        public PdfViewerModel(IInvoice invoices, IWebHostEnvironment env)
        {
            _invoices = invoices;
            _env = env;
        }

        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; }

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (Id == Guid.Empty)
                return Content("Invalid invoice id.", "text/plain");

            var inv = await _invoices.GetInvoiceAsync(Id, ct);
            if (inv is null)
                return Content("Invoice not found.", "text/plain");

            var storedPath = inv.LexwarePdfPath;
            if (string.IsNullOrWhiteSpace(storedPath))
                return Content("PDF not available yet.", "text/plain");

            var baseDir = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "App_Data", "LexwareInvoices"));

            var fileNameOnly = Path.GetFileName(storedPath);

            if (string.IsNullOrWhiteSpace(fileNameOnly))
                return Content("PDF file name is invalid.", "text/plain");

            var full = Path.GetFullPath(Path.Combine(baseDir, fileNameOnly));

            if (!full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                return Content("PDF path rejected by security check.", "text/plain");

            if (!System.IO.File.Exists(full))
                return Content($"PDF file not found: {fileNameOnly}", "text/plain");

            var result = PhysicalFile(full, "application/pdf");
            result.EnableRangeProcessing = true;
            return result;
        }
       
    }
}