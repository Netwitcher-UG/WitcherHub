using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Contracts;

namespace WitcherHub.Pages.Contracts
{
    public class SignDemoModel : PageModel
    {
        private readonly IContractDocumentGenerator _generator;

        public string ContractHtml { get; private set; } = "";
        public string ContractMarkdown { get; private set; } = "";

        public SignDemoModel(IContractDocumentGenerator generator)
        {
            _generator = generator;
        }

        public async Task OnGetAsync()
        {
            // ✅ بيانات افتراضية للتجريب مثل Swagger تمامًا
            var req = new GenerateContractDocumentRequest
            {
                ContractNo = "C-DEMO-001",
                ProjectTitle = "Demo Website Project",
                Currency = "EUR",
                SignerName = "Ahmed Ali",
                SignerEmail = "ahmed@example.com",
                LeaveCustomerFieldsBlank = true,
                IncludePricesInServicesSection = false,
                AdditionalInstructions = "Use concise bullet points. Mention timeline if present in config. Do NOT add '---'.",
                Services = new List<ContractServiceLineDto>
                {
                    new()
                    {
                        Position = 1,
                        Title = "Website Development",
                        ServiceName = "Website",
                        ServiceType = "Web",
                        PricingModel = "Fixed",
                        AgreedPrice = 1500,
                        Config = new Dictionary<string, object>
                        {
                            ["pages"] = 5,
                            ["languages"] = new [] { "de", "en" },
                            ["revisionsIncluded"] = 2,
                            ["timelineWeeks"] = 3,
                            ["includesCMS"] = true,
                            ["includesContactForm"] = true,
                            ["seoBasicsIncluded"] = true
                        }
                    },
                    new()
                    {
                        Position = 2,
                        Title = "SEO Setup",
                        ServiceName = "SEO",
                        ServiceType = "Marketing",
                        PricingModel = "Fixed",
                        AgreedPrice = 400,
                        Config = new Dictionary<string, object>
                        {
                            ["keywordCount"] = 10,
                            ["technicalAudit"] = true,
                            ["onpageSetup"] = new [] { "metaTitles", "metaDescriptions", "sitemap", "robots" },
                            ["reporting"] = "monthly"
                        }
                    },
                    new()
                    {
                        Position = 3,
                        Title = "Hosting (12 months)",
                        ServiceName = "Web Hosting",
                        ServiceType = "Hosting",
                        PricingModel = "Subscription",
                        AgreedPrice = 240,
                        Config = new Dictionary<string, object>
                        {
                            ["durationMonths"] = 12,
                            ["storageGB"] = 10,
                            ["emailAccounts"] = 5,
                            ["sslIncluded"] = true,
                            ["backups"] = "daily",
                            ["support"] = "business-hours"
                        }
                    }
                }
            };

            var res = await _generator.GenerateAsync(req);

            // Markdown
            ContractMarkdown = NormalizeNewLines(res.FullDocument);

            // Markdown -> HTML
            var pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();

            var rawHtml = Markdown.ToHtml(ContractMarkdown, pipeline);

            // ✅ Sanitize (مهم لأنك تستخدم Html.Raw)
            var sanitizer = new HtmlSanitizer();
            sanitizer.AllowedSchemes.Add("data"); // احتياط لو عندك data-url لاحقًا
            ContractHtml = sanitizer.Sanitize(rawHtml);
        }

        private static string NormalizeNewLines(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r\n", "\n");
        }
    }
}
