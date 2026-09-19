using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyPoll.Api;

/// <summary>
/// Serves the nav-injection client script at /JellyPoll/nav-inject (anonymous:
/// static code, no data). Referenced by the &lt;script&gt; tag that
/// NavInjectionMiddleware injects into Jellyfin's /web/index.html.
/// </summary>
/// <remarks>
/// Deliberately an explicit literal route — a /JellyPoll-level catch-all was
/// tried in 1.0.12.2 and reverted after it crashed the host on startup.
/// </remarks>
[ApiController]
[Route("JellyPoll")]
public sealed class NavScriptController : ControllerBase
{
    private const string ResourceName = "Jellyfin.Plugin.JellyPoll.Nav.nav-inject.js";

    [HttpGet("nav-inject")]
    public IActionResult GetScript()
    {
        var stream = typeof(Plugin).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "no-store";
        return File(stream, "text/javascript; charset=utf-8");
    }
}
