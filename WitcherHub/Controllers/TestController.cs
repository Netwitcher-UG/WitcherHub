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

        public TestController(
            ILexwareClient lexwareClient,
            IAiTextGenerator aiTextGenerator)
        {
            _lexwareClient = lexwareClient;
            _aiTextGenerator = aiTextGenerator;
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
    }
}
