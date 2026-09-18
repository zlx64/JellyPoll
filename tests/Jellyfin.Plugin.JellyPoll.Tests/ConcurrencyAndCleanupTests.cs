using Jellyfin.Plugin.JellyPoll.Data;
using Jellyfin.Plugin.JellyPoll.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyPoll.Tests;

/// <summary>Concurrency hardening tests against the real SQLite repository (M4).</summary>
public class ConcurrencyTests : IDisposable
{
    private readonly string _dir;
    private readonly SqlitePollRepository _repo;

    public ConcurrencyTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "jellypoll-concurrency", Guid.NewGuid().ToString("N"));
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
        Title = "Race",
        CreatedBy = creator
    };

    private static SuggestionRow NewSuggestion(Guid pollId, Guid itemId, Guid by) => new()
    {
        Id = Guid.NewGuid(),
        PollId = pollId,
        ItemId = itemId,
        ItemType = "Movie",
        ItemName = "M",
        SuggestedBy = by
    };

    [Fact]
    public void ParallelSameItemSuggestion_ExactlyOneWins()
    {
        var creator = Guid.NewGuid();
        var poll = NewPoll(creator);
        _repo.CreatePoll(poll);
        var item = Guid.NewGuid();

        const int threads = 8;
        var successes = 0;
        var duplicates = 0;
        var barrier = new Barrier(threads);
        var tasks = Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            try
            {
                _repo.AddSuggestion(NewSuggestion(poll.Id, item, Guid.NewGuid()));
                Interlocked.Increment(ref successes);
            }
            catch (DuplicateSuggestionException)
            {
                Interlocked.Increment(ref duplicates);
            }
        })).ToArray();

        Task.WaitAll(tasks);
        Assert.Equal(1, successes);
        Assert.Equal(threads - 1, duplicates);
        Assert.Equal(1, _repo.GetSuggestionCount(poll.Id));
    }

    [Fact]
    public void ParallelDistinctSuggestions_AllPersist_StateVersionCorrect()
    {
        var creator = Guid.NewGuid();
        var poll = NewPoll(creator);
        _repo.CreatePoll(poll);
        var v0 = _repo.GetStateVersion(poll.Id);

        const int writers = 20;
        Parallel.For(0, writers, i =>
        {
            _repo.AddSuggestion(NewSuggestion(poll.Id, Guid.NewGuid(), creator));
        });

        Assert.Equal(writers, _repo.GetSuggestionCount(poll.Id));
        Assert.Equal(v0 + writers, _repo.GetStateVersion(poll.Id));
    }

    [Fact]
    public void ParallelBallotUpdates_FinalBallotConsistent()
    {
        var creator = Guid.NewGuid();
        var poll = NewPoll(creator);
        _repo.CreatePoll(poll);

        var suggestions = new List<SuggestionRow>();
        for (var i = 0; i < 10; i++)
        {
            var s = NewSuggestion(poll.Id, Guid.NewGuid(), creator);
            _repo.AddSuggestion(s);
            suggestions.Add(s);
        }

        var voter = Guid.NewGuid();
        var order = suggestions.Select(s => s.Id).ToList();

        // Many concurrent full rewrites; every intermediate state must be a permutation of `order` with no gaps.
        Parallel.For(0, 10, i =>
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var shuffled = order.OrderBy(_ => Random.Shared.Next()).ToList();
                _repo.UpsertBallot(poll.Id, voter, shuffled);
            }
        });

        var final = _repo.GetBallot(poll.Id, voter);
        Assert.Equal(order.Count, final.Count);
        Assert.Equal(order.OrderBy(x => x), final.OrderBy(x => x)); // same set, positions gap-free by construction
    }

    [Fact]
    public void RemoveSuggestions_BumpsStateEvenWhenAllSuggestionsRemoved()
    {
        var creator = Guid.NewGuid();
        var poll = NewPoll(creator);
        _repo.CreatePoll(poll);
        var s = NewSuggestion(poll.Id, Guid.NewGuid(), creator);
        _repo.AddSuggestion(s);
        var v1 = _repo.GetStateVersion(poll.Id);

        var removed = _repo.RemoveSuggestions(new[] { s.Id });

        Assert.Equal(1, removed);
        // Poll had no ballots — state version must still advance.
        Assert.Equal(v1 + 1, _repo.GetStateVersion(poll.Id));
    }
}

/// <summary>Tests the missing-suggestions cleanup task logic (M4).</summary>
public class MissingSuggestionsCleanupTaskTests
{
    [Fact]
    public void Execute_RemovesOnlyMissingSuggestions()
    {
        var repo = new FakePollRepository();
        var lib = new FakeLibrary();
        var creator = Guid.NewGuid();
        var poll = new PollRow { Id = Guid.NewGuid(), Title = "T", CreatedBy = creator };
        repo.CreatePoll(poll);

        var missingItem = Guid.NewGuid();
        var presentItem = Guid.NewGuid();
        lib.Items[presentItem] = new MediaBrowser.Controller.Entities.Movies.Movie { Name = "Present", Id = presentItem };

        var sMissing = new SuggestionRow
        {
            Id = Guid.NewGuid(), PollId = poll.Id, ItemId = missingItem,
            ItemType = "Movie", ItemName = "Gone", SuggestedBy = creator,
            SuggestedAt = "2026-01-01T00:00:00Z"
        };
        var sPresent = new SuggestionRow
        {
            Id = Guid.NewGuid(), PollId = poll.Id, ItemId = presentItem,
            ItemType = "Movie", ItemName = "Present", SuggestedAt = "2026-01-01T00:00:01Z"
        };
        repo.AddSuggestion(sMissing);
        repo.AddSuggestion(sPresent);

        var task = new MissingSuggestionsCleanupTask(repo, lib);
        task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

        Assert.Null(repo.GetSuggestion(poll.Id, sMissing.Id));
        Assert.NotNull(repo.GetSuggestion(poll.Id, sPresent.Id));
        Assert.Contains(sMissing.Id, repo.RemovedSuggestions);
        Assert.DoesNotContain(sPresent.Id, repo.RemovedSuggestions);
    }
}
