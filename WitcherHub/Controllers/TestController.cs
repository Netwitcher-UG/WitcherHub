using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WitcherHub.Application.Interfaces;
using WitcherHub.Application.Models.DTO.Customers;
using WitcherHub.Application.Models.Email;
using WitcherHub.Application.Services.Email;
using WitcherHub.Infrastructure.Services.Lexware;

namespace WitcherHub.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TestController : ControllerBase
    {
        private readonly ILexwareClient _lexwareClient;
        private readonly IAiTextGenerator _aiTextGenerator;
        private readonly IAuthService _auth;
        private readonly ILexwareSyncService _sync;
        private readonly IEmailService _email;

        public TestController(
            ILexwareClient lexwareClient,
            IAiTextGenerator aiTextGenerator,
            IAuthService auth,
            ILexwareSyncService sync,
            IEmailService email)
        {
            _lexwareClient = lexwareClient;
            _aiTextGenerator = aiTextGenerator;
            _auth = auth;
            _sync = sync;
            _email = email;
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

        // لاختبار OpenAI
        [HttpGet("ai-demo")]
        public async Task<IActionResult> AiDemo(
            [FromQuery] string prompt = "دردش مع شات جي بي تي")
        {
            var response = await _aiTextGenerator.GenerateTextAsync(prompt);

            return Ok(new
            {
                Prompt = prompt,
                Response = response
            });
        }

        


        [HttpPost("login")]
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
