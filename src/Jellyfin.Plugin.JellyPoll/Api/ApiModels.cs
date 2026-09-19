namespace Jellyfin.Plugin.JellyPoll.Api;

/// <summary>Error body for all non-2xx responses (doc 04 §2).</summary>
public sealed record ApiError(string Error, string Message);

/// <summary>Poll list row (doc 04 §3).</summary>
public sealed record PollSummaryDto(
    string Id,
    string Title,
    string Status,
    int SuggestionCount,
    int VoterCount,
    string CreatedAt,
    string CreatedById,
    string CreatedByName,
    string? ClosedAt,
    int MyBallotCount);

/// <summary>Suggestion card (doc 04 §3).</summary>
public sealed record SuggestionDto(
    string Id,
    string ItemId,
    string ItemType,
    string Name,
    int? Year,
    string SuggestedById,
    string SuggestedByName,
    string SuggestedAt,
    bool ItemMissing);

/// <summary>Standings row (doc 04 §3).</summary>
public sealed record StandingEntryDto(
    int Rank,
    string SuggestionId,
    long Points,
    int FirstPlaceCount,
    int VoterCount,
    bool ItemMissing,
    string ItemId,
    string ItemType,
    string Name,
    int? Year);

/// <summary>Full poll detail (doc 04 §3).</summary>
public sealed record PollDetailDto(
    PollMetaDto Poll,
    IReadOnlyList<SuggestionDto> Suggestions,
    IReadOnlyList<string> MyBallot,
    IReadOnlyList<StandingEntryDto> Standings,
    bool IsCreator,
    bool IsAdmin);

public sealed record PollMetaDto(
    string Id,
    string Title,
    string Status,
    string CreatedAt,
    string? ClosedAt,
    string CreatedById,
    string CreatedByName,
    bool AllowEpisodes,
    bool AllowSeries,
    int StateVersion);

/// <summary>Results endpoint payload (doc 04 §4.10).</summary>
public sealed record ResultsDto(
    IReadOnlyList<StandingEntryDto> Standings,
    StandingEntryDto? Gold,
    StandingEntryDto? Silver,
    StandingEntryDto? Bronze);

public sealed record CreatePollRequest(string Title, bool? AllowEpisodes, bool? AllowSeries);

/// <summary>Collection (BoxSet) browse row for the suggest picker.</summary>
public sealed record CollectionInfoDto(string Id, string Name, int? Year, int MovieCount);

public sealed record AddSuggestionRequest(string ItemId);

public sealed record SaveBallotRequest(IReadOnlyList<string> SuggestionIds);

public sealed record MenuLinkRequest(bool Install);
