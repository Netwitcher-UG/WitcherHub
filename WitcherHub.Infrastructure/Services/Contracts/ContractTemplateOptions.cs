namespace WitcherHub.Infrastructure.Services.Contracts
{
    public class ContractTemplateOptions
    {
        public const string SectionName = "ContractTemplates";

        public string BaseDePath { get; set; } = "OpenAI/Contracts/contract_base_de.md";
        public string FixedTermsDePath { get; set; } = "OpenAI/Contracts/fixed_terms_de.md";

        public string AgbDePath { get; set; } = "OpenAI/Contracts/agb_de.md";

        public string ProviderBlock { get; set; } =
            "WitcherHub / Your Company Name\nAddress line\nCity, ZIP\n";
    }
}
