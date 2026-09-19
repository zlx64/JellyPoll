using Jellyfin.Plugin.JellyPoll.Data;
using Jellyfin.Plugin.JellyPoll.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyPoll.Api;

/// <summary>REST endpoints for polls (doc 04 §4).</summary>
[Route("JellyPoll")]
public sealed class PollsController : JellyPollControllerBase
{
    private readonly PollService _polls;
    private readonly Menu.MenuLinkInstaller _menuLinkInstaller;

    public PollsController(
        IAuthorizationContext authorizationContext,
        PollService polls,
        Menu.MenuLinkInstaller menuLinkInstaller)
        : base(authorizationContext)
    {
        _polls = polls;
        _menuLinkInstaller = menuLinkInstaller;
    }

    // ---------- 4.1 list ----------

    [HttpGet("Polls")]
    public async Task<ActionResult<object>> ListPolls()
    {
        var user = await ResolveUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return UnauthorizedError();
        }

        var rows = _polls.ListAll();
        var counts = _polls.BallotCountsForUser(user.Id);
        var list = rows.Select(p => new PollSummaryDto(
            p.Poll.Id.ToString(),
            p.Poll.Title,
            p.Poll.Status == PollStatus.Open ? "open" : "closed",
            p.SuggestionCount,
            p.VoterCount,
            p.Poll.CreatedAt,
            p.Poll.CreatedBy.ToString(),
            p.CreatedByName,
            p.Poll.ClosedAt,
            counts.TryGetValue(p.Poll.Id, out var c) ? c : 0)).ToList();
        return Ok(new { Polls = list, IsAdmin = _polls.IsAdmin(user) });
    }

    // ---------- 4.2 create ----------

    [HttpPost("Polls")]
    public async Task<ActionResult<object>> CreatePoll([FromBody] CreatePollRequest? request)
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

        try
        {
            var config = Plugin.Instance?.Configuration;
            var poll = _polls.CreatePoll(
                user,
                request.Title,
                request.AllowEpisodes ?? config?.DefaultAllowEpisodes ?? true,
                request.AllowSeries ?? config?.DefaultAllowSeries ?? true);
            return CreatedAtAction(nameof(GetPoll), new { id = poll.Id.ToString() }, BuildDetail(user, poll.Id));
        }
        catch (ValidationException ex)
        {
            return Error(400, "validation", ex.Message);
        }
    }

    // ---------- 4.3 detail ----------

    [HttpGet("Polls/{id:guid}")]
    public async Task<ActionResult<object>> GetPoll(Guid id)
    {
        var user = await ResolveUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return UnauthorizedError();
        }

        try
        {
            return Ok(BuildDetail(user, id));
        }
        catch (PollNotFoundException)
        {
            return NotFoundError();
        }
    }

    // ---------- 4.4 add suggestion ----------

    [HttpPost("Polls/{id:guid}/Suggestions")]
    public async Task<ActionResult<object>> AddSuggestion(Guid id, [FromBody] AddSuggestionRequest? request)
    {
        var user = await ResolveUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return UnauthorizedError();
        }

        if (request is null || !Guid.TryParse(request.ItemId, out var itemId))
        {
            return Error(400, "validation", "itemId (GUID) is required.");
        }

        try
        {
            var outcome = _polls.Suggest(user, id, itemId);
            if (outcome.Single is not null)
            {
                return StatusCode(201, new { Suggestion = _polls.ToSuggestionDto(outcome.Single) });
            }

            var dtos = outcome.CollectionAdded.Select(_polls.ToSuggestionDto).ToList();
            return StatusCode(201, new
            {
                Suggestions = dtos,
                SavedCount = dtos.Count,
                outcome.CollectionSkippedExisting,
                outcome.CollectionSkippedOverLimit,
                outcome.CollectionName
            });
        }
        catch (PollNotFoundException) { return NotFoundError(); }
        catch (PollClosedException) { return Error(409, "poll_closed", "Poll is closed."); }
        catch (SuggestionNotFoundException) { return NotFoundError(); }
        catch (ItemTypeNotAllowedException ex) { return Error(409, "item_type_not_allowed", ex.Message); }
        catch (AccessDeniedException) { return Error(403, "item_forbidden", "You do not have access to this item."); }
        catch (SuggestionLimitReachedException ex) { return Error(409, "suggestion_limit_reached", ex.Message); }
        catch (DuplicateSuggestionException) { return Error(409, "duplicate_suggestion", "Already suggested."); }
        catch (ValidationException ex) { return Error(400, "validation", ex.Message); }
    }

    // ---------- 4.4a browse collections ----------

    /// <summary>
    /// Collections (BoxSets) the user can see — Jellyfin 12's /Items searchTerm and
    /// /Search/Hints both exclude BoxSets, so the picker browses them through us.
    /// </summary>
    [HttpGet("Collections")]
    public async Task<ActionResult<object>> ListCollections([FromQuery] string? searchTerm)
    {
        var user = await ResolveUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return UnauthorizedError();
        }

        return Ok(new { Items = _polls.ListCollections(user, searchTerm) });
    }

    // ---------- 4.5 remove suggestion ----------

    [HttpDelete("Polls/{id:guid}/Suggestions/{suggestionId:guid}")]
    public async Task<ActionResult> RemoveSuggestion(Guid id, Guid suggestionId)
    {
        var user = await ResolveUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return UnauthorizedError();
        }

        try
        {
            _polls.RemoveSuggestion(user, id, suggestionId);
            return NoContent();
        }
        catch (PollNotFoundException) { return NotFoundError(); }
        catch (SuggestionNotFoundException) { return NotFoundError(); }
        catch (PollClosedException) { return Error(409, "poll_closed", "Poll is closed."); }
        catch (AccessDeniedException) { return ForbiddenError(); }
    }

    // ---------- 4.6 save ballot ----------

    [HttpPut("Polls/{id:guid}/Ballot")]
    public async Task<ActionResult<object>> SaveBallot(Guid id, [FromBody] SaveBallotRequest? request)
    {
        var user = await ResolveUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return UnauthorizedError();
        }

        if (request is null || request.SuggestionIds is null)
        {
            return Error(400, "validation", "suggestionIds array is required.");
        }

        var ids = new List<Guid>();
        foreach (var raw in request.SuggestionIds)
        {
            if (!Guid.TryParse(raw, out var g))
            {
                return Error(400, "validation", $"Invalid suggestion id: {raw}");
            }

            ids.Add(g);
        }

        try
        {
            var saved = _polls.SaveBallot(user, id, ids);
            return Ok(new { StateVersion = _polls.StateVersion(id), SavedCount = saved });
        }
        catch (PollNotFoundException) { return NotFoundError(); }
        catch (PollClosedException) { return Error(409, "poll_closed", "Poll is closed."); }
        catch (ValidationException ex) { return Error(400, "validation", ex.Message); }
    }

    // ---------- 4.7 close ----------

    [HttpPost("Polls/{id:guid}/Close")]
    public async Task<ActionResult<object>> ClosePoll(Guid id)
    {
        var user = await ResolveUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return UnauthorizedError();
        }

        try
        {
            _polls.ClosePoll(user, id);
            return Ok(BuildDetail(user, id));
        }
        catch (PollNotFoundException) { return NotFoundError(); }
        catch (AccessDeniedException) { return ForbiddenError(); }
    }

    // ---------- 4.8 reopen ----------

    [HttpPost("Polls/{id:guid}/Reopen")]
    public async Task<ActionResult<object>> ReopenPoll(Guid id)
    {
        var user = await ResolveUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return UnauthorizedError();
        }

        try
        {
            _polls.ReopenPoll(user, id);
            return Ok(BuildDetail(user, id));
        }
        catch (PollNotFoundException) { return NotFoundError(); }
        catch (AccessDeniedException) { return ForbiddenError(); }
    }

    // ---------- 4.9 delete ----------

    // ---------- 4.9 delete ----------

    [HttpDelete("Polls/{id:guid}")]
    public async Task<ActionResult> DeletePoll(Guid id)
    {
        var user = await ResolveUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return UnauthorizedError();
        }

        try
        {
            _polls.DeletePoll(user, id);
            return NoContent();
        }
        catch (PollNotFoundException) { return NotFoundError(); }
        catch (AccessDeniedException) { return ForbiddenError(); }
    }

    // ---------- admin: delete all closed polls ----------

    [HttpDelete("Polls/Closed")]
    public async Task<ActionResult<object>> DeleteAllClosedPolls()
    {
        var user = await ResolveUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return UnauthorizedError();
        }

        try
        {
            var count = _polls.DeleteAllClosedPolls(user);
            return Ok(new { DeletedCount = count });
        }
        catch (AccessDeniedException)
        {
            return ForbiddenError();
        }
    }

    // ---------- 4.10 results ----------

    [HttpGet("Polls/{id:guid}/Results")]
    public async Task<ActionResult<object>> Results(Guid id)
    {
        var user = await ResolveUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return UnauthorizedError();
        }

        try
        {
            return Ok(_polls.GetResults(id));
        }
        catch (PollNotFoundException)
        {
            return NotFoundError();
        }
    }

    // ---------- 4.11 state poller ----------

    [HttpGet("Polls/{id:guid}/State")]
    public async Task<ActionResult<object>> State(Guid id, [FromQuery] int? v)
    {
        await ResolveUserAsync().ConfigureAwait(false);
        try
        {
            var current = _polls.StateVersion(id);
            return v.HasValue && v.Value == current ? NoContent() : Ok(new { StateVersion = current });
        }
        catch (PollNotFoundException)
        {
            return NotFoundError();
        }
    }

    // ---------- admin: menuLinks installer ----------

    [HttpPost("Admin/MenuLink")]
    public async Task<ActionResult> InstallMenuLink([FromBody] MenuLinkRequest? request)
    {
        var user = await ResolveUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return UnauthorizedError();
        }

        if (!_polls.IsAdmin(user))
        {
            return ForbiddenError();
        }

        try
        {
            var installed = _menuLinkInstaller.SetMenuLink(request?.Install ?? true);
            return Ok(new { Installed = installed, WebConfigPath = _menuLinkInstaller.WebConfigPath });
        }
        catch (UnauthorizedAccessException ex)
        {
            // Read-only webroot (Docker, bind mounts) — degrade gracefully (doc 06 §4 step 5).
            return Ok(new { Installed = false, WebConfigPath = _menuLinkInstaller.WebConfigPath, Error = ex.Message, Manual = true });
        }
        catch (FileNotFoundException ex)
        {
            return Ok(new { Installed = false, WebConfigPath = _menuLinkInstaller.WebConfigPath, Error = ex.Message, Manual = true });
        }
    }

    // ---------- helpers ----------

    private PollDetailDto BuildDetail(Jellyfin.Database.Implementations.Entities.User user, Guid pollId)
        => _polls.BuildDetail(user, pollId);
}
