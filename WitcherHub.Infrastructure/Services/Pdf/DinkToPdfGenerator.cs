using DinkToPdf;
using DinkToPdf.Contracts;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using WitcherHub.Application.Interfaces;
using Microsoft.AspNetCore.Hosting;

namespace WitcherHub.Infrastructure.Services.Pdf
{
    public sealed class DinkToPdfGenerator : IPdfGenerator
    {
        private readonly IConverter _converter;
        private readonly ILogger<DinkToPdfGenerator> _logger;
        private readonly IWebHostEnvironment _env;
        public DinkToPdfGenerator(IConverter converter, ILogger<DinkToPdfGenerator> logger
            , IWebHostEnvironment env)
        {
            _converter = converter;
            _logger = logger;
            _env = env;

            EnsureWkhtmltoxLoaded(); 
        }

        public byte[] FromHtml(string html, string documentTitle)
        {
            var headerPath = CreateHeaderTempFile(documentTitle);

            try
            {
                var doc = new HtmlToPdfDocument
                {
                    GlobalSettings = new GlobalSettings
                    {
                        ColorMode = ColorMode.Color,
                        Orientation = Orientation.Portrait,
                        PaperSize = PaperKind.A4,
                        DocumentTitle = documentTitle,
                        Margins = new MarginSettings
                        {
                            Top = 26,
                            Bottom = 14,
                            Left = 12,
                            Right = 12
                        }
                    }
                };

                doc.Objects.Add(new ObjectSettings
                {
                    PagesCount = true,
                    HtmlContent = html,
                    WebSettings = new WebSettings
                    {
                        DefaultEncoding = "utf-8",
                        LoadImages = true,
                        EnableIntelligentShrinking = true
                    },
                    HeaderSettings = new HeaderSettings
                    {
                        HtmUrl = ToFileUrl(headerPath),
                        Spacing = 2
                    },
                    FooterSettings = new FooterSettings
                    {
                        FontName = "Arial",
                        FontSize = 8,
                        Line = true,
                        Right = "Seite [page] von [toPage]"
                    }
                });

                return _converter.Convert(doc);
            }
            finally
            {
                TryDelete(headerPath);
            }
        }

        private void EnsureWkhtmltoxLoaded()
        {
            var baseDir = AppContext.BaseDirectory;

            string nativePath =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? Path.Combine(baseDir, "libs", "windows", "libwkhtmltox.dll")
                    : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                        ? Path.Combine(baseDir, "libs", "linux", "libwkhtmltox.so")
                        : throw new PlatformNotSupportedException("wkhtmltox is only supported on Windows/Linux.");

            if (!File.Exists(nativePath))
                throw new FileNotFoundException($"wkhtmltox native library not found: {nativePath}");

            try
            {
                NativeLibrary.Load(nativePath);
                _logger.LogInformation("wkhtmltox loaded: {Path}", nativePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load wkhtmltox: {Path}", nativePath);
                throw;
            }
        }

        private string CreateHeaderTempFile(string documentTitle)
        {
            var logoDataUri = ResolveLogoDataUri();
            var title = WebUtility.HtmlEncode(documentTitle ?? "");
            var printedAt = WebUtility.HtmlEncode(DateTime.Now.ToString("dd.MM.yyyy HH:mm"));

            var logoHtml = string.IsNullOrWhiteSpace(logoDataUri)
    ? ""
    : $@"<img src=""{logoDataUri}"" style=""height:40px;max-width:320px;object-fit:contain;display:block;"" />";

            var headerHtml = $@"
<!doctype html>
<html>
<head>
  <meta charset=""utf-8"" />
  <style>
    body {{ margin:0; padding:0; font-family: Arial, Helvetica, sans-serif; font-size:10px; color:#111; }}
    .wrap {{ width:100%; padding:0 10px; box-sizing:border-box; }}
    .row {{ width:100%; border-bottom:1px solid #ddd; padding:6px 0 6px 0; }}
    table {{ width:100%; border-collapse:collapse; }}
    td {{ vertical-align:middle; }}
    .meta {{ color:#666; font-size:9.5px; text-align:right; white-space:nowrap; }}
    .meta .title {{ font-weight:700; font-size:10.5px; color:#111; }}
  </style>
</head>
<body>
  <div class=""wrap"">
    <div class=""row"">
      <table>
        <tr>
          <td style=""width:1%;white-space:nowrap;"">{logoHtml}</td>

          <td></td>

          <td class=""meta"">
            <div class=""title"">{title}</div>
            <div>{printedAt}</div>
          </td>
        </tr>
      </table>
    </div>
  </div>
</body>
</html>";

            var path = Path.Combine(Path.GetTempPath(), $"wh-pdf-header-{Guid.NewGuid():N}.html");
            File.WriteAllText(path, headerHtml, Encoding.UTF8);
            return path;
        }

        private static string ToFileUrl(string path)
        {
            var full = Path.GetFullPath(path).Replace("\\", "/");
            return full.StartsWith("/") ? $"file://{full}" : $"file:///{full}";
        }

        private string ResolveLogoDataUri()
        {
            var p = Path.Combine(_env.WebRootPath, "theme", "assets", "images", "netwitcher-logo.png");

            if (!File.Exists(p))
            {
                _logger.LogWarning("PDF header image not found at: {Path}", p);
                return "";
            }

            var bytes = File.ReadAllBytes(p);
            return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }
        
    }
}
