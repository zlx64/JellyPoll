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
    private readonly IPollRepository _repo;
    private readonly ILibraryAccessValidator _library;
    private readonly IUserNameResolver _userNames;
    private readonly IConfigurationAccessor _config;
    private readonly ILogger<PollService> _logger;

    public PollService(
        IPollRepository repo,
        ILibraryAccessValidator library,
        IUserNameResolver userNames,
        IConfigurationAccessor config,
        ILogger<PollService> logger)
    {
        _repo = repo;
        _library = library;
        _userNames = userNames;
        _config = config;
        _logger = logger;
    }

    public bool IsAdmin(User user) => user.HasPermission(Jellyfin.Database.Implementations.Enums.PermissionKind.IsAdministrator);

    public bool CanManage(Data.PollRow poll, User user)
        => poll.CreatedBy == user.Id || IsAdmin(user);

    private string DisplayName(Guid userId)
        => _userNames.GetName(userId);

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

    /// <summary>Admin-only bulk cleanup of closed polls. Returns the number deleted.</summary>
    public int DeleteAllClosedPolls(User user)
    {
        if (!IsAdmin(user))
        {
            throw new AccessDeniedException();
        }

        var count = _repo.DeleteClosedPolls();
        _logger.LogInformation("Deleted {Count} closed polls (by {User})", count, user.Username);
        return count;
    }

    // ---------- suggestions ----------

    /// <summary>
    /// Outcome of Suggest(): either a single suggestion, or a collection expansion.
    /// </summary>
    public sealed record SuggestOutcome(
        Data.SuggestionRow? Single,
        IReadOnlyList<Data.SuggestionRow> CollectionAdded,
        int CollectionSkippedExisting,
        int CollectionSkippedOverLimit,
        string? CollectionName);

    /// <summary>
    /// Adds an item to a poll's suggestions. Movie/Episode/Series are added as a single
    /// suggestion; a Collection (BoxSet) is expanded — every movie inside it that is not
    /// already in the poll is added as the suggester's suggestion.
    /// </summary>
    public SuggestOutcome Suggest(User user, Guid pollId, Guid itemId)
    {
        var item = _library.ResolveItem(itemId) ?? throw new SuggestionNotFoundException();
        if (_library.GetTypeName(item) == "Collection")
        {
            var (added, skippedExisting, skippedOverLimit) = AddCollectionSuggestions(user, pollId, itemId, item.Name);
            return new SuggestOutcome(null, added, skippedExisting, skippedOverLimit, item.Name);
        }

        return new SuggestOutcome(AddSuggestion(user, pollId, itemId), Array.Empty<Data.SuggestionRow>(), 0, 0, null);
    }

    /// <summary>Expands a collection into per-movie suggestions for the user.</summary>
    private (List<Data.SuggestionRow> Added, int SkippedExisting, int SkippedOverLimit) AddCollectionSuggestions(
        User user, Guid pollId, Guid itemId, string? collectionName)
    {
        var poll = GetPoll(pollId);
        if (poll.Status != PollStatus.Open)
        {
            throw new PollClosedException();
        }

        if (!_library.CanAccess(user, itemId))
        {
            throw new AccessDeniedException();
        }

        var movies = _library.GetCollectionMovies(user, itemId);
        if (movies.Count == 0)
        {
            throw new ValidationException("This collection contains no movies you can access.");
        }

        var existing = _repo.ListSuggestions(pollId).Select(s => s.ItemId).ToHashSet();
        var max = _config.Current.MaxSuggestionsPerUser;
        var used = max > 0 ? _repo.CountSuggestionsByUser(pollId, user.Id) : 0;

        var added = new List<Data.SuggestionRow>();
        var skippedExisting = 0;
        foreach (var movie in movies)
        {
            if (existing.Contains(movie.Id))
            {
                skippedExisting++;
                continue;
            }

            if (max > 0 && used >= max)
            {
                break;
            }

            var row = new Data.SuggestionRow
            {
                Id = Guid.NewGuid(),
                PollId = pollId,
                ItemId = movie.Id,
                ItemType = "Movie",
                ItemName = movie.Name ?? "Unknown",
                ItemYear = movie.ProductionYear,
                SuggestedBy = user.Id
            };
            _repo.AddSuggestion(row);
            added.Add(row);
            existing.Add(movie.Id);
            used++;
        }

        if (added.Count == 0)
        {
            if (max > 0 && used >= max && movies.Any(m => !existing.Contains(m.Id)))
            {
                throw new SuggestionLimitReachedException(max);
            }

            throw new DuplicateSuggestionException();
        }

        _logger.LogInformation(
            "Collection '{Collection}' suggested into poll {Poll} by {User}: {Added} movies added, {Existing} already present, {OverLimit} over limit",
            collectionName ?? itemId.ToString(), pollId, user.Username, added.Count, skippedExisting,
            movies.Count - skippedExisting - added.Count);
        return (added, skippedExisting, movies.Count - skippedExisting - added.Count);
    }

    public Data.SuggestionRow AddSuggestion(User user, Guid pollId, Guid itemId)
    {
        var poll = GetPoll(pollId);
        if (poll.Status != PollStatus.Open)
        {
            throw new PollClosedException();
        }

        var item = _library.ResolveItem(itemId) ?? throw new SuggestionNotFoundException();

        var typeName = _library.GetTypeName(item)
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

        var max = _config.Current.MaxSuggestionsPerUser;
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

    /// <summary>Collections the user can see, optionally filtered by name, with accessible-movie counts.</summary>
    public IReadOnlyList<Api.CollectionInfoDto> ListCollections(User user, string? searchTerm)
    {
        var items = _library.ListCollections(user)
            .Where(b => string.IsNullOrWhiteSpace(searchTerm)
                || (b.Name ?? string.Empty).Contains(searchTerm.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(b => new Api.CollectionInfoDto(
                b.Id.ToString(),
                b.Name ?? "Unknown",
                b.ProductionYear,
                _library.GetCollectionMovies(user, b.Id).Count))
            .ToList();
        return items;
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

    // ---------- controller-facing projections ----------

    public string UserName(Guid userId) => DisplayName(userId);

    public int StateVersion(Guid pollId) => _repo.GetStateVersion(pollId);

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
        var includeLive = _config.Current.ShowLiveStandings;
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

    // ---------- user preferences + ranking breakdown ----------

    public bool GetShareRanking(Guid userId) => _repo.GetShareRanking(userId);

    public void SetShareRanking(Guid userId, bool value) => _repo.SetShareRanking(userId, value);

    /// <summary>
    /// Per-suggestion voter breakdown for the standings tooltip. Mutual opt-in:
    /// returns empty when the viewer has sharing off, and only lists voters who
    /// have sharing on. Place/points mirror the Borda score (N - place).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Api.VoterBreakdownDto>> GetRankingBreakdown(User viewer, Guid pollId)
    {
        GetPoll(pollId); // throws PollNotFoundException

        if (!_repo.GetShareRanking(viewer.Id))
        {
            return new Dictionary<string, IReadOnlyList<Api.VoterBreakdownDto>>();
        }

        var snapshot = _repo.GetSnapshot(pollId);
        var eligible = snapshot.Suggestions.Where(s => _library.ResolveItem(s.ItemId) is not null).ToList();
        var n = eligible.Count;
        var eligibleIds = eligible.Select(s => s.Id).ToHashSet();
        var sharing = _repo.GetShareRankingUserIds();

        var breakdown = new Dictionary<string, List<Api.VoterBreakdownDto>>();
        foreach (var ballot in snapshot.Ballots)
        {
            if (!sharing.Contains(ballot.UserId))
            {
                continue;
            }

            var name = _userNames.GetName(ballot.UserId);
            var place = 0;
            var seen = new HashSet<Guid>();
            foreach (var sid in ballot.OrderedSuggestionIds)
            {
                if (!eligibleIds.Contains(sid) || !seen.Add(sid))
                {
                    continue;
                }

                place++;
                var key = sid.ToString();
                if (!breakdown.TryGetValue(key, out var list))
                {
                    breakdown[key] = list = new List<Api.VoterBreakdownDto>();
                }

                list.Add(new Api.VoterBreakdownDto(name, place, (long)(n - place)));
            }
        }

        var result = new Dictionary<string, IReadOnlyList<Api.VoterBreakdownDto>>(breakdown.Count);
        foreach (var kv in breakdown)
        {
            result[kv.Key] = kv.Value.OrderBy(v => v.Position).ToList();
        }

        return result;
    }
}
