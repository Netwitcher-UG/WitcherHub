using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WitcherHub.Infrastructure.Services.Lexware;

namespace WitcherHub.Controllers
{
    [ApiController]
    [Route("api/lexware/webhooks")]
    public sealed class LexwareWebhookController : ControllerBase
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly LexwareInvoiceStatusSyncService _statusSyncService;
        private readonly LexwareWebhookOptions _options;
        private readonly ILogger<LexwareWebhookController> _logger;

        public LexwareWebhookController(
            LexwareInvoiceStatusSyncService statusSyncService,
            IOptions<LexwareWebhookOptions> options,
            ILogger<LexwareWebhookController> logger)
        {
            _statusSyncService = statusSyncService;
            _options = options.Value;
            _logger = logger;
        }

        [HttpHead]
        [AllowAnonymous]
        public IActionResult Head() => Ok();

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> PostAsync(CancellationToken ct)
        {
            string rawBody;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                rawBody = await reader.ReadToEndAsync(ct);
            }

            if (!VerifySignature(rawBody, Request.Headers["X-Lxo-Signature"]))
                return Unauthorized();

            LexwareWebhookPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<LexwareWebhookPayload>(rawBody, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid Lexware webhook payload.");
                return BadRequest();
            }

            if (payload == null)
                return BadRequest();

            await _statusSyncService.HandleWebhookAsync(payload, ct);
            return NoContent();
        }

        private bool VerifySignature(string rawBody, string? signatureHeader)
        {
            if (!_options.VerifySignature)
                return true;

            if (string.IsNullOrWhiteSpace(signatureHeader))
            {
                _logger.LogWarning("Lexware webhook rejected because X-Lxo-Signature is missing.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(_options.PublicKeyPem))
            {
                _logger.LogWarning("Lexware webhook rejected because no public key PEM is configured.");
                return false;
            }

            try
            {
                var canonicalJson = CanonicalizeJson(rawBody);
                var bodyBytes = Encoding.UTF8.GetBytes(canonicalJson);
                var signatureBytes = Convert.FromBase64String(signatureHeader);

                using var rsa = RSA.Create();
                rsa.ImportFromPem(_options.PublicKeyPem);

                return rsa.VerifyData(
                    bodyBytes,
                    signatureBytes,
                    HashAlgorithmName.SHA512,
                    RSASignaturePadding.Pkcs1);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lexware webhook signature verification failed.");
                return false;
            }
        }

        private static string CanonicalizeJson(string rawBody)
        {
            using var doc = JsonDocument.Parse(rawBody);
            return JsonSerializer.Serialize(doc.RootElement);
        }
    }
}
