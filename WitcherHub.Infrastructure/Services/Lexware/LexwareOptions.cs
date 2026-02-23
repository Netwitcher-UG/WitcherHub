
namespace WitcherHub.Infrastructure.Services.Lexware
{
    public class LexwareOptions
    {
        public const string SectionName = "Lexware";

        public string BaseUrl { get; set; } = "https://api.lexware.io";
        public string AccessToken { get; set; } = string.Empty;
    

        // ✅ مدموجين من LexwareOptions2 (Defaults)
        public string AppBaseUrl { get; set; } = "https://app.lexware.de";
        public string DefaultCountryCode { get; set; } = "DE";
        public decimal DefaultTaxRatePercentage { get; set; } = 0m;
        public string DefaultPaymentTermLabel { get; set; } = "Zahlbar sofort";
        public int DefaultPaymentTermDays { get; set; } = 0;

    }
}
