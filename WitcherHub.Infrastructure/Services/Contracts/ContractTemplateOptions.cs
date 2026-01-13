namespace WitcherHub.Infrastructure.Services.Contracts
{
    public class ContractTemplateOptions
    {
        public const string SectionName = "ContractTemplates";

        public string FixedTermsDePath { get; set; } = "OpenAI/Contracts/fixed_terms_de.md";

        public string BaseDePath { get; set; } = "OpenAI/Contracts/Agenturvertrag.de.md";
        public string AgbDePath { get; set; } = "OpenAI/Contracts/AGB.de.md";
        public string ProviderBlock { get; set; } =
           "WitcherHub / netwitcher\n" +
           "Berlin, Deutschland\n" +
           "Address line\n";
        
    }
}
