using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WitcherHub.Application.Common.Exceptions;
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
            if (request is null)
                throw new ArgumentNullException(nameof(request));

            request.SignerName ??= "";

            // ✅ هذا مكانه الصحيح: نرمي Exception فقط (ولا نرجع JsonResult)
            if (request.Services == null || request.Services.Count == 0)
                throw new BadRequestAppException("At least one service (Position) is required.");

            // 1) Template
            var templatePath = Path.Combine(AppContext.BaseDirectory, _opt.BaseDePath);
            if (!File.Exists(templatePath))
                throw new InvalidOperationException($"Contract template not found: {templatePath}");

            var baseTemplate = await ReadTextSmartAsync(templatePath, ct);

            // 2) AGB
            var agbPath = Path.Combine(AppContext.BaseDirectory, _opt.AgbDePath);
            if (!File.Exists(agbPath))
                throw new InvalidOperationException($"AGB file not found: {agbPath}");

            var agbBody = await ReadTextSmartAsync(agbPath, ct);

            // 3) Customer block
            var customerBlock = request.CustomerBlockOverride;
            if (string.IsNullOrWhiteSpace(customerBlock))
            {
                customerBlock = request.LeaveCustomerFieldsBlank
                    ? "Name/Firma:\nAdresse:\nPLZ/Ort:\n"
                    : "Name/Firma: (filled)\nAdresse: (filled)\nPLZ/Ort: (filled)\n";
            }

            customerBlock = StripHtml(customerBlock);

            // 4) Provider block
            var providerBlock = _opt.ProviderBlock ?? "";

            // ✅ IMPORTANT: Preserve line breaks in markdown
            providerBlock = ToMarkdownHardBreaks(providerBlock);
            customerBlock = ToMarkdownHardBreaks(customerBlock);

            // 5) Services payload for GPT
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

            // إذا دالتك تدعم ct استخدمها، إذا لا تدعم اتركها كما كانت
            var servicesSection = await _ai.GenerateTextAsync(prompt /*, ct*/);
            servicesSection = CleanModelOutput(servicesSection);

            if (string.IsNullOrWhiteSpace(servicesSection))
                servicesSection = "*(No services section generated.)*";

            // 6) Merge
            var tokens = new Dictionary<string, string?>
            {
                ["CONTRACT_NO"] = string.IsNullOrWhiteSpace(request.ContractNo) ? "" : request.ContractNo,
                ["PROJECT_TITLE"] = request.ProjectTitle,
                ["PROVIDER_BLOCK"] = providerBlock,
                ["CUSTOMER_BLOCK"] = customerBlock,
                ["SIGNER_NAME"] = request.SignerName,
                ["AGB_BODY"] = agbBody,
                ["SERVICES_SECTION"] = servicesSection,
            };

            var full = ReplaceTokens(baseTemplate, tokens);

            return new GenerateContractDocumentResponse
            {
                FixedTerms = "",
                ServicesSection = servicesSection,
                FullDocument = full
            };
        }


        private static string StripHtml(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return Regex.Replace(input, "<.*?>", string.Empty);
        }

        // ✅ This fixes "Address line City, ZIP" collapsing into one line
        private static string ToMarkdownHardBreaks(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            input = input.Replace("\r\n", "\n");
            var lines = input.Split('\n')
                .Select(l => l.TrimEnd())
                .Where(l => l.Length > 0);
            return string.Join("  \n", lines); // markdown hard line break
        }

        private static async Task<string> ReadTextSmartAsync(string path, CancellationToken ct)
        {
            var text = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);

            if (LooksLikeBrokenEncoding(text))
            {
                var win1252 = Encoding.GetEncoding(1252);
                text = await File.ReadAllTextAsync(path, win1252, ct);

                if (LooksLikeBrokenEncoding(text))
                    text = await File.ReadAllTextAsync(path, Encoding.Latin1, ct);
            }

            return text;
        }

        private static bool LooksLikeBrokenEncoding(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (text.Contains('�')) return true;
            if (Regex.IsMatch(text, "Ã.|Â.")) return true;
            return false;
        }

        private static string ReplaceTokens(string template, Dictionary<string, string?> tokens)
        {
            var result = template;
            foreach (var kv in tokens)
                result = result.Replace("{{" + kv.Key + "}}", kv.Value ?? "");
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

            var trimmed = text.TrimStart();
            if ((trimmed.StartsWith("#") || trimmed.StartsWith("##")) && trimmed.Contains("Anlage A"))
            {
                var idx = trimmed.IndexOf("\n\n", StringComparison.Ordinal);
                if (idx > 0) trimmed = trimmed[(idx + 2)..];
                text = trimmed.Trim();
            }

            var lines = text.Split('\n')
                .Where(l =>
                {
                    var t = l.Trim();
                    return t != "---" && t != "***" && t != "___";
                });

            return string.Join("\n", lines).Trim();
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
- Output language: German (DE).
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
