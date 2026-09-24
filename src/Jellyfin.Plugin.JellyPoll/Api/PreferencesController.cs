using Jellyfin.Plugin.JellyPoll.Services;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyPoll.Api;

/// <summary>
/// Per-user preferences (ranking visibility). The "share ranking" flag drives the
/// mutual opt-in for the standings vote breakdown.
/// </summary>
[Route("JellyPoll")]
public sealed class PreferencesController : JellyPollControllerBase
{
    private readonly PollService _polls;

    public PreferencesController(IAuthorizationContext authorizationContext, PollService polls)
        : base(authorizationContext)
    {
        _polls = polls;
    }

    [HttpGet("Preferences")]
    public async Task<ActionResult<object>> Get()
    {
        var user = await ResolveUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return UnauthorizedError();
        }

        return Ok(new { shareRanking = _polls.GetShareRanking(user.Id) });
    }

    [HttpPut("Preferences")]
    public async Task<ActionResult> Set([FromBody] SetPreferencesRequest? request)
    {
        var user = await ResolveUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return UnauthorizedError();
        }

        if (request is null)
        {
            return Error(400, "validation", "Request body required.");
        }

        _polls.SetShareRanking(user.Id, request.ShareRanking);
        return NoContent();
    }
}
