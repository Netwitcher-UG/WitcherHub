using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WitcherHub.Application.Interfaces;

namespace WitcherHub.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TestController : ControllerBase
    {
        private readonly ILexwareClient _lexwareClient;
        private readonly IAiTextGenerator _aiTextGenerator;
        private readonly IAuthService _auth;

        public TestController(
            ILexwareClient lexwareClient,
            IAiTextGenerator aiTextGenerator,
            IAuthService auth)
        {
            _lexwareClient = lexwareClient;
            _aiTextGenerator = aiTextGenerator;
            _auth = auth;
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
    }
}
