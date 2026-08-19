using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Infrastructure.Services.Contracts;

namespace WitcherHub.Pages.Contracts
{
    public class SignDemoModel : PageModel
    {
        private readonly IContractDocumentGenerator _generator;
        private readonly ContractTemplateOptions _opt;

        public string ContractHtml { get; private set; } = "";
        public string ContractMarkdown { get; private set; } = "";

        public string ProviderName { get; private set; } = "";
        public string ProviderAddress { get; private set; } = "";

        public SignDemoModel(IContractDocumentGenerator generator, IOptions<ContractTemplateOptions> opt)
        {
            _generator = generator;
            _opt = opt.Value;
        }

        public async Task OnGetAsync(CancellationToken ct)
        {
            var req = new GenerateContractDocumentRequest
            {
                ContractNo = "DEMO-001",
                ProjectTitle = "Videos Animation",
                SignerName = "", // ✅ name is filled on the page (input)
                Currency = "EUR",
                LeaveCustomerFieldsBlank = true,
                IncludePricesInServicesSection = true,
                Services = new List<ContractServiceLineDto>
                {
                    new ContractServiceLineDto
                    {
                        Position = 1,
                        Title = "Video Animation",
                        ServiceName = "Erklärvideo / Animation",
                        ServiceType = "Video",
                        PricingModel = "Fixed",
                        AgreedPrice = 0m,
                        Config = new Dictionary<string, object>()
                    }
                }
            };

            // Provider block split: first line = name, rest = address
            var pb = NormalizeNewLines(_opt.ProviderBlock ?? "");
            var lines = pb.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            ProviderName = lines.Length > 0 ? lines[0] : "";
            ProviderAddress = lines.Length > 1 ? string.Join("\n", lines.Skip(1)) : "";

            var doc = await _generator.GenerateAsync(req, ct);

            ContractMarkdown = NormalizeNewLines(doc.FullDocument);

            ContractHtml = Rendering.ContractMarkdown.ToHtml(ContractMarkdown);
        }

        private static string NormalizeNewLines(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r\n", "\n");
        }
    }
}
