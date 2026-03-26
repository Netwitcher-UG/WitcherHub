
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using WitcherHub.Application.Interfaces;

namespace WitcherHub.Infrastructure.Services.Pdf
{
    public sealed class PlaywrightPdfGenerator : IPdfGenerator
    {
        private readonly PlaywrightBrowserInstaller _browserInstaller;
        private readonly ILogger<PlaywrightPdfGenerator> _logger;
        private readonly IWebHostEnvironment _env;
        public PlaywrightPdfGenerator(
    PlaywrightBrowserInstaller browserInstaller,
    ILogger<PlaywrightPdfGenerator> logger,
    IWebHostEnvironment env)
        {
            _browserInstaller = browserInstaller;
            _logger = logger;
            _env = env;
        }

        public byte[] FromHtml(string html, string? documentTitle = null)
        {
            return FromHtmlInternalAsync(html, documentTitle).GetAwaiter().GetResult();
        }

        private async Task<byte[]> FromHtmlInternalAsync(string html, string? documentTitle)
        {
            if (string.IsNullOrWhiteSpace(html))
                throw new ArgumentException("HTML content is required.", nameof(html));

            await _browserInstaller.EnsureInstalledAsync();

            _logger.LogInformation("Generating PDF with Playwright. Title={Title}", documentTitle);

            using var playwright = await Playwright.CreateAsync();

            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });

            var page = await browser.NewPageAsync();
            var logoPath = Path.Combine(_env.WebRootPath, "theme", "assets", "images", "netwitcher-logo.png");

            if (File.Exists(logoPath))
            {
                var logoBytes = await File.ReadAllBytesAsync(logoPath);
                var logoDataUri = $"data:image/png;base64,{Convert.ToBase64String(logoBytes)}";
                html = html.Replace("__NETWITCHER_LOGO__", logoDataUri, StringComparison.Ordinal);
            }
            else
            {
                _logger.LogWarning("Logo file not found: {Path}", logoPath);
                html = html.Replace("__NETWITCHER_LOGO__", "", StringComparison.Ordinal);
            }
            await page.SetContentAsync(html, new PageSetContentOptions
            {
                WaitUntil = WaitUntilState.Load
            });

            var pdfBytes = await page.PdfAsync(new PagePdfOptions
            {
                PrintBackground = true,
                PreferCSSPageSize = true,
                DisplayHeaderFooter = true,
                HeaderTemplate = "<div></div>",
                FooterTemplate = """
        <div style="
            width:100%;
            font-size:10px;
            color:#6b7280;
            padding:0 12mm;
            box-sizing:border-box;
            font-family:Arial, Helvetica, sans-serif;
            text-align:center;">
            Seite <span class="pageNumber"></span> von <span class="totalPages"></span>
        </div>
        """,
                Margin = new Margin
                {
                    Top = "0",
                    Right = "0",
                    Bottom = "14mm",
                    Left = "0"
                }
            });

            return pdfBytes;
        }
    }
}
