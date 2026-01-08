using Microsoft.Extensions.Options;
using System.Net;
using System.Text.RegularExpressions;
using WitcherHub.Application.Interfaces.Email;
using WitcherHub.Infrastructure.Services.Email_Sender.Options;

namespace WitcherHub.Infrastructure.Services.Email_Sender.EmailTemplates
{
    public sealed class FileEmailTemplateRenderer : IEmailTemplateRenderer
    {
        private static readonly Regex RawToken = new(@"\{\{\{\s*(?<key>[\w\.-]+)\s*\}\}\}", RegexOptions.Compiled);
        private static readonly Regex EncToken = new(@"\{\{\s*(?<key>[\w\.-]+)\s*\}\}", RegexOptions.Compiled);

        private readonly EmailTemplateOptions _opt;

        public FileEmailTemplateRenderer(IOptions<EmailTemplateOptions> opt)
        {
            _opt = opt.Value;
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

            // raw first
            combined = RawToken.Replace(combined, m =>
            {
                var key = m.Groups["key"].Value;
                return tokens.TryGetValue(key, out var val) ? val ?? "" : m.Value;
            });

            // encoded
            combined = EncToken.Replace(combined, m =>
            {
                var key = m.Groups["key"].Value;
                if (!tokens.TryGetValue(key, out var val)) return m.Value;
                return WebUtility.HtmlEncode(val ?? "");
            });

            return combined;
        }

        private Dictionary<string, string?> BuildTokens(object model)
        {
            var dict = ToDictionary(model);

            dict.TryAdd("AppName", _opt.AppName);
            dict.TryAdd("BrandLine", _opt.BrandLine);
            dict.TryAdd("FooterText", _opt.FooterText);

            var year = DateTime.UtcNow.Year.ToString();
            dict.TryAdd("Year", year);

            // جهّز LegalLine بعد إدخال السنة (بدون nested tokens)
            if (!dict.ContainsKey("LegalLine"))
            {
                var legal = _opt.LegalLine ?? "";
                legal = legal.Replace("{{Year}}", year, StringComparison.OrdinalIgnoreCase);
                dict["LegalLine"] = legal;
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
