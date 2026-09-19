using Jellyfin.Plugin.JellyPoll.Data;
using Jellyfin.Plugin.JellyPoll.Services;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

namespace Jellyfin.Plugin.JellyPoll.Tests;

/// <summary>Collection (BoxSet) suggestion expansion rules.</summary>
public class CollectionSuggestionTests
{
    private readonly Guid _user = Guid.NewGuid();

    private static (PollService Svc, FakePollRepository Repo, FakeLibrary Library, FakeConfigAccessor Config)
        Build(params (Guid id, string name)[] users)
    {
        var repo = new FakePollRepository();
        var lib = new FakeLibrary();
        var config = new FakeConfigAccessor();
        var svc = new PollService(repo, lib, new FakeUserNames(users), config, Microsoft.Extensions.Logging.Abstractions.NullLogger<PollService>.Instance);
        return (svc, repo, lib, config);
    }

    private User User() => new("user-" + _user.ToString("N")[..8], "prov", "prov") { Id = _user };

    private static Movie Movie(string name)
    {
        var m = new Movie { Name = name, SortName = name };
        m.Id = Guid.NewGuid();
        return m;
    }

    private (Guid BoxId, List<Movie> Movies) RegisterCollection(FakeLibrary lib, params Movie[] movies)
    {
        foreach (var m in movies)
        {
            lib.Items[m.Id] = m;
            lib.AccessibleToAllUsers.Add(m.Id);
        }

        var box = new BoxSet { Name = "Box" };
        box.Id = Guid.NewGuid();
        lib.Items[box.Id] = box;
        lib.AccessibleToAllUsers.Add(box.Id);
        lib.Collections[box.Id] = movies.Select(m => m.Id).ToList();
        return (box.Id, movies.ToList());
    }

    private PollRow MakeOpenPoll(PollService svc)
    {
        var creator = new User("creator", "prov", "prov") { Id = Guid.NewGuid() };
        return svc.CreatePoll(creator, "T", true, true);
    }

