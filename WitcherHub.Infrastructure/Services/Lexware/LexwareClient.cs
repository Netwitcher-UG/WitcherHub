using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using WitcherHub.Application.Interfaces;

namespace WitcherHub.Infrastructure.Services.Lexware
{
    public sealed class LexwareClient : ILexwareClient
    {
        private readonly HttpClient _http;

        // خيار JSON موحّد (مفيد للـ invoices + parsing)
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public LexwareClient(HttpClient httpClient)
        {
            _http = httpClient;
        }

        // =========================================
        // Retry helper (FIXED) - لا يعيد إرسال نفس Request
        // =========================================
        private async Task<HttpResponseMessage> SendWithRetryAsync(
            Func<HttpRequestMessage> requestFactory,
            CancellationToken ct)
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                using var req = requestFactory();
                var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

                if (res.StatusCode != (HttpStatusCode)429)
                    return res; // caller must dispose

                // 429 => لازم نعمل Dispose قبل الانتظار
                res.Dispose();

                // Backoff بسيط (Lexware: rate limit منخفض)
                await Task.Delay(800 + attempt * 400, ct);
            }

            using var lastReq = requestFactory();
            return await _http.SendAsync(lastReq, HttpCompletionOption.ResponseHeadersRead, ct);
        }

        // =========================================
        // Contacts (existing)
        // =========================================

        public async Task<string> GetContactsPageAsync(int page = 0, CancellationToken cancellationToken = default)
        {
            using var response = await _http.GetAsync($"/v1/contacts?page={page}", cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<JsonElement>> GetAllContactsAsync(CancellationToken cancellationToken = default)
            => await GetAllPagedAsync("/v1/contacts", cancellationToken);

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

                if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                {
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
                    all.Add(item.Clone());
                }

                var isLast = root.TryGetProperty("last", out var lastEl) && lastEl.ValueKind == JsonValueKind.True;
                if (isLast || !any) break;

                page++;
            }

            return all;
        }

        public async Task<JsonElement> CreateContactAsync(object payload, CancellationToken ct = default)
        {
            using var res = await _http.PostAsJsonAsync("/v1/contacts", payload, ct);
            if (!res.IsSuccessStatusCode)
            {
                var error = await res.Content.ReadAsStringAsync(ct);
                throw new Exception($"Lexware API error: {res.StatusCode} => {error}");
            }

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.Clone();
        }

        public async Task DeleteContactAsync(string lexwareContactId, CancellationToken ct = default)
        {
            using var res = await _http.DeleteAsync($"/v1/contacts/{lexwareContactId}", ct);
            res.EnsureSuccessStatusCode();
        }

        // =========================================
        // ✅ Invoices (MERGED FROM LexwareClient2) - PUBLIC
        // =========================================

        public async Task<LexwareActionResult> CreateInvoiceAsync(
            LexwareInvoiceCreateRequest body,
            bool finalize,
            CancellationToken ct = default)
        {
            var url = finalize ? "/v1/invoices?finalize=true" : "/v1/invoices";
            var json = JsonSerializer.Serialize(body, JsonOpts);

            using var res = await SendWithRetryAsync(() =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                return req;
            }, ct);

            var payload = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"Lexware CreateInvoice failed: {(int)res.StatusCode} {payload}");

            return JsonSerializer.Deserialize<LexwareActionResult>(payload, JsonOpts)
                   ?? throw new InvalidOperationException("Lexware CreateInvoice: empty response");
        }
        public async Task FinalizeInvoiceAsync(
      string id,
      CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Invoice id is required.", nameof(id));

            var url = $"/v1/invoices/{id}?finalize=true";

            using var res = await SendWithRetryAsync(() =>
            {
                var req = new HttpRequestMessage(HttpMethod.Put, url);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                return req;
            }, ct);

            var payload = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"Lexware FinalizeInvoice failed: {(int)res.StatusCode} {payload}");
        }
        public async Task<JsonDocument> GetInvoiceAsync(string id, CancellationToken ct = default)
        {
            using var res = await SendWithRetryAsync(() =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, $"/v1/invoices/{id}");
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return req;
            }, ct);

            var payload = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"Lexware GetInvoice failed: {(int)res.StatusCode} {payload}");

            return JsonDocument.Parse(payload);
        }

        public async Task<byte[]> DownloadInvoiceFileAsync(
            string id,
            string accept,
            CancellationToken ct = default)
        {
            using var res = await SendWithRetryAsync(() =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, $"/v1/invoices/{id}/file");
                req.Headers.Accept.Clear();
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
                return req;
            }, ct);

            if (!res.IsSuccessStatusCode)
            {
                var txt = await res.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"Lexware DownloadInvoiceFile failed: {(int)res.StatusCode} {txt}");
            }

            return await res.Content.ReadAsByteArrayAsync(ct);
        }
    }
}
