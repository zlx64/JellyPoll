namespace Jellyfin.Plugin.JellyPoll.Voting;

/// <summary>A suggestion eligible for scoring (doc 03 §1).</summary>
public sealed record SuggestionRecord(
    string Id,
    string ItemId,
    bool ItemMissing,
    DateTimeOffset SuggestedAt);

/// <summary>A user's ranked ballot: index 0 = position 1 = most wanted (doc 03 §1).</summary>
public sealed record BallotRecord(
    string UserId,
    IReadOnlyList<string> OrderedSuggestionIds);

/// <summary>Consistent snapshot input to <see cref="BordaCalculator"/> (doc 03 §1).</summary>
public sealed record VotingInput(
    IReadOnlyList<SuggestionRecord> Suggestions,
    IReadOnlyList<BallotRecord> Ballots);

/// <summary>One scored row of the standings (doc 03 §1).</summary>
public sealed record StandingEntry(
    int Rank,
    string SuggestionId,
    long Points,
    int FirstPlaceCount,
    int VoterCount,
    bool ItemMissing);

/// <summary>Final standings: dense ranks, no shared ranks (doc 03 §3).</summary>
public sealed record Standings(IReadOnlyList<StandingEntry> Entries);
