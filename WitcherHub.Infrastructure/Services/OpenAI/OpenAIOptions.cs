
namespace WitcherHub.Infrastructure.Services.OpenAI
{
    public class OpenAIOptions
    {
        public const string SectionName = "OpenAI";

        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "gpt-4o";
    }
}
