using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

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

    [AllowAnonymous]
    [HttpGet("footer-signature")]
    [Produces("image/png")]
    public IActionResult FooterSignature()
        => ImageFromWwwroot("email-assets/footer-signature.png", "image/png");

    [AllowAnonymous]
    [HttpGet("header")]
    [Produces("image/png")]
    public IActionResult Header()
        => ImageFromWwwroot("email-assets/header.png", "image/png");

    private IActionResult ImageFromWwwroot(string relativePath, string contentType)
    {
        var path = Path.Combine(_env.WebRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!System.IO.File.Exists(path))
            return NotFound(new { message = "File not found", relativePath });

        var fi = new FileInfo(path);
        var lastModified = fi.LastWriteTimeUtc;

        // ETag based on file length + last write time
        var etagValue = $"\"{fi.Length:x}-{lastModified.Ticks:x}\"";
        var etag = new EntityTagHeaderValue(etagValue);

        // Force revalidation (kills the old "immutable" behavior for NEW requests)
        Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = TimeSpan.Zero,
            MustRevalidate = true
        };
        Response.GetTypedHeaders().ETag = etag;
        Response.GetTypedHeaders().LastModified = lastModified;
        Response.Headers[HeaderNames.Pragma] = "no-cache";
        Response.Headers[HeaderNames.Expires] = "0";
        Response.Headers["Content-Disposition"] = "inline";

        // If client has the same ETag -> 304
        if (Request.Headers.TryGetValue(HeaderNames.IfNoneMatch, out var inm) && inm.ToString() == etagValue)
            return StatusCode(StatusCodes.Status304NotModified);

        return PhysicalFile(path, contentType, enableRangeProcessing: true);
    }
}