    [Fact]
    public void Suggest_Collection_ExpandsIntoMovies()
    {
        var (svc, repo, lib, config) = Build();
        var user = User();
        var poll = MakeOpenPoll(svc);
        var m1 = Movie("Alpha"); var m2 = Movie("Beta"); var m3 = Movie("Gamma");
        var (boxId, _) = RegisterCollection(lib, m1, m2, m3);

        var outcome = svc.Suggest(user, poll.Id, boxId);

        Assert.Null(outcome.Single);
        Assert.Equal(3, outcome.CollectionAdded.Count);
        Assert.Equal("Box", outcome.CollectionName);
        Assert.All(outcome.CollectionAdded, s =>
        {
            Assert.Equal("Movie", s.ItemType);
            Assert.Equal(user.Id, s.SuggestedBy);
            Assert.Equal(poll.Id, s.PollId);
        });
        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }.OrderBy(x => x), repo.Suggestions.Values.Select(s => s.ItemName).OrderBy(x => x));
    }

    [Fact]
    public void Suggest_Collection_SkipsMoviesAlreadyInPoll()
    {
        var (svc, repo, lib, config) = Build();
        var user = User();
        var poll = MakeOpenPoll(svc);
        var m1 = Movie("Alpha"); var m2 = Movie("Beta");
        var (boxId, _) = RegisterCollection(lib, m1, m2);

        // Pre-add m1 to the poll as someone else's suggestion.
        repo.AddSuggestion(new SuggestionRow { Id = Guid.NewGuid(), PollId = poll.Id, ItemId = m1.Id, ItemType = "Movie", ItemName = "Alpha", SuggestedBy = Guid.NewGuid() });

        var outcome = svc.Suggest(user, poll.Id, boxId);

        Assert.Single(outcome.CollectionAdded);
        Assert.Equal("Beta", outcome.CollectionAdded[0].ItemName);
        Assert.Equal(1, outcome.CollectionSkippedExisting);
        Assert.Equal(2, repo.Suggestions.Count);
    }

    [Fact]
    public void Suggest_Collection_AllAccessibleEmpty_ThrowsValidation()
    {
        var (svc, repo, lib, config) = Build();
        var user = User();
        var poll = MakeOpenPoll(svc);
        var movie = Movie("Hidden");
        var boxId = Guid.NewGuid();
        var box = new BoxSet { Name = "Box" };
        box.Id = boxId;
        lib.Items[boxId] = box;
        lib.AccessibleToAllUsers.Add(boxId);
        lib.Collections[boxId] = new List<Guid> { movie.Id }; // movie exists but is NOT accessible
        lib.Items[movie.Id] = movie; // present but not accessible

        Assert.Throws<ValidationException>(() => svc.Suggest(user, poll.Id, boxId));
    }

    [Fact]
    public void Suggest_Collection_RespectsPerUserLimit()
    {
        var (svc, repo, lib, config) = Build();
        var user = User();
        var poll = MakeOpenPoll(svc);
        config.Current.MaxSuggestionsPerUser = 2;
        var movies = new[] { Movie("A"), Movie("B"), Movie("C"), Movie("D") };
        var (boxId, _) = RegisterCollection(lib, movies);

        var outcome = svc.Suggest(user, poll.Id, boxId);

        Assert.Equal(2, outcome.CollectionAdded.Count);
        Assert.Equal(2, outcome.CollectionSkippedOverLimit);
        Assert.Equal(0, outcome.CollectionSkippedExisting);
        Assert.Equal(2, repo.CountSuggestionsByUser(poll.Id, user.Id));
    }

    [Fact]
    public void Suggest_Collection_QuotaAlreadyExhausted_Throws()
    {
        var (svc, repo, lib, config) = Build();
        var user = User();
        var poll = MakeOpenPoll(svc);
        config.Current.MaxSuggestionsPerUser = 1;
        var (boxId, _) = RegisterCollection(lib, Movie("A"), Movie("B"));

        repo.AddSuggestion(new SuggestionRow { Id = Guid.NewGuid(), PollId = poll.Id, ItemId = Guid.NewGuid(), ItemType = "Movie", ItemName = "Pre", SuggestedBy = user.Id });

        Assert.Throws<SuggestionLimitReachedException>(() => svc.Suggest(user, poll.Id, boxId));
    }

    [Fact]
    public void Suggest_Collection_NoAccessToBox_Throws()
    {
        var (svc, repo, lib, config) = Build();
        var user = User();
        var poll = MakeOpenPoll(svc);
        var (boxId, _) = RegisterCollection(lib, Movie("A"));
        lib.AccessibleToAllUsers.Remove(boxId); // user cannot see the collection itself

        Assert.Throws<AccessDeniedException>(() => svc.Suggest(user, poll.Id, boxId));
    }

    [Fact]
    public void Suggest_Collection_ClosedPoll_Throws()
    {
        var (svc, repo, lib, config) = Build();
        var user = User();
        var poll = MakeOpenPoll(svc);
        var (boxId, _) = RegisterCollection(lib, Movie("A"));
        repo.ClosePoll(poll.Id, poll.CreatedBy);

        Assert.Throws<PollClosedException>(() => svc.Suggest(user, poll.Id, boxId));
    }

    [Fact]
    public void Suggest_Collection_AllAlreadyPresent_ThrowsDuplicate()
    {
        var (svc, repo, lib, config) = Build();
        var user = User();
        var poll = MakeOpenPoll(svc);
        var (boxId, movies) = RegisterCollection(lib, Movie("A"), Movie("B"));

        // Everything already in the poll (as another user's suggestions).
        foreach (var m in movies)
        {
            repo.AddSuggestion(new SuggestionRow { Id = Guid.NewGuid(), PollId = poll.Id, ItemId = m.Id, ItemType = "Movie", ItemName = m.Name, SuggestedBy = Guid.NewGuid() });
        }

        Assert.Throws<DuplicateSuggestionException>(() => svc.Suggest(user, poll.Id, boxId));
    }

    [Fact]
    public void Suggest_Collection_PartialOverLimit_StillAddsFirstOnes()
    {
        var (svc, repo, lib, config) = Build();
        var user = User();
        var poll = MakeOpenPoll(svc);
        config.Current.MaxSuggestionsPerUser = 3;
        var (boxId, _) = RegisterCollection(lib, Movie("A"), Movie("B"), Movie("C"), Movie("D"), Movie("E"));

        var outcome = svc.Suggest(user, poll.Id, boxId);

        Assert.Equal(3, outcome.CollectionAdded.Count);
        Assert.Equal(2, outcome.CollectionSkippedOverLimit);
    }

    [Fact]
    public void ListCollections_FiltersByTermAndAccess()
    {
        var (svc, repo, lib, config) = Build();
        var user = User();

        var visible = RegisterCollection(lib, Movie("A"));
        var hidden = RegisterCollection(lib, Movie("B"));
        lib.Items[hidden.BoxId].Name = "HiddenBox"; // registered but not accessible
        lib.AccessibleToAllUsers.Remove(hidden.BoxId);
        lib.Items[visible.BoxId].Name = "Spider-Man Collection";

        var all = svc.ListCollections(user, null);
        Assert.Single(all);
        Assert.Equal("Spider-Man Collection", all[0].Name);
        Assert.Equal(1, all[0].MovieCount);

        var byTerm = svc.ListCollections(user, "spider");
        Assert.Single(byTerm);

        Assert.Empty(svc.ListCollections(user, "nomatch"));
    }
}
