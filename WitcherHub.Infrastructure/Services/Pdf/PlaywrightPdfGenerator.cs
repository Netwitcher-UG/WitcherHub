using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using WitcherHub.Application.Interfaces;

namespace WitcherHub.Infrastructure.Services.Pdf
{
    /// <summary>
    /// Renders HTML to PDF with headless Chromium.
    ///
    /// The browser is launched once and reused. Previously every invoice, quote
    /// and contract download started a Playwright driver and a fresh Chromium
    /// process, which cost seconds per request and left the container's memory
    /// use tracking the number of concurrent downloads. Each render still gets
    /// its own browser context, so documents stay isolated from one another.
    /// </summary>
    public sealed class PlaywrightPdfGenerator : IPdfGenerator, IAsyncDisposable
    {
        private readonly PlaywrightBrowserInstaller _browserInstaller;
        private readonly ILogger<PlaywrightPdfGenerator> _logger;
        private readonly IWebHostEnvironment _env;
        private readonly BrandingOptions _branding;

        private readonly SemaphoreSlim _launchGate = new(1, 1);
        private IPlaywright? _playwright;
        private IBrowser? _browser;
        private string? _cachedLogoDataUri;

        public PlaywrightPdfGenerator(
            PlaywrightBrowserInstaller browserInstaller,
            ILogger<PlaywrightPdfGenerator> logger,
            IWebHostEnvironment env,
            Microsoft.Extensions.Options.IOptions<BrandingOptions> branding)
        {
            _browserInstaller = browserInstaller;
            _logger = logger;
            _env = env;
            _branding = branding.Value;
        }

        /// <summary>
        /// Synchronous entry point kept for existing callers. Prefer
        /// <see cref="FromHtmlAsync"/>, which does not block a request thread.
        /// </summary>
        public byte[] FromHtml(string html, string? documentTitle = null)
        {
            return Task.Run(() => FromHtmlAsync(html, documentTitle)).GetAwaiter().GetResult();
        }

        public async Task<byte[]> FromHtmlAsync(
            string html,
            string? documentTitle = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(html))
                throw new ArgumentException("HTML content is required.", nameof(html));

            _logger.LogInformation("Generating PDF with Playwright. Title={Title}", documentTitle);

            var browser = await GetBrowserAsync(ct);

            html = html.Replace("__NETWITCHER_LOGO__", await GetLogoDataUriAsync(ct), StringComparison.Ordinal);

            await using var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();

            await page.SetContentAsync(html, new PageSetContentOptions
            {
                WaitUntil = WaitUntilState.Load
            });

            return await page.PdfAsync(new PagePdfOptions
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
        }

        private async Task<IBrowser> GetBrowserAsync(CancellationToken ct)
        {
            var browser = _browser;
            if (browser is { IsConnected: true })
                return browser;

            await _launchGate.WaitAsync(ct);
            try
            {
                // Another caller may have relaunched while we waited.
                if (_browser is { IsConnected: true })
                    return _browser;

                if (_browser is not null)
                {
                    _logger.LogWarning("Chromium disconnected; relaunching for PDF generation.");
                    await SafeDisposeBrowserAsync();
                }

                await _browserInstaller.EnsureInstalledAsync(ct);

                _playwright ??= await Playwright.CreateAsync();

                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true
                });

                return _browser;
            }
            finally
            {
                _launchGate.Release();
            }
        }

        private async Task<string> GetLogoDataUriAsync(CancellationToken ct)
        {
            if (_cachedLogoDataUri is not null)
                return _cachedLogoDataUri;

            // From configuration, and combined segment by segment so the setting
            // can be written with forward slashes on any platform.
            var relative = (_branding.LogoPath ?? "").Replace('\\', '/').Trim('/');

            var logoPath = Path.Combine(
                new[] { _env.WebRootPath }.Concat(relative.Split('/')).ToArray());

            if (!File.Exists(logoPath))
            {
                // Worth a warning rather than a shrug: the substitute is an empty
                // src, which renders as nothing, so an unbranded contract went out
                // looking like a deliberate design choice.
                _logger.LogWarning(
                    "No logo at {Path}; contracts and quotes will be generated without one. " +
                    "Set {Setting} to a file under wwwroot.",
                    logoPath, BrandingOptions.LogoPathSettingName);

                return _cachedLogoDataUri = "";
            }

            var logoBytes = await File.ReadAllBytesAsync(logoPath, ct);
            var mediaType = Path.GetExtension(logoPath).ToLowerInvariant() switch
            {
                ".svg" => "image/svg+xml",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                _ => "image/png"
            };

            return _cachedLogoDataUri = $"data:{mediaType};base64,{Convert.ToBase64String(logoBytes)}";
        }

        private async Task SafeDisposeBrowserAsync()
        {
            try
            {
                if (_browser is not null)
                    await _browser.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispose the Chromium instance.");
            }
            finally
            {
                _browser = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await SafeDisposeBrowserAsync();
            _playwright?.Dispose();
            _playwright = null;
            _launchGate.Dispose();
        }
    }
}
