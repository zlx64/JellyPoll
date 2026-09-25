using Jellyfin.Plugin.JellyPoll.Configuration;
using Jellyfin.Plugin.JellyPoll.Data;
using Jellyfin.Plugin.JellyPoll.Library;
using Jellyfin.Plugin.JellyPoll.Services;
using MediaBrowser.Controller.Entities;
using Jellyfin.Database.Implementations.Entities;

namespace Jellyfin.Plugin.JellyPoll.Tests;

/// <summary>In-memory IPollRepository fake mirroring the real semantics.</summary>
public sealed class FakePollRepository : IPollRepository
{
    public Dictionary<Guid, PollRow> Polls { get; } = new();
    public Dictionary<Guid, SuggestionRow> Suggestions { get; } = new();
    public Dictionary<(Guid PollId, Guid UserId), List<Guid>> Ballots { get; } = new();
    public List<Guid> RemovedSuggestions { get; } = new();

    private static string Now() => DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    private void Bump(Guid pollId) => Polls[pollId].StateVersion++;

    public void CreatePoll(PollRow poll)
    {
        poll.CreatedAt = Now();
        poll.StateVersion = 1;
        Polls[poll.Id] = poll;
    }

    public PollRow? GetPoll(Guid pollId) => Polls.TryGetValue(pollId, out var p) ? p : null;

    public IReadOnlyList<PollRow> ListPolls() => Polls.Values.ToList();

    public void ClosePoll(Guid pollId, Guid closedBy)
    {
        var p = Polls[pollId];
        p.Status = PollStatus.Closed;
        p.ClosedBy = closedBy;
        p.ClosedAt = Now();
        Bump(pollId);
    }

    public void ReopenPoll(Guid pollId)
    {
        var p = Polls[pollId];
        p.Status = PollStatus.Open;
        p.ClosedBy = null;
        p.ClosedAt = null;
        Bump(pollId);
    }

    public void DeletePoll(Guid pollId)
    {
        Polls.Remove(pollId);
        foreach (var k in Suggestions.Where(s => s.Value.PollId == pollId).Select(s => s.Key).ToList())
        {
            Suggestions.Remove(k);
        }

        foreach (var k in Ballots.Keys.Where(k => k.PollId == pollId).ToList())
        {
            Ballots.Remove(k);
        }
    }

    public int DeleteClosedPolls()
    {
        var closed = Polls.Values.Where(p => p.Status == PollStatus.Closed).Select(p => p.Id).ToList();
        foreach (var id in closed)
        {
            DeletePoll(id);
        }

        return closed.Count;
    }

    public void AddSuggestion(SuggestionRow suggestion)
    {
        if (Suggestions.Values.Any(s => s.PollId == suggestion.PollId && s.ItemId == suggestion.ItemId))
        {
            throw new DuplicateSuggestionException();
        }

        if (string.IsNullOrEmpty(suggestion.SuggestedAt))
        {
            suggestion.SuggestedAt = Now(); // mirror the real repository's SQL-side stamp
        }

        Suggestions[suggestion.Id] = suggestion;
        Bump(suggestion.PollId);
    }

    public SuggestionRow? GetSuggestion(Guid pollId, Guid suggestionId) =>
        Suggestions.TryGetValue(suggestionId, out var s) && s.PollId == pollId ? s : null;

    public IReadOnlyList<SuggestionRow> ListSuggestions(Guid pollId) =>
        Suggestions.Values.Where(s => s.PollId == pollId).OrderBy(s => s.SuggestedAt).ToList();

    public int CountSuggestionsByUser(Guid pollId, Guid userId) =>
        Suggestions.Values.Count(s => s.PollId == pollId && s.SuggestedBy == userId);

    public void RemoveSuggestion(Guid pollId, Guid suggestionId)
    {
        if (!Suggestions.Remove(suggestionId))
        {
            throw new SuggestionNotFoundException();
        }

        Bump(pollId);
    }

    public void UpsertBallot(Guid pollId, Guid userId, IReadOnlyList<Guid> orderedSuggestionIds)
    {
        if (!Polls.TryGetValue(pollId, out var poll))
        {
            throw new PollNotFoundException();
        }

        if (poll.Status != PollStatus.Open)
        {
            throw new PollClosedException();
        }

        var own = Suggestions.Values.Where(s => s.PollId == pollId).Select(s => s.Id).ToHashSet();
        if (orderedSuggestionIds.Any(id => !own.Contains(id)))
        {
            throw new ValidationException("foreign suggestion id");
        }

        Ballots[(pollId, userId)] = orderedSuggestionIds.ToList();
        Bump(pollId);
    }

    public IReadOnlyList<Guid> GetBallot(Guid pollId, Guid userId) =>
        Ballots.TryGetValue((pollId, userId), out var b) ? b : Array.Empty<Guid>();

    public int GetBallotCountForUser(Guid pollId, Guid userId) => GetBallot(pollId, userId).Count;

    public IReadOnlyDictionary<Guid, int> GetBallotCountsForUser(Guid userId) =>
        Ballots.Where(kv => kv.Key.UserId == userId && kv.Value.Count > 0)
            .ToDictionary(kv => kv.Key.PollId, kv => kv.Value.Count);

    public int GetVoterCount(Guid pollId) =>
        Ballots.Count(kv => kv.Key.PollId == pollId && kv.Value.Count > 0);

    public int GetSuggestionCount(Guid pollId) =>
        Suggestions.Values.Count(s => s.PollId == pollId);

