using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using System.Globalization;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Contracts;
using WitcherHub.Application.Models.DTO.Customers;
using WitcherHub.Application.Models.Email;
using WitcherHub.Application.Services.Email;
using WitcherHub.Configuration.Filters;
using WitcherHub.Infrastructure.Services.Lexware;
using WitcherHub.Resources;



namespace WitcherHub.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    // Diagnostics only. These endpoints send mail, spend OpenAI credits and can
    // delete Lexware customers, so they are unreachable outside Development and
    // still require an authenticated user there (via the global fallback policy).
    [DevelopmentOnly]
    public class TestController : ControllerBase
    {
        //for railway
        private readonly ILexwareClient _lexwareClient;
        private readonly IAiTextGenerator _aiTextGenerator;
        private readonly IAuthService _auth;
        private readonly ILexwareSyncService _sync;
        private readonly IEmailService _email;
        private readonly IStringLocalizer<SharedResource> T;
        private readonly IContractDocumentGenerator _contractDoc;
        public TestController(
            ILexwareClient lexwareClient,
            IAiTextGenerator aiTextGenerator,
            IAuthService auth,
            ILexwareSyncService sync,
            IEmailService email,
            IStringLocalizer<SharedResource> t,
            IContractDocumentGenerator contractDoc)
        {
            _lexwareClient = lexwareClient;
            _aiTextGenerator = aiTextGenerator;
            _auth = auth;
            _sync = sync;
            _email = email;
            T = t;
            _contractDoc = contractDoc;
        }



        // Endpoint يرسل قالب ContractReady حسب اللغة (en/de)
        // الملفات الموجودة عندك: ContractReady.de.html و ContractReady.en.html
        [HttpPost("email/test-contract-ready")]
        public async Task<IActionResult> SendTestContractReadyEmail(
            [FromQuery] string lang = "en",
            CancellationToken ct = default)
        {
            // ثبّت إيميلك هنا
            const string myEmail = "basel.slaby@gmail.com";

            // اختر اسم التيمبلت حسب ملفاتك بالضبط
            var templateName = (lang ?? "en").Trim().ToLowerInvariant() switch
            {
                "de" => "ContractReady.de",
                _ => "ContractReady.en"
            };

            var subject = "Contract ready for signature ✅";

            // نفس التوكنز المستخدمة داخل القالب
            var model = new
            {
                Subject = subject,                 // لو _Layout فيه {{Subject}}
                UserName = "Basel Slaby",           // Hello {{UserName}}
                ContractNo = "C-2026-000001",
                ProjectTitle = "website",
                SignedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'"),

                // مثل اللي ظاهر بالصورة (عدّل المسار إذا عندك مختلف)
                ActionUrl = $"{Request.Scheme}://{Request.Host}/contracts/sign/{Guid.NewGuid()}"
            };

            await _email.QueueTemplateAsync(
                templateName: templateName,
                model: model,
                to: new EmailAddress(myEmail, "Me"),
                subject: subject,
                ct: ct);

            return Ok(new { message = "Queued", templateName, to = myEmail, subject });
        }


        // Endpoint يرسل قالب ContractSigned.de (حسب ملفك الموجود)
        // الملف الموجود عندك: ContractSigned.de.html
        [HttpPost("email/test-contract-signed-de")]
        public async Task<IActionResult> SendTestContractSignedDeEmail(CancellationToken ct = default)
        {
            const string myEmail = "basel.slaby@gmail.com";

            var templateName = "ContractSigned.de";
            var subject = "Contract signed ✅";

            var model = new
            {
                Subject = subject,
                UserName = "Basel Slaby",
                ContractNo = "C-2026-000001",
                ProjectTitle = "website",
                SignedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'"),
                ActionUrl = $"{Request.Scheme}://{Request.Host}/contracts/{Guid.NewGuid()}"
            };

            await _email.QueueTemplateAsync(
                templateName: templateName,
                model: model,
                to: new EmailAddress(myEmail, "Me"),
                subject: subject,
                ct: ct);

            return Ok(new { message = "Queued", templateName, to = myEmail, subject });
        }

        [HttpGet("i18n-ping")]
        public IActionResult I18nPing([FromQuery] string? lang = null)
        {
            if (!string.IsNullOrWhiteSpace(lang))
            {
                var culture = new CultureInfo(lang);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
            }

            var s = T["Hello"];

            return Ok(new
            {
                lang = CultureInfo.CurrentUICulture.Name,
                value = s.Value,
                notFound = s.ResourceNotFound
            });
        }



        [HttpPost("lexware/import/contacts")]
        public async Task<IActionResult> ImportLexwareContacts(CancellationToken ct)
        {
            var res = await _sync.ImportAllContactsAsync(ct);
            return Ok(res);
        }
        [HttpPost("lexware/export/customer")]
        public async Task<IActionResult> ExportCustomerToLexware(
            [FromBody] CustomerIdRequest req,
            CancellationToken ct)
        {
            if (req == null || req.CustomerId == Guid.Empty)
                return BadRequest(new { message = "CustomerId is missing." });

            var updated = await _sync.ExportCustomerAsync(req.CustomerId, ct);

            return Ok(updated);
        }
        [HttpPost("lexware/delete/customer")]
        public async Task<IActionResult> DeleteCustomerFromLexware(
    [FromBody] LexwareDeleteRequest req,
    CancellationToken ct)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.ContactId))
                return BadRequest(new { message = "ContactId is missing." });

            var updated = await _sync.DeleteCustomerFromLexwareAsync(req.ContactId, ct);

            return Ok(updated);
        }



        /// <summary>
        /// يجلب صفحة Contacts من Lexware كـ JSON خام
        /// </summary>
        [HttpGet("contacts")]
        public async Task<IActionResult> GetContacts([FromQuery] int page = 0, CancellationToken cancellationToken = default)
        {
            var json = await _lexwareClient.GetContactsPageAsync(page, cancellationToken);
            return Content(json, "application/json");
        }
        [HttpGet("contacts/all")]
        public async Task<IActionResult> GetAllContacts(CancellationToken ct)
        {
            var items = await _lexwareClient.GetAllContactsAsync(ct);
            return Ok(items);
        }

        [HttpGet("ai-demo")]
        public async Task<IActionResult> AiDemo([FromQuery] string? prompt)
        {
            prompt ??= "دردش مع شات جي بي تي";

            try
            {
                var response = await _aiTextGenerator.GenerateTextAsync(prompt);

                return Ok(new
                {
                    Prompt = prompt,
                    Response = response
                });
            }
            catch (Exception ex)
            {
                // يرجّع خطأ واضح وقت التجربة
                return Problem(
                    title: "OpenAI call failed",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError
                );
            }
        }

        [HttpPost("contract/preview")]
        public async Task<IActionResult> ContractPreview([FromBody] GenerateContractDocumentRequest request, CancellationToken ct)
        {
            var result = await _contractDoc.GenerateAsync(request, ct);
            return Ok(result);
        }


        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
        {
            var result = await _auth.LoginAsync(request, ct);

            Response.Cookies.Append("access_token", result.AccessToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = result.ExpiresAtUtc
            });

            return Ok(result);
        }

        [HttpPost("logout")]
        [AllowAnonymous]
        public IActionResult Logout()
        {
            Response.Cookies.Delete("access_token");
            return Ok();
        }

        public sealed class SendWelcomeEmailRequest
        {
            public string ToEmail { get; set; } = "";
            public string? ToName { get; set; }
            public string UserName { get; set; } = "";
            public string ActionUrl { get; set; } = "";
        }


        [HttpPost("email/welcome")]
        public async Task<IActionResult> SendWelcomeEmail([FromBody] SendWelcomeEmailRequest req, CancellationToken ct)
        {
            if (req is null || string.IsNullOrWhiteSpace(req.ToEmail))
                return BadRequest(new { message = "ToEmail is required." });

            if (string.IsNullOrWhiteSpace(req.ActionUrl))
                return BadRequest(new { message = "ActionUrl is required." });

            var to = new EmailAddress(req.ToEmail, req.ToName);

            // مهم: نمرر Subject داخل model لكي يظهر في _Layout.html إذا استخدمت {{Subject}}
            var subject = "Welcome to WitcherHub";

            await _email.QueueTemplateAsync(
                templateName: "Welcome",
                model: new
                {
                    Subject = subject,
                    AppName = "WitcherHub",
                    UserName = req.UserName,
                    ActionUrl = req.ActionUrl
                },
                to: to,
                subject: subject,
                ct: ct);

            return Ok(new { message = "Queued. Check logs for sending result." });
        }

    }
}
