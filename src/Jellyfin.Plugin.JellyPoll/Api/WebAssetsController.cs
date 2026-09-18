using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyPoll.Api;

/// <summary>
/// Serves the embedded SPA at /JellyPoll/Web/** (doc 04 §4.12, doc 05 §9).
/// Anonymous by design: assets contain no data; the SPA authenticates API calls itself.
/// </summary>
[ApiController]
[Route("JellyPoll/Web")]
public sealed class WebAssetsController : ControllerBase
{
    private const string ResourcePrefix = "Jellyfin.Plugin.JellyPoll.Web.";

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".ico"] = "image/x-icon",
        [".json"] = "application/json",
        [".txt"] = "text/plain; charset=utf-8",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".map"] = "application/json"
    };

    [HttpGet]
    [HttpGet("{**path}")]
    public IActionResult GetAsset(string? path)
    {
        var assembly = typeof(Plugin).Assembly;

        // Empty or unknown paths resolve to index.html (hash routing needs no server fallbacks).
        var relative = string.IsNullOrWhiteSpace(path) ? "index.html" : path.TrimStart('/');

        // Reject path traversal.
        if (relative.Contains("..") || relative.Contains('\\'))
        {
            return BadRequest();
        }

        var resourceName = ResourcePrefix + relative.Replace('/', '.');

        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null && relative != "index.html")
        {
            // SPA fallback.
            stream = assembly.GetManifestResourceStream(ResourcePrefix + "index.html");
            relative = "index.html";
        }

        if (stream is null)
        {
            return NotFound();
        }

        var ext = Path.GetExtension(relative);
        var contentType = ContentTypes.TryGetValue(ext, out var ct) ? ct : "application/octet-stream";
        var isIndex = relative == "index.html";
        Response.Headers.CacheControl = isIndex ? "no-store" : "public, max-age=604800, immutable";
        return File(stream, contentType);
    }
}
