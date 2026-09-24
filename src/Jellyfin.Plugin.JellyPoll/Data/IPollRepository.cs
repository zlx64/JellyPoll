using Jellyfin.Plugin.JellyPoll.Data;

namespace Jellyfin.Plugin.JellyPoll.Data;

/// <summary>Persistence contract consumed by PollService (extracted for M4 testability).</summary>
public interface IPollRepository
{
    void CreatePoll(PollRow poll);

    PollRow? GetPoll(Guid pollId);

    IReadOnlyList<PollRow> ListPolls();

    void ClosePoll(Guid pollId, Guid closedBy);

    void ReopenPoll(Guid pollId);

    void DeletePoll(Guid pollId);

    /// <summary>Deletes all closed polls (cascades their children). Returns the number deleted.</summary>
    int DeleteClosedPolls();

    /// <exception cref="DuplicateSuggestionException">UNIQUE(poll_id, item_id) violated.</exception>
    void AddSuggestion(SuggestionRow suggestion);

    SuggestionRow? GetSuggestion(Guid pollId, Guid suggestionId);

    IReadOnlyList<SuggestionRow> ListSuggestions(Guid pollId);

    int CountSuggestionsByUser(Guid pollId, Guid userId);

    void RemoveSuggestion(Guid pollId, Guid suggestionId);

    void UpsertBallot(Guid pollId, Guid userId, IReadOnlyList<Guid> orderedSuggestionIds);

    IReadOnlyList<Guid> GetBallot(Guid pollId, Guid userId);

    int GetBallotCountForUser(Guid pollId, Guid userId);

    IReadOnlyDictionary<Guid, int> GetBallotCountsForUser(Guid userId);

    int GetVoterCount(Guid pollId);

    int GetSuggestionCount(Guid pollId);

    PollSnapshot GetSnapshot(Guid pollId);

    int GetStateVersion(Guid pollId);

    int RemoveSuggestions(IReadOnlyList<Guid> suggestionIds);

    // ---------- user preferences ----------

    /// <summary>True when the user has opted in to sharing their ranking (default false).</summary>
    bool GetShareRanking(Guid userId);

    void SetShareRanking(Guid userId, bool value);

    /// <summary>All user ids that have opted in to sharing their ranking.</summary>
    IReadOnlySet<Guid> GetShareRankingUserIds();

    // ---------- thumbs down (social signal, never scored) ----------

    /// <summary>Records the user's "I don't want to watch this" on a suggestion.</summary>
    void AddThumbsDown(Guid suggestionId, Guid userId);

    /// <summary>Removes the user's thumbs-down from a suggestion.</summary>
    void RemoveThumbsDown(Guid suggestionId, Guid userId);

    /// <summary>Per-suggestion thumbs-down user ids for a poll (suggestionId -> userIds).</summary>
    IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> GetThumbsDowns(Guid pollId);
}
