using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyPoll.Api;

/// <summary>
/// Serves the embedded SPA at /JellyPoll/Web/** (doc 04 §4.12, doc 05 §9).
/// Anonymous by design: assets contain no data; the SPA authenticates API calls itself.
/// </summary>
/// <remarks>
/// Route is scoped to /JellyPoll/Web only. Third-party plugins (FileTransformation-based,
/// e.g. Jellyfin Security) inject relative scripts like "../TwoFactorAuth/inject" into served
/// HTML; under our subpath those resolve to /JellyPoll/TwoFactorAuth/... which 404s — harmless
/// console noise only (the injected code is a no-op for our page). A /JellyPoll-level catch-all
/// was tried (1.0.12.2) and REVERTED after the live server failed to finish startup with it.
/// </remarks>
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

        var relative = string.IsNullOrWhiteSpace(path) ? "index.html" : path!.TrimStart('/');

        // Reject path traversal.
        if (relative.Contains("..") || relative.Contains('\\'))
        {
            return BadRequest();
        }

        var resourceName = ResourcePrefix + relative.Replace('/', '.');
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            // Unknown asset under /JellyPoll/Web/** — serve an empty script so stray
            // third-party injections under our subpath are harmless no-ops.
            Response.Headers.CacheControl = "no-store";
            return File(Array.Empty<byte>(), "text/javascript");
        }

        var assetExt = Path.GetExtension(relative);
        var contentType = ContentTypes.TryGetValue(assetExt, out var ct) ? ct : "application/octet-stream";
        var isIndex = relative == "index.html";
        Response.Headers.CacheControl = isIndex ? "no-store" : "public, max-age=604800, immutable";
        return File(stream, contentType);
    }
}
