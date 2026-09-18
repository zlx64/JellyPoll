using Jellyfin.Plugin.JellyPoll.Data;
using Xunit;

namespace Jellyfin.Plugin.JellyPoll.Tests;

/// <summary>Repository tests against a temp SQLite file — doc 02 §8.</summary>
public class RepositoryTests : IDisposable
{
    private readonly Db _db;
    private readonly SqlitePollRepository _repo;
    private readonly string _dir;

    public RepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "jellypoll-tests", Guid.NewGuid().ToString("N"));
        _db = new Db(Path.Combine(_dir, "test.db"));
        _repo = new SqlitePollRepository(_db);
        _repo.Initialize();
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static PollRow NewPoll(Guid creator, bool episodes = true, bool series = true) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Test Poll",
        CreatedBy = creator,
        AllowEpisodes = episodes,
        AllowSeries = series
    };

    private static SuggestionRow NewSuggestion(Guid pollId, Guid itemId, Guid by) => new()
    {
        Id = Guid.NewGuid(),
        PollId = pollId,
        ItemId = itemId,
        ItemType = "Movie",
        ItemName = "Movie " + itemId.ToString("N")[..6],
        ItemYear = 2020,
        SuggestedBy = by
    };

    [Fact]
    public void Schema_BootstrapsOnEmptyFile()
    {
        // Initialize already ran in ctor; second run is a no-op.
        _repo.Initialize();
        Assert.Empty(_repo.ListPolls());
    }

    [Fact]
    public void DuplicateSuggestion_ThrowsTyped()
    {
        var poll = NewPoll(Guid.NewGuid());
        _repo.CreatePoll(poll);
        var item = Guid.NewGuid();
        _repo.AddSuggestion(NewSuggestion(poll.Id, item, Guid.NewGuid()));

        Assert.Throws<DuplicateSuggestionException>(
            () => _repo.AddSuggestion(NewSuggestion(poll.Id, item, Guid.NewGuid())));
    }

    [Fact]
    public void DeletePoll_CascadesAll()
    {
        var poll = NewPoll(Guid.NewGuid());
        _repo.CreatePoll(poll);
        var s1 = NewSuggestion(poll.Id, Guid.NewGuid(), Guid.NewGuid());
        var s2 = NewSuggestion(poll.Id, Guid.NewGuid(), Guid.NewGuid());
        _repo.AddSuggestion(s1);
        _repo.AddSuggestion(s2);
        _repo.UpsertBallot(poll.Id, Guid.NewGuid(), new[] { s1.Id, s2.Id });

        _repo.DeletePoll(poll.Id);

        Assert.Null(_repo.GetPoll(poll.Id));
        Assert.Empty(_repo.ListSuggestions(poll.Id));
        Assert.Empty(_repo.GetSnapshot(poll.Id).Ballots);
    }

    [Fact]
    public void StateVersion_BumpsOnEveryMutationKind()
    {
        var creator = Guid.NewGuid();
        var poll = NewPoll(creator);
        _repo.CreatePoll(poll);
        var v0 = _repo.GetStateVersion(poll.Id);

        var s1 = NewSuggestion(poll.Id, Guid.NewGuid(), creator);
        _repo.AddSuggestion(s1);
        var v1 = _repo.GetStateVersion(poll.Id);
        Assert.Equal(v0 + 1, v1);

        var user = Guid.NewGuid();
        _repo.UpsertBallot(poll.Id, user, new[] { s1.Id });
        var v2 = _repo.GetStateVersion(poll.Id);
        Assert.Equal(v1 + 1, v2);

        _repo.ClosePoll(poll.Id, creator);
        var v3 = _repo.GetStateVersion(poll.Id);
        Assert.Equal(v2 + 1, v3);

        _repo.ReopenPoll(poll.Id);
        var v4 = _repo.GetStateVersion(poll.Id);
        Assert.Equal(v3 + 1, v4);

        _repo.RemoveSuggestion(poll.Id, s1.Id);
        var v5 = _repo.GetStateVersion(poll.Id);
        Assert.Equal(v4 + 1, v5);
    }

    [Fact]
    public void BallotUpsert_RenumbersPositionsGapFree()
    {
        var poll = NewPoll(Guid.NewGuid());
        _repo.CreatePoll(poll);
        var s1 = NewSuggestion(poll.Id, Guid.NewGuid(), Guid.NewGuid());
        var s2 = NewSuggestion(poll.Id, Guid.NewGuid(), Guid.NewGuid());
        var s3 = NewSuggestion(poll.Id, Guid.NewGuid(), Guid.NewGuid());
        _repo.AddSuggestion(s1);
        _repo.AddSuggestion(s2);
        _repo.AddSuggestion(s3);

        var user = Guid.NewGuid();
        _repo.UpsertBallot(poll.Id, user, new[] { s3.Id, s1.Id, s2.Id });
        Assert.Equal(new[] { s3.Id, s1.Id, s2.Id }, _repo.GetBallot(poll.Id, user));

        // Replace with a shorter ballot (no gaps).
        _repo.UpsertBallot(poll.Id, user, new[] { s2.Id });
        Assert.Equal(new[] { s2.Id }, _repo.GetBallot(poll.Id, user));

        // Clear ballot.
        _repo.UpsertBallot(poll.Id, user, Array.Empty<Guid>());
        Assert.Empty(_repo.GetBallot(poll.Id, user));
    }

    [Fact]
    public void Ballot_WithForeignSuggestionId_IsRejected()
    {
        var poll1 = NewPoll(Guid.NewGuid());
        var poll2 = NewPoll(Guid.NewGuid());
        _repo.CreatePoll(poll1);
        _repo.CreatePoll(poll2);
        var s1 = NewSuggestion(poll1.Id, Guid.NewGuid(), Guid.NewGuid());
        _repo.AddSuggestion(s1);

        Assert.Throws<ValidationException>(
            () => _repo.UpsertBallot(poll2.Id, Guid.NewGuid(), new[] { s1.Id }));
    }

    [Fact]
    public void Ballot_OnClosedPoll_IsRejected()
    {
        var creator = Guid.NewGuid();
        var poll = NewPoll(creator);
        _repo.CreatePoll(poll);
        var s1 = NewSuggestion(poll.Id, Guid.NewGuid(), creator);
        _repo.AddSuggestion(s1);
        _repo.ClosePoll(poll.Id, creator);

        Assert.Throws<PollClosedException>(
            () => _repo.UpsertBallot(poll.Id, Guid.NewGuid(), new[] { s1.Id }));
    }

    [Fact]
    public void ListPolls_OpenFirst_ThenClosedByDate()
    {
        var creator = Guid.NewGuid();
        var open = NewPoll(creator);
        var closed = NewPoll(creator);
        _repo.CreatePoll(closed);
        _repo.CreatePoll(open);
        _repo.ClosePoll(closed.Id, creator);

        var list = _repo.ListPolls();
        Assert.Equal(2, list.Count);
        Assert.Equal(PollStatus.Open, list[0].Status);
        Assert.Equal(PollStatus.Closed, list[1].Status);
    }

    [Fact]
    public void VoterCount_CountsDistinctUsersWithEntries()
    {
        var creator = Guid.NewGuid();
        var poll = NewPoll(creator);
        _repo.CreatePoll(poll);
        var s1 = NewSuggestion(poll.Id, Guid.NewGuid(), creator);
        _repo.AddSuggestion(s1);

        _repo.UpsertBallot(poll.Id, Guid.NewGuid(), new[] { s1.Id });
        var voter = Guid.NewGuid();
        _repo.UpsertBallot(poll.Id, voter, Array.Empty<Guid>()); // empty ballot = not a voter

        Assert.Equal(1, _repo.GetVoterCount(poll.Id));
    }
}
