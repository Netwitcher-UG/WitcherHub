using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Contracts;

namespace WitcherHub.Infrastructure.Services.Contracts
{
    public class ContractDocumentGenerator : IContractDocumentGenerator
    {
        private readonly IAiTextGenerator _ai;
        private readonly ContractTemplateOptions _opt;

        public ContractDocumentGenerator(IAiTextGenerator ai, IOptions<ContractTemplateOptions> opt)
        {
            _ai = ai;
            _opt = opt.Value;
        }

        public async Task<GenerateContractDocumentResponse> GenerateAsync(
            GenerateContractDocumentRequest request,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(request.SignerName))
                throw new InvalidOperationException("SignerName is required (entered by the user).");

            if (request.Services == null || request.Services.Count == 0)
                throw new InvalidOperationException("At least one service is required.");

            // 1) اقرأ القالب الثابت
            var templatePath = Path.GetFullPath(_opt.BaseDePath);
            if (!File.Exists(templatePath))
                throw new InvalidOperationException($"Contract template not found: {templatePath}");

            var baseTemplate = await ReadTextSmartAsync(templatePath, ct);
            var fixedPath = Path.GetFullPath(_opt.FixedTermsDePath);
            if (!File.Exists(fixedPath))
                throw new InvalidOperationException($"Fixed terms file not found: {fixedPath}");

            var fixedTermsBody = await ReadTextSmartAsync(fixedPath, ct);

            var agbPath = Path.GetFullPath(_opt.AgbDePath);
            if (!File.Exists(agbPath))
                throw new InvalidOperationException($"AGB file not found: {agbPath}");

            var agbBody = await ReadTextSmartAsync(agbPath, ct);
            // 2) Customer block (placeholder أو override)
            var customerBlock = request.CustomerBlockOverride;
            if (string.IsNullOrWhiteSpace(customerBlock))
            {
                customerBlock = request.LeaveCustomerFieldsBlank
                    ? "Name/Firma: ____________________\nAdresse: ____________________\nPLZ/Ort: ____________________\n"
                    : "Name/Firma: (filled)\nAdresse: (filled)\nPLZ/Ort: (filled)\n";
            }

            // 3) Provider block من Options
            var providerBlock = _opt.ProviderBlock;

            // 4) جهّز payload للخدمات لـ GPT
            var servicesPayload = request.Services
                .OrderBy(s => s.Position)
                .Select(s => new
                {
                    position = s.Position,
                    title = s.Title,
                    serviceName = s.ServiceName,
                    serviceType = s.ServiceType,
                    pricingModel = s.PricingModel,
                    agreedPrice = request.IncludePricesInServicesSection ? s.AgreedPrice : null,
                    currency = request.Currency,
                    config = s.Config
                })
                .ToList();

            var prompt = BuildServicesPrompt(
                projectTitle: request.ProjectTitle,
                currency: request.Currency,
                includePrices: request.IncludePricesInServicesSection,
                servicesPayload: servicesPayload,
                additionalInstructions: request.AdditionalInstructions);

            // 5) GPT يولّد فقط قسم الخدمات
            var servicesSection = await _ai.GenerateTextAsync(prompt);
            servicesSection = CleanModelOutput(servicesSection);

            if (string.IsNullOrWhiteSpace(servicesSection))
                servicesSection = "*(No services section generated.)*";

            // 6) ادمج داخل القالب
            var tokens = new Dictionary<string, string?>
            {
                ["CONTRACT_NO"] = string.IsNullOrWhiteSpace(request.ContractNo) ? "____________________" : request.ContractNo,
                ["PROJECT_TITLE"] = request.ProjectTitle,
                ["PROVIDER_BLOCK"] = providerBlock,
                ["CUSTOMER_BLOCK"] = customerBlock,
                ["SIGNER_NAME"] = request.SignerName,
                ["FIXED_TERMS_BODY"] = fixedTermsBody,
                ["AGB_BODY"] = agbBody,               
                ["SERVICES_SECTION"] = servicesSection,
            };



            var full = ReplaceTokens(baseTemplate, tokens);

