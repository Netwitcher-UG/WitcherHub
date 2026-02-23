using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WitcherHub.Controllers;

[ApiController]
[Route("api/email-assets")]
public class EmailAssetsController : ControllerBase
{
    private readonly IWebHostEnvironment _env;

    public EmailAssetsController(IWebHostEnvironment env)
    {
        _env = env;
    }

    // 1) صورة الفوتر (Signature)
    [AllowAnonymous]
    [HttpGet("footer-signature")]
    [Produces("image/png")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public IActionResult FooterSignature()
        => PngFromWwwroot("email-assets/footer-signature.png");

    // 2) صورة watermark للبوكس (شفافة)
    [AllowAnonymous]
    [HttpGet("box-watermark")]
    [Produces("image/png")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public IActionResult BoxWatermark()
        => PngFromWwwroot("email-assets/box-watermark.png");
    [AllowAnonymous]
    [HttpGet("header")]
    [Produces("image/png")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public IActionResult Header()
    {
        Response.Headers["Cache-Control"] = "public,max-age=31536000,immutable";
        Response.Headers["Content-Disposition"] = "inline";
        return PngFromWwwroot("email-assets/header.png");
    }
    private IActionResult PngFromWwwroot(string relativePath)
    {
        var path = Path.Combine(_env.WebRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!System.IO.File.Exists(path))
            return NotFound(new { message = "File not found", relativePath });

        // كاش قوي
        Response.Headers["Cache-Control"] = "public,max-age=31536000,immutable";

        return PhysicalFile(path, "image/png", enableRangeProcessing: true);
    }
}