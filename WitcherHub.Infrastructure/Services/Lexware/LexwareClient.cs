
using WitcherHub.Application.Interfaces;

namespace WitcherHub.Infrastructure.Services.Lexware
{
    public class LexwareClient : ILexwareClient
    {
        private readonly HttpClient _httpClient;

        public LexwareClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<string> GetContactsPageAsync(int page = 0, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.GetAsync($"v1/contacts?page={page}", cancellationToken);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
    }
}
