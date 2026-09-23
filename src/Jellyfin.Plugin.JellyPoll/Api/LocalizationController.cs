using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyPoll.Api;

/// <summary>
/// Serves translation dictionaries and the public (non-secret) plugin settings
/// (docs/localization-plan.md §4.4).
/// </summary>
[Route("JellyPoll")]
public sealed class LocalizationController : JellyPollControllerBase
{
    private static readonly string[] SupportedLanguages = ["en", "uk"];

    public LocalizationController(IAuthorizationContext authorizationContext)
        : base(authorizationContext)
    {
    }

    /// <summary>
    /// Anonymous by design: static dictionary text, no user data.
    /// Unknown or empty lang serves "en".
    /// </summary>
    [HttpGet("Localization")]
    public IActionResult GetDefaultDictionary() => GetDictionary("en");

    [HttpGet("Localization/{lang}")]
    public IActionResult GetDictionary(string lang)
    {
        var normalized = (lang ?? string.Empty).Replace('_', '-').Split('-')[0].ToLowerInvariant();
        if (normalized.Length == 0 || !SupportedLanguages.Contains(normalized))
        {
            normalized = "en";
        }

        var stream = typeof(Plugin).Assembly.GetManifestResourceStream($"Jellyfin.Plugin.JellyPoll.Localization.{normalized}.json");
        if (stream is null)
        {
            return NotFound();
        }

        // Short, revalidating cache: the dictionaries are embedded in the DLL,
        // so a plugin update changes them; "immutable" + a week of max-age would
        // serve stale translations to already-loaded clients after an update.
        Response.Headers.CacheControl = "public, max-age=3600";
        return File(stream, "application/json; charset=utf-8");
    }

    /// <summary>
    /// Authenticated, non-admin OK: the SPA needs the admin's global language
    /// override to run its resolution chain.
    /// </summary>
    [HttpGet("PublicConfig")]
    public async Task<ActionResult<object>> GetPublicConfig()
    {
        var user = await ResolveUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return UnauthorizedError();
        }

        return Ok(new { displayLanguage = Plugin.Instance?.Configuration.DisplayLanguage ?? string.Empty });
    }
}
