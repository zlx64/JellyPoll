using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Jellyfin.Plugin.JellyPoll.Data;
using Jellyfin.Plugin.JellyPoll.Library;
using Jellyfin.Plugin.JellyPoll.Voting;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Data;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyPoll.Services;

/// <summary>
/// Orchestration layer: permission checks, item validation, standings assembly (doc 01 §5, doc 04).
/// Throws domain exceptions from Jellyfin.Plugin.JellyPoll.Data which controllers map to HTTP codes.
/// </summary>
public sealed class PollService
{
    private readonly SqlitePollRepository _repo;
    private readonly LibraryAccessValidator _library;
    private readonly IUserManager _userManager;
    private readonly ILogger<PollService> _logger;

    public PollService(
        SqlitePollRepository repo,
        LibraryAccessValidator library,
        IUserManager userManager,
        ILogger<PollService> logger)
    {
        _repo = repo;
        _library = library;
        _userManager = userManager;
        _logger = logger;
    }

    public bool IsAdmin(User user) => user.HasPermission(Jellyfin.Database.Implementations.Enums.PermissionKind.IsAdministrator);

    public bool CanManage(Data.PollRow poll, User user)
        => poll.CreatedBy == user.Id || IsAdmin(user);

    private string DisplayName(Guid userId)
        => _userManager.GetUserById(userId)?.Username ?? "Unknown user";

    // ---------- polls ----------

