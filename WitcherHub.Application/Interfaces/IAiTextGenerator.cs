
namespace WitcherHub.Application.Interfaces
{
    public interface IAiTextGenerator
    {
        Task<string> GenerateTextAsync(string prompt);
    }
}
