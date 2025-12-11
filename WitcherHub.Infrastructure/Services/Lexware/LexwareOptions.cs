
namespace WitcherHub.Infrastructure.Services.Lexware
{
    public class LexwareOptions
    {
        public const string SectionName = "Lexware";

        public string BaseUrl { get; set; } = "https://api.lexware.io";
        public string AccessToken { get; set; } = string.Empty;
    }
}
