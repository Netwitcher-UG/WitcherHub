using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.RegularExpressions;
using WitcherHub.Application.Interfaces.Email;
using WitcherHub.Infrastructure.Services.Email_Sender.Options;

namespace WitcherHub.Infrastructure.Services.Email_Sender.EmailTemplates
{
    public sealed class FileEmailTemplateRenderer : IEmailTemplateRenderer
    {
        private static readonly Regex ConditionalBlock = new(
            @"\{\{#\s*(?<key>[\w\.-]+)\s*\}\}(?<content>.*?)\{\{/\s*\k<key>\s*\}\}",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex RawToken = new(
            @"\{\{\{\s*(?<key>[\w\.-]+)\s*\}\}\}",
            RegexOptions.Compiled);

        private static readonly Regex EncToken = new(
            @"\{\{\s*(?<key>[\w\.-]+)\s*\}\}",
            RegexOptions.Compiled);

        private readonly EmailTemplateOptions _opt;
        private readonly IConfiguration _cfg;

        public FileEmailTemplateRenderer(IOptions<EmailTemplateOptions> opt, IConfiguration cfg)
        {
            _opt = opt.Value;
            _cfg = cfg;
        }

        public async Task<string> RenderAsync(string templateName, object model, CancellationToken ct = default)
        {
            var root = Path.Combine(AppContext.BaseDirectory, _opt.TemplatesFolder);

            var layoutPath = Path.Combine(root, _opt.LayoutFileName);
            var bodyPath = Path.Combine(root, _opt.MessagesFolder, $"{templateName}.html");

            if (!File.Exists(layoutPath))
                throw new FileNotFoundException($"Email layout not found: {layoutPath}");

            if (!File.Exists(bodyPath))
                throw new FileNotFoundException($"Email template not found: {bodyPath}");

            var layout = await File.ReadAllTextAsync(layoutPath, ct);
            var body = await File.ReadAllTextAsync(bodyPath, ct);

            var combined = layout.Replace("{{{Body}}}", body);

            var tokens = BuildTokens(model);

            // مهم جداً: لازم الشرط يشتغل قبل RawToken و EncToken
            if (IsQuoteSignatureTemplate(templateName))
            {
                combined = RenderConditionalBlocks(combined, tokens);
            }

            combined = RawToken.Replace(combined, m =>
            {
                var key = m.Groups["key"].Value;
                return tokens.TryGetValue(key, out var val) ? val ?? "" : m.Value;
            });

            combined = EncToken.Replace(combined, m =>
            {
                var key = m.Groups["key"].Value;
                if (!tokens.TryGetValue(key, out var val)) return m.Value;
                return WebUtility.HtmlEncode(val ?? "");
            });

            return combined;
        }

        private static bool IsQuoteSignatureTemplate(string templateName)
        {
            return string.Equals(templateName, "QuoteSignatureRequest", StringComparison.OrdinalIgnoreCase)
                || string.Equals(templateName, "QuoteSignatureRequest.html", StringComparison.OrdinalIgnoreCase);
        }

        private static string RenderConditionalBlocks(string template, Dictionary<string, string?> tokens)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return template;
            }

            return ConditionalBlock.Replace(template, match =>
            {
                var key = match.Groups["key"].Value;
                var content = match.Groups["content"].Value;

                if (!tokens.TryGetValue(key, out var value))
                {
                    return string.Empty;
                }

                return string.IsNullOrWhiteSpace(value)
                    ? string.Empty
                    : content;
            });
        }

        private Dictionary<string, string?> BuildTokens(object model)
        {
            var dict = ToDictionary(model);

            dict.TryAdd("AppName", _opt.AppName);
            dict.TryAdd("BrandLine", _opt.BrandLine);
            dict.TryAdd("FooterText", _opt.FooterText);

            var year = DateTime.UtcNow.Year.ToString();
            dict.TryAdd("Year", year);

            if (!dict.ContainsKey("LegalLine"))
            {
                var legal = _opt.LegalLine ?? "";
                legal = legal.Replace("{{Year}}", year, StringComparison.OrdinalIgnoreCase);
                dict["LegalLine"] = legal;
            }

            var publicBaseUrl =
                _cfg["WITCHERHUB_PUBLIC_BASE_URL"]
                ?? Environment.GetEnvironmentVariable("WITCHERHUB_PUBLIC_BASE_URL")
                ?? "";

            publicBaseUrl = publicBaseUrl.Trim().TrimEnd('/');

            if (!string.IsNullOrWhiteSpace(publicBaseUrl) &&
                !publicBaseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !publicBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                publicBaseUrl = "https://" + publicBaseUrl;
            }

            var v = _cfg["WITCHERHUB_ASSETS_VERSION"]
                    ?? Environment.GetEnvironmentVariable("WITCHERHUB_ASSETS_VERSION")
                    ?? DateTime.UtcNow.ToString("yyyyMMddHHmmss");

            if (!string.IsNullOrWhiteSpace(publicBaseUrl))
            {
                dict.TryAdd("FooterSignatureUrl", $"{publicBaseUrl}/api/email-assets/footer-signature?v={v}");
                dict.TryAdd("HeaderImageUrl", $"{publicBaseUrl}/api/email-assets/header?v={v}");
            }

            return dict;
        }

        private static Dictionary<string, string?> ToDictionary(object model)
        {
            if (model is IReadOnlyDictionary<string, string?> dict)
                return new Dictionary<string, string?>(dict, StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var props = model.GetType().GetProperties();

            foreach (var p in props)
                result[p.Name] = p.GetValue(model)?.ToString();

            return result;
        }
    }
}
