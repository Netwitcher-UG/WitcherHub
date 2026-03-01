using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WitcherHub.Application.Common.Exceptions;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Contracts;
using System.Globalization;


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

            if (request.Services == null || request.Services.Count == 0)
                throw new BadRequestAppException("At least one service (Position) is required.");

            request.SignerName ??= "";

            // 1) Load template
            var templatePath = Path.Combine(AppContext.BaseDirectory, _opt.BaseDePath);
            if (!File.Exists(templatePath))
                throw new InvalidOperationException($"Contract template not found: {templatePath}");

            var baseTemplate = await ReadTextSmartAsync(templatePath, ct);

            // 2) Load AGB
            var agbPath = Path.Combine(AppContext.BaseDirectory, _opt.AgbDePath);
            if (!File.Exists(agbPath))
                throw new InvalidOperationException($"AGB file not found: {agbPath}");

            var agbBody = await ReadTextSmartAsync(agbPath, ct);

            // 3) Prepare customer/provider blocks
            var customerBlock = request.CustomerBlockOverride;
            if (string.IsNullOrWhiteSpace(customerBlock))
            {
                customerBlock = request.LeaveCustomerFieldsBlank
                    ? "Name/Firma:\nAdresse:\nPLZ/Ort:\n"
                    : "Name/Firma: (filled)\nAdresse: (filled)\nPLZ/Ort: (filled)\n";
            }

            customerBlock = StripHtml(customerBlock);

            var providerBlock = _opt.ProviderBlock ?? "";

            providerBlock = FormatPartyBlockMarkdown(providerBlock);
            customerBlock = FormatPartyBlockMarkdown(customerBlock);

            // 4) Prepare payload for GPT
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

            var prompt = BuildStructuredServicesPrompt(
                projectTitle: request.ProjectTitle,
                currency: request.Currency,
                includePrices: request.IncludePricesInServicesSection,
                servicesPayload: servicesPayload,
                additionalInstructions: request.AdditionalInstructions);

            
            ContractStructuredTermsDto structured;

            if (request.StructuredOverride != null)
            {
                structured = request.StructuredOverride;
            }
            else
            {
                var aiRaw = await _ai.GenerateTextAsync(prompt);

                if (string.IsNullOrWhiteSpace(aiRaw))
                    throw new InvalidOperationException("AI returned empty structured response.");

                aiRaw = CleanModelOutput(aiRaw);

                try
                {
                    structured = JsonSerializer.Deserialize<ContractStructuredTermsDto>(
                        aiRaw,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    ) ?? throw new Exception("Deserialized object is null.");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("AI did not return valid structured JSON.", ex);
                }
            }

            if (structured.Positions == null || structured.Positions.Count == 0)
                throw new InvalidOperationException("Structured contract contains no positions.");

            structured.Version = "1.0";
            structured.Language = "de-DE";
            structured.GeneratedBy = "openai";
            structured.GeneratedAt = DateTimeOffset.UtcNow;

            // 5) Convert Structured → Markdown (temporary rendering layer)
            var servicesMarkdown = RenderStructuredToMarkdown(structured, request.Currency);
            // build fallback price map from line items (AgreedPrice)
            var agreedByPosition = request.Services
                .GroupBy(x => x.Position)
                .ToDictionary(g => g.Key, g => g.FirstOrDefault()?.AgreedPrice);

            var priceBox = BuildPriceBoxMarkdown(structured, agreedByPosition, request.Currency);
            // 6) Merge template
            var tokens = new Dictionary<string, string?>
            {
                ["CONTRACT_NO"] = string.IsNullOrWhiteSpace(request.ContractNo) ? "" : request.ContractNo,
                ["PROJECT_TITLE"] = request.ProjectTitle,
                ["PROVIDER_BLOCK"] = providerBlock,
                ["CUSTOMER_BLOCK"] = customerBlock,
                ["SIGNER_NAME"] = request.SignerName,
                ["AGB_BODY"] = agbBody,
                ["SERVICES_SECTION"] = servicesMarkdown,
                ["PRICE_BOX"] = priceBox, // ✅ new
            };

            var full = ReplaceTokens(baseTemplate, tokens);

            return new GenerateContractDocumentResponse
            {
                Structured = structured,
                ServicesSectionMarkdown = servicesMarkdown,
                FullDocument = full
            };
        }
        private static string BuildStructuredServicesPrompt(
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
You are generating structured data for ""Anlage A – Leistungsbeschreibung"" of a German agency contract.

Return JSON ONLY.
No markdown.
No explanations.
No code fences.

Schema (exactly, no extra properties):

{{
  ""version"": ""1.0"",
  ""language"": ""de-DE"",
  ""positions"": [
    {{
      ""positionNo"": 1,
      ""title"": ""string"",
      ""quantity"": 1,
      ""unitNetPrice"": null,
      ""lineNetPrice"": null,
      ""taxRatePercent"": 19,
      ""sections"": {{
        ""scope"": ""string"",
        ""deliverables"": [""string""],
        ""outOfScope"": [""string""],
        ""customerResponsibilities"": [""string""],
        ""acceptanceCriteria"": [""string""],
        ""timeline"": ""string"",
        ""assumptions"": ""string"",
        ""revisions"": ""string""
      }},
      ""customClauses"": []
    }}
  ]
}}

Hard Rules (must follow):
- German language only (de-DE).
- Professional, business tone.
- DO NOT write any legal terms, payment terms, due dates, cancellation rights, warranties, liability limits, or references to laws/paragraphs.
- DO NOT define any acceptance deadlines (no days/weeks/months). Acceptance is governed by AGB. Keep acceptanceCriteria as measurable checks only (format, completeness, consistency).
- No contradictions with AGB. If uncertain, stay generic and factual.
- Each list item must be a single line (no line breaks inside items).
- {(includePrices ? "Use provided pricing fields if available. Never invent prices." : "Do not invent pricing values.")}
- Do not add extra properties.

Project: {projectTitle}
Currency: {currency}

Services:
{servicesJson}

Additional instructions:
{additionalInstructions ?? "(none)"}
";
        }




        private static string RenderStructuredToMarkdown(
    ContractStructuredTermsDto structured,
    string currency)
        {
            var sb = new StringBuilder();

            foreach (var p in (structured.Positions ?? new()).OrderBy(x => x.PositionNo))
            {
                var title = string.IsNullOrWhiteSpace(p.Title) ? $"Position {p.PositionNo}" : p.Title.Trim();
                sb.AppendLine($"### Position {p.PositionNo}: {title}");
                sb.AppendLine();

                if (p.LineNetPrice.HasValue)
                {
                    sb.AppendLine($"**Preis (Netto):** {p.LineNetPrice.Value:0.00} {currency}");
                    sb.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(p.Sections?.Scope))
                {
                    sb.AppendLine("**Leistungsumfang (Scope)**");
                    sb.AppendLine();
                    sb.AppendLine(p.Sections.Scope.Trim());
                    sb.AppendLine();
                }

                WriteList(sb, "Liefergegenstände (Deliverables)", p.Sections?.Deliverables);
                WriteList(sb, "Nicht enthalten (Out-of-scope)", p.Sections?.OutOfScope);
                WriteList(sb, "Mitwirkungspflichten des Auftraggebers", p.Sections?.CustomerResponsibilities);
                WriteList(sb, "Abnahmekriterien", p.Sections?.AcceptanceCriteria);

                if (!string.IsNullOrWhiteSpace(p.Sections?.Timeline))
                {
                    sb.AppendLine("**Zeitplan**");
                    sb.AppendLine();
                    sb.AppendLine(p.Sections.Timeline.Trim());
                    sb.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(p.Sections?.Assumptions))
                {
                    sb.AppendLine("**Annahmen**");
                    sb.AppendLine();
                    sb.AppendLine(p.Sections.Assumptions.Trim());
                    sb.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(p.Sections?.Revisions))
                {
                    sb.AppendLine("**Überarbeitungen**");
                    sb.AppendLine();
                    sb.AppendLine(p.Sections.Revisions.Trim());
                    sb.AppendLine();
                }

                sb.AppendLine();
            }

            return sb.ToString().Trim();
        }

        private static void WriteList(StringBuilder sb, string title, List<string>? items)
        {
            if (items == null) return;

            var clean = items.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            if (clean.Count == 0) return;

            sb.AppendLine($"**{title}**");
            sb.AppendLine();
            foreach (var i in clean)
                sb.AppendLine($"- {i}");
            sb.AppendLine();
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

        private static string FormatPartyBlockMarkdown(string input)
        {
            input ??= "";
            input = StripHtml(input).Replace("\r\n", "\n").Trim();

            var lines = input.Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            if (lines.Count == 0) return "";

            // ✅ لا جدول نهائياً — فقط سطور مع hard breaks
            return ToMarkdownHardBreaks(string.Join("\n", lines));
        }
        private static string EscapeMd(string s)
        {
            s ??= "";
            return s.Replace("|", "\\|").Trim();
        }

        
        private static string BuildPriceBoxMarkdown(
    ContractStructuredTermsDto structured,
    IReadOnlyDictionary<int, decimal?> agreedByPosition,
    string currency)
        {
            var de = CultureInfo.GetCultureInfo("de-DE");

            structured ??= new ContractStructuredTermsDto();
            agreedByPosition ??= new Dictionary<int, decimal?>();
            currency = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency.Trim();

            var positions = (structured.Positions ?? new List<ContractPositionSpecDto>())
                .OrderBy(p => p.PositionNo)
                .ToList();

            if (positions.Count == 0)
                return "—";

            static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

            var rows = new List<(int Pos, string Title, decimal Net, decimal Rate)>();

            foreach (var p in positions)
            {
                var rate = p.TaxRatePercent.HasValue ? Convert.ToDecimal(p.TaxRatePercent.Value) : 19m;
                var qty = p.Quantity.HasValue ? Convert.ToDecimal(p.Quantity.Value) : 1m;

                decimal? netOpt = p.LineNetPrice;

                if (!netOpt.HasValue && p.UnitNetPrice.HasValue)
                    netOpt = p.UnitNetPrice.Value * qty;

                if (!netOpt.HasValue &&
                    agreedByPosition.TryGetValue(p.PositionNo, out var agreed) &&
                    agreed.HasValue)
                {
                    netOpt = agreed.Value;
                }

                var net = Round2(netOpt ?? 0m);

                var title = string.IsNullOrWhiteSpace(p.Title)
                    ? $"Position {p.PositionNo}"
                    : p.Title.Trim();

                rows.Add((p.PositionNo, title, net, rate));
            }

            var netTotal = Round2(rows.Sum(r => r.Net));

            var taxByRate = rows
                .GroupBy(r => r.Rate)
                .ToDictionary(
                    g => g.Key,
                    g => Round2(g.Sum(x => x.Net) * (g.Key / 100m))
                );

            var taxTotal = Round2(taxByRate.Values.Sum());
            var grossTotal = Round2(netTotal + taxTotal);

            var sb = new StringBuilder();
            sb.AppendLine("| Pos. | Bezeichnung | Netto |");
            sb.AppendLine("|---:|---|---:|");

            foreach (var r in rows)
            {
                sb.AppendLine($"| {r.Pos} | {EscapePipes(r.Title)} | {r.Net.ToString("N2", de)} {currency} |");
            }

            sb.AppendLine($"|  | **Zwischensumme (Netto)** | **{netTotal.ToString("N2", de)} {currency}** |");

            foreach (var kv in taxByRate.OrderBy(x => x.Key))
            {
                sb.AppendLine($"|  | **USt. {kv.Key.ToString("0.#", de)}%** | **{kv.Value.ToString("N2", de)} {currency}** |");
            }

            sb.AppendLine($"|  | **Gesamtbetrag (Brutto)** | **{grossTotal.ToString("N2", de)} {currency}** |");
            sb.AppendLine();
            sb.AppendLine("_Alle Beträge netto zzgl. gesetzlicher Umsatzsteuer, sofern nicht anders ausgewiesen. Zahlungsbedingungen gemäß Anlage B (AGB)._");

            return sb.ToString().Trim();

            static string EscapePipes(string s) => (s ?? "").Replace("|", "\\|").Trim();
        }



       
    }
}
