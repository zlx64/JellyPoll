using Jellyfin.Plugin.JellyPoll.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.JellyPoll.Nav;

/// <summary>
/// Injects a small script tag into Jellyfin's served /web/index.html so the
/// injected client script can add a "Jelly Polls" entry to the web UI's
/// avatar menu for ALL users. Purely a response transform — the webroot on
/// disk is never modified (proven pattern from the JellyfinSecurity plugin's
/// IndexHtmlInjectionMiddleware, verified live on Jellyfin 12.1).
/// </summary>
public sealed class NavInjectionMiddleware
{
    private const string InjectionMarker = "<!-- jellypoll-nav-inject -->";

    // Cache-bust token: assembly version + per-process random suffix so every
    // restart/upgrade is a brand-new URL (fresh cache key everywhere —
    // browser, webview, reverse proxy). Extension-less path keeps CDN "*.js"
    // rules from edge-caching it.
    private static readonly string CacheBust =
        (typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0")
        + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);

    private readonly RequestDelegate _next;
    private readonly IConfigurationAccessor _config;
    private readonly ILogger<NavInjectionMiddleware> _logger;

    // Patched index bytes cached by fingerprint of the upstream response —
    // the server's index.html is stable across the plugin's lifetime, so
    // this skips the decode/search/re-encode work on every page load.
    private static byte[]? _cachedFingerprint;
    private static byte[]? _cachedPatched;
    private static readonly object CacheLock = new();

    public NavInjectionMiddleware(RequestDelegate next, IConfigurationAccessor config, ILogger<NavInjectionMiddleware> logger)
    {
        _next = next;
        _config = config;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsIndexHtmlRequest(context) || !_config.Current.ShowMainNavEntry)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Identity-encoded bytes only: we need to inject text into the body.
        context.Request.Headers.Remove("Accept-Encoding");

        // The patched response has different bytes than the unmodified file,
        // so stale browser validators must not short-circuit us.
        context.Request.Headers.Remove("If-None-Match");
        context.Request.Headers.Remove("If-Modified-Since");

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch
        {
            context.Response.Body = originalBody;
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
            throw;
        }

        context.Response.Body = originalBody;
        buffer.Position = 0;

        var contentType = context.Response.ContentType ?? string.Empty;
        if (!contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase)
            || context.Response.StatusCode != StatusCodes.Status200OK)
        {
            await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
            return;
        }

        var contentEncoding = context.Response.Headers.ContentEncoding.ToString();
        if (!string.IsNullOrEmpty(contentEncoding) && !contentEncoding.Equals("identity", StringComparison.OrdinalIgnoreCase))
        {
            await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
            return;
        }

        try
        {
            var upstream = buffer.ToArray();
            var fingerprint = System.Security.Cryptography.SHA256.HashData(upstream);
            byte[]? cachedPatched;
            lock (CacheLock)
            {
                cachedPatched = _cachedFingerprint is not null
                    && _cachedPatched is not null
                    && _cachedFingerprint.AsSpan().SequenceEqual(fingerprint)
                    ? _cachedPatched
                    : null;
            }

            if (cachedPatched is not null)
            {
                SetPatchedCacheHeaders(context.Response);
                context.Response.ContentLength = cachedPatched.Length;
                await originalBody.WriteAsync(cachedPatched).ConfigureAwait(false);
                return;
            }

            var html = Encoding.UTF8.GetString(upstream);
            if (html.Contains(InjectionMarker, StringComparison.Ordinal))
            {
                // Already patched upstream (another middleware/theme) — pass through.
                await originalBody.WriteAsync(upstream).ConfigureAwait(false);
                return;
            }

            // Inject as the FIRST child of <head> so the script runs before
            // Jellyfin's own bundles initialize (matches the proven 2FA pattern;
            // also means the entry can exist before the SPA renders its shell).
            var headOpen = Regex.Match(html, @"<head\b[^>]*>", RegexOptions.IgnoreCase);
            int insertAt;
            if (headOpen.Success)
            {
                insertAt = headOpen.Index + headOpen.Length;
            }
            else
            {
                var bodyOpen = Regex.Match(html, @"<body\b[^>]*>", RegexOptions.IgnoreCase);
                if (bodyOpen.Success)
                {
                    insertAt = bodyOpen.Index + bodyOpen.Length;
                }
                else
                {
                    var bodyCloseIndex = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                    if (bodyCloseIndex < 0)
                    {
                        await originalBody.WriteAsync(upstream).ConfigureAwait(false);
                        return;
                    }

                    insertAt = bodyCloseIndex;
                }
            }

            var patched = html.Insert(insertAt, InjectionMarker + BuildScriptTag(context));
            var patchedBytes = Encoding.UTF8.GetBytes(patched);

            lock (CacheLock)
            {
                _cachedFingerprint = fingerprint;
                _cachedPatched = patchedBytes;
            }

            SetPatchedCacheHeaders(context.Response);
            context.Response.ContentLength = patchedBytes.Length;
            await originalBody.WriteAsync(patchedBytes).ConfigureAwait(false);
            _logger.LogDebug("[JellyPoll] nav-inject script added to {Path}", context.Request.Path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyPoll] failed to inject nav script into index.html; serving original");
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
        }
    }

    private static void SetPatchedCacheHeaders(HttpResponse response)
    {
        response.Headers.Remove("ETag");
        response.Headers.Remove("Last-Modified");
        response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        response.Headers.Pragma = "no-cache";
        response.Headers.Expires = "0";
    }

    /// <summary>
    /// Absolute script URL rooted at the request's base path (reverse-proxy
    /// deployments keep the prefix in PathBase). Absolute so the tag resolves
    /// correctly no matter which document it lands in.
    /// </summary>
    private static string BuildScriptTag(HttpContext context)
    {
        var basePath = context.Request.PathBase.Value ?? string.Empty;
        return "<script src=\"" + basePath + "/JellyPoll/nav-inject?v=" + CacheBust + "\"></script>";
    }

    private static bool IsIndexHtmlRequest(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            return false;
        }

        var path = context.Request.Path.Value;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        // Never touch our own plugin routes (e.g. the SPA at /JellyPoll/Web).
        if (path.Contains("/JellyPoll/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalized = path.Length > 1 ? path.TrimEnd('/') : path;
        return normalized.Equals("/web", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("/web/index.html", StringComparison.OrdinalIgnoreCase)
            // Server BaseUrl deployments keep the prefix in Request.Path
            // (e.g. /jellyfin/web/index.html) — match the stable web suffix.
            // Case-sensitive: our SPA lives at /JellyPoll/Web and must not match.
            || normalized.EndsWith("/web", StringComparison.Ordinal)
            || normalized.EndsWith("/web/index.html", StringComparison.Ordinal);
    }
}
