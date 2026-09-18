using Jellyfin.Plugin.JellyPoll.Data;
using Xunit;

namespace Jellyfin.Plugin.JellyPoll.Tests;

/// <summary>Tests for bulk closed-poll deletion (doc: delete all closed polls).</summary>
public class DeleteClosedPollsTests : IDisposable
{
    private readonly string _dir;
    private readonly SqlitePollRepository _repo;

    public DeleteClosedPollsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "jellypoll-deleteclosed", Guid.NewGuid().ToString("N"));
        _repo = new SqlitePollRepository(new Db(Path.Combine(_dir, "test.db")));
        _repo.Initialize();
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static PollRow NewPoll(Guid creator) => new()
    {
        Id = Guid.NewGuid(),
        Title = "P",
        CreatedBy = creator
    };

    private static SuggestionRow NewSuggestion(Guid pollId, Guid by) => new()
    {
        Id = Guid.NewGuid(),
        PollId = pollId,
        ItemId = Guid.NewGuid(),
        ItemType = "Movie",
        ItemName = "M",
        SuggestedBy = by
    };

    [Fact]
    public void DeleteClosedPolls_DeletesOnlyClosed_AndCascades()
    {
        var creator = Guid.NewGuid();
        var open = NewPoll(creator);
        var closed1 = NewPoll(creator);
        var closed2 = NewPoll(creator);
        _repo.CreatePoll(open);
        _repo.CreatePoll(closed1);
        _repo.CreatePoll(closed2);

        // add data to closed polls to verify cascade
        var s = NewSuggestion(closed1.Id, creator);
        _repo.AddSuggestion(s);
        _repo.UpsertBallot(closed1.Id, Guid.NewGuid(), new[] { s.Id });
        _repo.ClosePoll(closed1.Id, creator);
        _repo.ClosePoll(closed2.Id, creator);

        var deleted = _repo.DeleteClosedPolls();

        Assert.Equal(2, deleted);
        Assert.Null(_repo.GetPoll(closed1.Id));
        Assert.Null(_repo.GetPoll(closed2.Id));
        Assert.NotNull(_repo.GetPoll(open.Id));
        Assert.Empty(_repo.GetSnapshot(closed1.Id).Ballots);
        Assert.Empty(_repo.ListSuggestions(closed1.Id));
    }

    [Fact]
    public void DeleteClosedPolls_NoClosed_ReturnsZero()
    {
        var creator = Guid.NewGuid();
        var open = NewPoll(creator);
        _repo.CreatePoll(open);

        Assert.Equal(0, _repo.DeleteClosedPolls());
        Assert.NotNull(_repo.GetPoll(open.Id));
    }
}
