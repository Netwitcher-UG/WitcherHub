
using System.Text.Json;

namespace WitcherHub.Application.Interfaces
{
    public interface ILexwareClient
    {
        Task<string> GetContactsPageAsync(int page = 0, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<JsonElement>> GetAllContactsAsync(CancellationToken cancellationToken = default);

    }
}