    public Data.PollRow CreatePoll(User user, string title, bool allowEpisodes, bool allowSeries)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ValidationException("Title is required.");
        }

        title = title.Trim();
        if (title.Length is < 1 or > 100)
        {
            throw new ValidationException("Title must be 1-100 characters.");
        }

        var poll = new Data.PollRow
        {
            Id = Guid.NewGuid(),
            Title = title,
            CreatedBy = user.Id,
            AllowEpisodes = allowEpisodes,
            AllowSeries = allowSeries
        };
        _repo.CreatePoll(poll);
        _logger.LogInformation("Poll '{Title}' created by {User}", title, user.Username);
        return poll;
    }

    public Data.PollRow GetPoll(Guid pollId) => _repo.GetPoll(pollId) ?? throw new PollNotFoundException();

    public void ClosePoll(User user, Guid pollId)
    {
        var poll = GetPoll(pollId);
        if (!CanManage(poll, user))
        {
            throw new AccessDeniedException();
        }

        _repo.ClosePoll(pollId, user.Id);
    }

    public void ReopenPoll(User user, Guid pollId)
    {
        var poll = GetPoll(pollId);
        if (!CanManage(poll, user))
        {
            throw new AccessDeniedException();
        }

        _repo.ReopenPoll(pollId);
    }

    public void DeletePoll(User user, Guid pollId)
    {
        var poll = GetPoll(pollId);
        if (!CanManage(poll, user))
        {
            throw new AccessDeniedException();
        }

        _repo.DeletePoll(pollId);
    }

    // ---------- suggestions ----------

    public Data.SuggestionRow AddSuggestion(User user, Guid pollId, Guid itemId)
    {
        var poll = GetPoll(pollId);
        if (poll.Status != PollStatus.Open)
        {
            throw new PollClosedException();
        }

        var item = _library.ResolveItem(itemId) ?? throw new SuggestionNotFoundException();

        var typeName = LibraryAccessValidator.TypeName(item)
            ?? throw new ValidationException("This item type cannot be suggested.");
        if (typeName == "Episode" && !poll.AllowEpisodes)
        {
            throw new ItemTypeNotAllowedException(typeName);
        }

        if (typeName == "Series" && !poll.AllowSeries)
        {
            throw new ItemTypeNotAllowedException(typeName);
        }

        if (!_library.CanAccess(user, itemId))
        {
            throw new AccessDeniedException();
        }

        var max = Plugin.Instance?.Configuration.MaxSuggestionsPerUser ?? 0;
        if (max > 0 && _repo.CountSuggestionsByUser(pollId, user.Id) >= max)
        {
            throw new SuggestionLimitReachedException(max);
        }

        var suggestion = new Data.SuggestionRow
        {
            Id = Guid.NewGuid(),
            PollId = pollId,
            ItemId = itemId,
            ItemType = typeName,
            ItemName = item.Name ?? "Unknown",
            ItemYear = item.ProductionYear,
            SuggestedBy = user.Id
        };
        _repo.AddSuggestion(suggestion);
        return suggestion;
    }

    public void RemoveSuggestion(User user, Guid pollId, Guid suggestionId)
    {
        var poll = GetPoll(pollId);
        var suggestion = _repo.GetSuggestion(pollId, suggestionId) ?? throw new SuggestionNotFoundException();
        if (poll.Status != PollStatus.Open)
        {
            throw new PollClosedException();
        }

        if (suggestion.SuggestedBy != user.Id && !CanManage(poll, user))
        {
            throw new AccessDeniedException();
        }

        _repo.RemoveSuggestion(pollId, suggestionId);
    }

    // ---------- ballots ----------

    public int SaveBallot(User user, Guid pollId, IReadOnlyList<Guid> orderedSuggestionIds)
    {
        if (orderedSuggestionIds.Distinct().Count() != orderedSuggestionIds.Count)
        {
            throw new ValidationException("Ballot contains duplicate suggestions.");
        }

        _repo.UpsertBallot(pollId, user.Id, orderedSuggestionIds);
        return orderedSuggestionIds.Count;
    }

    // ---------- standings / results ----------

    /// <summary>Scored standings joined with current item metadata (doc 04 §3).</summary>
    public IReadOnlyList<(Voting.StandingEntry Entry, string ItemId, string ItemType, string Name, int? Year)> GetStandings(
        Guid pollId,
        bool includeLive)
    {
        var poll = GetPoll(pollId);
        var snapshot = _repo.GetSnapshot(pollId);

        var input = new VotingInput(
            snapshot.Suggestions.Select(s => new SuggestionRecord(
                s.Id.ToString(), s.ItemId.ToString(), ItemMissing: _library.ResolveItem(s.ItemId) is null, ParseTs(s.SuggestedAt))).ToList(),
            snapshot.Ballots.Select(b => new BallotRecord(
                b.UserId.ToString(), b.OrderedSuggestionIds.Select(g => g.ToString()).ToList())).ToList());

        var scored = BordaCalculator.Compute(input);

        if (!includeLive && poll.Status == PollStatus.Open)
        {
            return Array.Empty<(Voting.StandingEntry, string, string, string, int?)>();
        }

        var result = new List<(Voting.StandingEntry, string, string, string, int?)>(scored.Count);
        foreach (var entry in scored)
        {
            var suggestion = snapshot.Suggestions.First(s => s.Id.ToString() == entry.SuggestionId);
            result.Add((entry, suggestion.ItemId.ToString(), suggestion.ItemType, suggestion.ItemName, suggestion.ItemYear));
        }

        return result;
    }

    private static DateTimeOffset ParseTs(string ts) => DateTimeOffset.ParseExact(
        ts, "yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.AssumeUniversal);

    // ---------- helpers used by controllers ----------

    public string UserName(Guid userId) => DisplayName(userId);

    public int StateVersion(Guid pollId) => _repo.GetStateVersion(pollId);

    // ---------- controller-facing projections ----------

    public IReadOnlyList<(Data.PollRow Poll, int SuggestionCount, int VoterCount, string CreatedByName)> ListAll()
    {
        var polls = _repo.ListPolls();
        var result = new List<(Data.PollRow, int, int, string)>(polls.Count);
        foreach (var poll in polls)
        {
            result.Add((
                poll,
                _repo.GetSuggestionCount(poll.Id),
                _repo.GetVoterCount(poll.Id),
                DisplayName(poll.CreatedBy)));
        }

        return result;
    }

    public IReadOnlyDictionary<Guid, int> BallotCountsForUser(Guid userId)
        => _repo.GetBallotCountsForUser(userId);

    public Api.SuggestionDto ToSuggestionDto(Data.SuggestionRow s)
    {
        var item = _library.ResolveItem(s.ItemId);
        var missing = item is null;
        return new Api.SuggestionDto(
            s.Id.ToString(),
            s.ItemId.ToString(),
            s.ItemType,
            missing ? s.ItemName : (item!.Name ?? s.ItemName),
            missing ? s.ItemYear : item!.ProductionYear,
            s.SuggestedBy.ToString(),
            DisplayName(s.SuggestedBy),
            s.SuggestedAt,
            missing);
    }

    public Api.PollDetailDto BuildDetail(User user, Guid pollId)
    {
        var poll = GetPoll(pollId);
        var includeLive = Plugin.Instance?.Configuration.ShowLiveStandings ?? true;
        var suggestions = _repo.ListSuggestions(pollId).Select(ToSuggestionDto).ToList();
        var standings = GetStandings(pollId, includeLive)
            .Select(e => new Api.StandingEntryDto(
                e.Entry.Rank, e.Entry.SuggestionId, e.Entry.Points, e.Entry.FirstPlaceCount,
                e.Entry.VoterCount, e.Entry.ItemMissing, e.ItemId, e.ItemType, e.Name, e.Year))
            .ToList();
        return new Api.PollDetailDto(
            new Api.PollMetaDto(
                poll.Id.ToString(), poll.Title,
                poll.Status == PollStatus.Open ? "open" : "closed",
                poll.CreatedAt, poll.ClosedAt,
                poll.CreatedBy.ToString(), DisplayName(poll.CreatedBy),
                poll.AllowEpisodes, poll.AllowSeries, poll.StateVersion),
            suggestions,
            _repo.GetBallot(pollId, user.Id).Select(g => g.ToString()).ToList(),
            standings,
            IsCreator: poll.CreatedBy == user.Id,
            IsAdmin: IsAdmin(user));
    }

    public Api.ResultsDto GetResults(Guid pollId)
    {
        var standings = GetStandings(pollId, includeLive: true)
            .Select(e => new Api.StandingEntryDto(
                e.Entry.Rank, e.Entry.SuggestionId, e.Entry.Points, e.Entry.FirstPlaceCount,
                e.Entry.VoterCount, e.Entry.ItemMissing, e.ItemId, e.ItemType, e.Name, e.Year))
            .ToList();
        Api.StandingEntryDto? Medal(int rank) => standings.FirstOrDefault(s => s.Rank == rank && !s.ItemMissing);
        return new Api.ResultsDto(standings, Medal(1), Medal(2), Medal(3));
    }
}
