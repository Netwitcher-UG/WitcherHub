using System.Text.Json;
using WitcherHub.Application.Interfaces;

namespace WitcherHub.Infrastructure.Services.Lexware
{
    public sealed class LexwareClient : ILexwareClient
    {
        private readonly HttpClient _http;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public LexwareClient(HttpClient httpClient)
        {
            _http = httpClient;
        }

        // ====== Existing: get one page as raw json ======
        public async Task<string> GetContactsPageAsync(int page = 0, CancellationToken cancellationToken = default)
        {
            using var response = await _http.GetAsync($"/v1/contacts?page={page}", cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        // ====== NEW: get ALL contacts (all pages) ======
        // (Add this signature to ILexwareClient)
        public async Task<IReadOnlyList<JsonElement>> GetAllContactsAsync(CancellationToken cancellationToken = default)
            => await GetAllPagedAsync("/v1/contacts", cancellationToken);

        // ====== Generic paged fetcher for any Lexware collection endpoint ======
        // Example later: await GetAllPagedAsync("/v1/invoices", ct)
        private async Task<IReadOnlyList<JsonElement>> GetAllPagedAsync(string basePath, CancellationToken ct)
        {
            var all = new List<JsonElement>();
            var page = 0;

            while (true)
            {
                using var response = await _http.GetAsync($"{basePath}?page={page}", ct);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

                var root = doc.RootElement;

                // Lexware غالبًا يرجّع: { content: [...], last: true/false, ... }
                if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                {
                    // fallback: لو رجّع Array مباشرة
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in root.EnumerateArray())
                            all.Add(item.Clone());
                    }
                    break;
                }

                var any = false;
                foreach (var item in content.EnumerateArray())
                {
                    any = true;
                    all.Add(item.Clone()); // مهم لأن doc سيتم Dispose
                }

                var isLast = root.TryGetProperty("last", out var lastEl) && lastEl.ValueKind == JsonValueKind.True;

                // أمان إضافي: لو الصفحة فاضية وقف
                if (isLast || !any) break;

                page++;
            }

            return all;
        }
    }
}