            // FixedTerms: نفس القالب لكن بدون الخدمات
            tokens["SERVICES_SECTION"] = "";
            var fixedTerms = ReplaceTokens(baseTemplate, tokens);

            return new GenerateContractDocumentResponse
            {
                FixedTerms = fixedTerms,
                ServicesSection = servicesSection,
                FullDocument = full
            };
        }
        private static async Task<string> ReadTextSmartAsync(string path, CancellationToken ct)
        {
            // 1) Try UTF-8 first
            var text = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);

            // If it looks broken (replacement chars), try Windows-1252
            if (LooksLikeBrokenEncoding(text))
            {
                // Windows-1252 is common for German text saved as ANSI on Windows
                var win1252 = Encoding.GetEncoding(1252);
                text = await File.ReadAllTextAsync(path, win1252, ct);

                // If still broken, last fallback: Latin1
                if (LooksLikeBrokenEncoding(text))
                {
                    text = await File.ReadAllTextAsync(path, Encoding.Latin1, ct);
                }
            }

            return text;
        }

        private static bool LooksLikeBrokenEncoding(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            // Common symptom: replacement character �
            if (text.Contains('�')) return true;

            // Another symptom: sequences like "Ã¼" instead of "ü"
            // (UTF-8 bytes read as Latin1)
            if (Regex.IsMatch(text, "Ã.|Â.")) return true;

            return false;
        }

        private static string ReplaceTokens(string template, Dictionary<string, string?> tokens)
        {
            var result = template;
            foreach (var kv in tokens)
            {
                result = result.Replace("{{" + kv.Key + "}}", kv.Value ?? "");
            }
            return result;
        }

        private static string CleanModelOutput(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            text = text.Trim();

            if (text.StartsWith("```"))
            {
                var firstNewLine = text.IndexOf('\n');
                if (firstNewLine >= 0) text = text[(firstNewLine + 1)..];
                var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (lastFence >= 0) text = text[..lastFence];
            }

            // لو رجع عنوان Anlage A بالغلط نحذفه
            var trimmed = text.TrimStart();
            if (trimmed.StartsWith("#") && trimmed.Contains("Anlage A") || trimmed.StartsWith("##") && trimmed.Contains("Anlage A"))
            {
                var idx = trimmed.IndexOf("\n\n", StringComparison.Ordinal);
                if (idx > 0)
                    trimmed = trimmed[(idx + 2)..];
                text = trimmed.Trim();
            }
            // Remove horizontal rules if model returns them anyway
            text = string.Join("\n",
                text.Split('\n')
                    .Where(l =>
                    {
                        var t = l.Trim();
                        return t != "---" && t != "***" && t != "___";
                    })
            );
            var lines = text.Split('\n')
                    .Where(l =>
                    {
                        var t = l.Trim();
                        return t != "---" && t != "***" && t != "___";
                    });

            text = string.Join("\n", lines).Trim();

            return text.Trim();
        }


        private static string BuildServicesPrompt(
            string projectTitle,
            string currency,
            bool includePrices,
            object servicesPayload,
            string? additionalInstructions)
        {
            var servicesJson = JsonSerializer.Serialize(servicesPayload, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            return $@"
You are generating ONLY the BODY content that will be inserted under:
""## Anlage A – Leistungsbeschreibung (variabel)"".

IMPORTANT:
- Do NOT write the title or any heading like ""Anlage A"".
- Start directly with: ""### Position 1: ..."" (and so on).
- Do NOT output horizontal rules like ""---"" or ""***"".
- Do NOT write any other contract sections, legal clauses, or signatures.

Rules:
- Output Markdown only (no code fences).
- Do NOT add horizontal rules (no '---', '***', or '___').
- Do NOT add any section separators; keep a clean list of positions.
- For each service item: include Scope, Deliverables, Out-of-scope, Customer responsibilities, Acceptance criteria.
- Keep it professional and client-friendly.
- {(includePrices ? "Include the agreed price per item if present." : "Do NOT include any prices.")}

Project: {projectTitle}
Currency: {currency}

Services (JSON):
{servicesJson}

Additional instructions:
{additionalInstructions ?? "(none)"}
";


        }
    }
}
