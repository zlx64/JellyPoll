namespace Jellyfin.Plugin.JellyPoll.Data;

/// <summary>Poll lifecycle (doc 02 §2).</summary>
public enum PollStatus
{
    Open = 0,
    Closed = 1
}

/// <summary>A poll row (doc 02 §3).</summary>
public sealed class PollRow
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public PollStatus Status { get; set; }
    public Guid CreatedBy { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public Guid? ClosedBy { get; set; }
    public string? ClosedAt { get; set; }
    public bool AllowEpisodes { get; set; }
    public bool AllowSeries { get; set; }
    public int StateVersion { get; set; }
}

/// <summary>A suggested library item (doc 02 §3).</summary>
public sealed class SuggestionRow
{
    public Guid Id { get; set; }
    public Guid PollId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemType { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int? ItemYear { get; set; }
    public Guid SuggestedBy { get; set; }
    public string SuggestedAt { get; set; } = string.Empty;
}

/// <summary>A user's ballot header (doc 02 §3).</summary>
public sealed class BallotRow
{
    public Guid Id { get; set; }
    public Guid PollId { get; set; }
    public Guid UserId { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
}

/// <summary>Ordered ballot ids for one user (scoring input shape).</summary>
public sealed record BallotSnapshot(Guid UserId, IReadOnlyList<Guid> OrderedSuggestionIds);

/// <summary>Consistent snapshot of a poll for scoring.</summary>
public sealed record PollSnapshot(IReadOnlyList<SuggestionRow> Suggestions, IReadOnlyList<BallotSnapshot> Ballots);