    public PollSnapshot GetSnapshot(Guid pollId) => new(
        Suggestions.Values.Where(s => s.PollId == pollId).ToList(),
        Ballots.Where(kv => kv.Key.PollId == pollId)
            .Select(kv => new BallotSnapshot(kv.Key.UserId, kv.Value)).ToList());

    public int GetStateVersion(Guid pollId) =>
        Polls.TryGetValue(pollId, out var p) ? p.StateVersion : throw new PollNotFoundException();

    public int RemoveSuggestions(IReadOnlyList<Guid> suggestionIds)
    {
        var removed = 0;
        var polls = new HashSet<Guid>();
        foreach (var sid in suggestionIds)
        {
            if (Suggestions.Remove(sid, out var s))
            {
                polls.Add(s.PollId);
                removed++;
            }
        }

        foreach (var p in polls)
        {
            Bump(p);
        }

        RemovedSuggestions.AddRange(suggestionIds);
        return removed;
    }

    // ---------- user preferences ----------

    public Dictionary<Guid, bool> ShareRanking { get; } = new();

    public bool GetShareRanking(Guid userId) => ShareRanking.TryGetValue(userId, out var v) && v;

    public void SetShareRanking(Guid userId, bool value) => ShareRanking[userId] = value;

    public IReadOnlySet<Guid> GetShareRankingUserIds() =>
        ShareRanking.Where(kv => kv.Value).Select(kv => kv.Key).ToHashSet();

    // ---------- thumbs down (social signal, never scored) ----------

    public Dictionary<Guid, List<Guid>> ThumbsDowns { get; } = new();

    public void AddThumbsDown(Guid suggestionId, Guid userId)
    {
        if (!Suggestions.TryGetValue(suggestionId, out var s))
        {
            throw new SuggestionNotFoundException();
        }

        if (!ThumbsDowns.TryGetValue(suggestionId, out var users))
        {
            ThumbsDowns[suggestionId] = users = new List<Guid>();
        }

        if (!users.Contains(userId))
        {
            users.Add(userId);
            Bump(s.PollId);
        }
    }

    public void RemoveThumbsDown(Guid suggestionId, Guid userId)
    {
        if (!Suggestions.TryGetValue(suggestionId, out var s))
        {
            throw new SuggestionNotFoundException();
        }

        if (ThumbsDowns.TryGetValue(suggestionId, out var users) && users.Remove(userId))
        {
            Bump(s.PollId);
        }
    }

    public IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> GetThumbsDowns(Guid pollId) =>
        Suggestions.Values.Where(s => s.PollId == pollId).OrderBy(s => s.SuggestedAt)
            .Where(s => ThumbsDowns.ContainsKey(s.Id))
            .ToDictionary(s => s.Id, s => (IReadOnlyList<Guid>)ThumbsDowns[s.Id].ToList());
}

/// <summary>ILibraryAccessValidator fake: dictionary-driven item resolution + access.</summary>
public sealed class FakeLibrary : ILibraryAccessValidator
{
    public Dictionary<Guid, BaseItem> Items { get; } = new();
    public HashSet<Guid> AccessibleToAllUsers { get; } = new();
    public Dictionary<Guid, List<Guid>> Collections { get; } = new();
    public List<Guid> CollectionLookups { get; } = new();

    public BaseItem? ResolveItem(Guid itemId) => Items.TryGetValue(itemId, out var i) ? i : null;

    public bool CanAccess(Jellyfin.Database.Implementations.Entities.User user, Guid itemId) =>
        Items.ContainsKey(itemId) && AccessibleToAllUsers.Contains(itemId);

    public string? GetTypeName(BaseItem item) => item switch
    {
        MediaBrowser.Controller.Entities.Movies.Movie => "Movie",
        MediaBrowser.Controller.Entities.TV.Episode => "Episode",
        MediaBrowser.Controller.Entities.TV.Series => "Series",
        MediaBrowser.Controller.Entities.Movies.BoxSet => "Collection",
        _ => null
    };

    public IReadOnlyList<BaseItem> GetCollectionMovies(Jellyfin.Database.Implementations.Entities.User user, Guid collectionId)
    {
        CollectionLookups.Add(collectionId);
        if (!Collections.TryGetValue(collectionId, out var childIds))
        {
            return Array.Empty<BaseItem>();
        }

        return childIds
            .Where(Items.ContainsKey)
            .Where(id => AccessibleToAllUsers.Contains(id))
            .Select(id => Items[id])
            .OrderBy(m => m.SortName ?? m.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<BaseItem> ListCollections(Jellyfin.Database.Implementations.Entities.User user) =>
        Items.Values
            .Where(i => GetTypeName(i) == "Collection" && AccessibleToAllUsers.Contains(i.Id))
            .OrderBy(i => i.SortName ?? i.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
}

/// <summary>Fixed name resolver fake.</summary>
public sealed class FakeUserNames : IUserNameResolver
{
    private readonly Dictionary<Guid, string> _names = new();

    public FakeUserNames(params (Guid id, string name)[] entries)
    {
        foreach (var (id, name) in entries)
        {
            _names[id] = name;
        }
    }

    public string GetName(Guid userId) => _names.TryGetValue(userId, out var n) ? n : "Unknown user";
}

/// <summary>Configuration accessor fake bound to a single instance.</summary>
public sealed class FakeConfigAccessor : IConfigurationAccessor
{
    public PluginConfiguration Current { get; set; } = new();
}
