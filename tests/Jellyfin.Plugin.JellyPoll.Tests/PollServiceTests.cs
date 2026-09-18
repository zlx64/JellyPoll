using Jellyfin.Plugin.JellyPoll.Data;
using Jellyfin.Plugin.JellyPoll.Services;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.JellyPoll.Tests;

/// <summary>PollService business-rule tests with fakes (M4 hardening, doc 06 §6).</summary>
public class PollServiceTests
{
    private static (PollService Svc, FakePollRepository Repo, FakeLibrary Library, FakeConfigAccessor Config)
        Build(params (Guid id, string name)[] users)
    {
        var repo = new FakePollRepository();
        var lib = new FakeLibrary();
        var config = new FakeConfigAccessor();
        var svc = new PollService(repo, lib, new FakeUserNames(users), config, Microsoft.Extensions.Logging.Abstractions.NullLogger<PollService>.Instance);
        return (svc, repo, lib, config);
    }

    private static User NewUser(Guid id, bool admin = false)
    {
        var user = new User("user-" + id.ToString("N")[..8], "prov", "prov");
        user.Id = id;
        if (admin)
        {
            user.Permissions.Add(new Permission(PermissionKind.IsAdministrator, true));
        }

        return user;
    }

    private static PollRow OpenPoll(User creator, bool episodes = true, bool series = true)
    {
        var poll = new PollRow
        {
            Id = Guid.NewGuid(),
            Title = "T",
            CreatedBy = creator.Id,
            AllowEpisodes = episodes,
            AllowSeries = series
        };
        return poll;
    }

    // ---------- create ----------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void CreatePoll_EmptyTitle_Throws(string? title)
    {
        var (svc, _, _, _) = Build();
        var user = NewUser(Guid.NewGuid());
        Assert.Throws<ValidationException>(() => svc.CreatePoll(user, title!, true, true));
    }

    [Fact]
    public void CreatePoll_TooLongTitle_Throws()
    {
        var (svc, _, _, _) = Build();
        var user = NewUser(Guid.NewGuid());
        Assert.Throws<ValidationException>(() => svc.CreatePoll(user, new string('x', 101), true, true));
    }

    [Fact]
    public void CreatePoll_Valid_Persists()
    {
        var (svc, repo, _, _) = Build();
        var user = NewUser(Guid.NewGuid());
        var poll = svc.CreatePoll(user, "Friday", false, false);
        Assert.Equal("Friday", poll.Title);
        Assert.False(repo.Polls[poll.Id].AllowEpisodes);
        Assert.Equal(user.Id, repo.Polls[poll.Id].CreatedBy);
    }

    // ---------- permission matrix (creator / admin / other) ----------

    [Fact]
    public void ClosePoll_ByOtherUser_Denied()
    {
        var (svc, repo, _, _) = Build();
        var creator = NewUser(Guid.NewGuid());
        var stranger = NewUser(Guid.NewGuid());
        repo.CreatePoll(OpenPoll(creator));

        Assert.Throws<AccessDeniedException>(() => svc.ClosePoll(stranger, repo.Polls.Values.First().Id));
    }

    [Fact]
    public void ClosePoll_ByAdmin_Allowed()
    {
        var (svc, repo, _, _) = Build();
        var creator = NewUser(Guid.NewGuid());
        var admin = NewUser(Guid.NewGuid(), admin: true);
        repo.CreatePoll(OpenPoll(creator));
        var pollId = repo.Polls.Keys.First();

        svc.ClosePoll(admin, pollId);
        Assert.Equal(PollStatus.Closed, repo.GetPoll(pollId)!.Status);
        Assert.Equal(admin.Id, repo.GetPoll(pollId)!.ClosedBy);
    }

    [Fact]
    public void ClosePoll_ByCreator_Allowed()
    {
        var (svc, repo, _, _) = Build();
        var creator = NewUser(Guid.NewGuid());
        repo.CreatePoll(OpenPoll(creator));
        var pollId = repo.Polls.Keys.First();
        svc.ClosePoll(creator, pollId);
        Assert.Equal(PollStatus.Closed, repo.GetPoll(pollId)!.Status);
    }

    [Fact]
    public void DeletePoll_ByAdmin_Allowed()
    {
        var (svc, repo, _, _) = Build();
        var creator = NewUser(Guid.NewGuid());
        var admin = NewUser(Guid.NewGuid(), admin: true);
        repo.CreatePoll(OpenPoll(creator));
        var pollId = repo.Polls.Keys.First();
        svc.DeletePoll(admin, pollId);
        Assert.Null(repo.GetPoll(pollId));
    }

    [Fact]
    public void ReopenPoll_ByStranger_Denied()
    {
        var (svc, repo, _, _) = Build();
        var creator = NewUser(Guid.NewGuid());
        var stranger = NewUser(Guid.NewGuid());
        repo.CreatePoll(OpenPoll(creator));
        var pollId = repo.Polls.Keys.First();
        repo.ClosePoll(pollId, creator.Id);

        Assert.Throws<AccessDeniedException>(() => svc.ReopenPoll(stranger, pollId));
    }

    // ---------- suggestions ----------

    [Fact]
    public void AddSuggestion_OnClosedPoll_Throws()
    {
        var (svc, repo, lib, _) = Build();
        var creator = NewUser(Guid.NewGuid());
        var poll = OpenPoll(creator);
        repo.CreatePoll(poll);
        repo.ClosePoll(poll.Id, creator.Id);

        var item = Guid.NewGuid();
        lib.Items[item] = new Movie { Name = "Arrival", Id = item };
        lib.AccessibleToAllUsers.Add(item);

        Assert.Throws<PollClosedException>(() => svc.AddSuggestion(creator, poll.Id, item));
    }

    [Fact]
    public void AddSuggestion_ItemDeleted_Throws()
    {
        var (svc, repo, lib, _) = Build();
        var creator = NewUser(Guid.NewGuid());
        var poll = OpenPoll(creator);
        repo.CreatePoll(poll);

        Assert.Throws<SuggestionNotFoundException>(() => svc.AddSuggestion(creator, poll.Id, Guid.NewGuid()));
    }

    [Fact]
    public void AddSuggestion_EpisodeNotAllowed_Throws()
    {
        var (svc, repo, lib, _) = Build();
        var creator = NewUser(Guid.NewGuid());
        var poll = OpenPoll(creator, episodes: false);
        repo.CreatePoll(poll);

        var item = Guid.NewGuid();
        lib.Items[item] = new Episode { Name = "S01E01", Id = item };
        lib.AccessibleToAllUsers.Add(item);

        Assert.Throws<ItemTypeNotAllowedException>(() => svc.AddSuggestion(creator, poll.Id, item));
    }

    [Fact]
    public void AddSuggestion_SeriesNotAllowed_Throws()
    {
        var (svc, repo, lib, _) = Build();
        var creator = NewUser(Guid.NewGuid());
        var poll = OpenPoll(creator, series: false);
        repo.CreatePoll(poll);

        var item = Guid.NewGuid();
        lib.Items[item] = new Series { Name = "Show", Id = item };
        lib.AccessibleToAllUsers.Add(item);

        Assert.Throws<ItemTypeNotAllowedException>(() => svc.AddSuggestion(creator, poll.Id, item));
    }

    [Fact]
    public void AddSuggestion_NoAccess_Throws()
    {
        var (svc, repo, lib, _) = Build();
        var creator = NewUser(Guid.NewGuid());
        var poll = OpenPoll(creator);
        repo.CreatePoll(poll);

        var item = Guid.NewGuid();
        lib.Items[item] = new Movie { Name = "Secret", Id = item };
        // note: AccessibleToAllUsers deliberately left empty

        Assert.Throws<AccessDeniedException>(() => svc.AddSuggestion(creator, poll.Id, item));
    }

    [Fact]
    public void AddSuggestion_LimitReached_Throws()
    {
        var (svc, repo, lib, config) = Build();
        config.Current.MaxSuggestionsPerUser = 1;
        var creator = NewUser(Guid.NewGuid());
        var poll = OpenPoll(creator);
        repo.CreatePoll(poll);

        var item1 = Guid.NewGuid();
        lib.Items[item1] = new Movie { Name = "A", Id = item1 };
        lib.AccessibleToAllUsers.Add(item1);
        svc.AddSuggestion(creator, poll.Id, item1);

        var item2 = Guid.NewGuid();
        lib.Items[item2] = new Movie { Name = "B", Id = item2 };
        lib.AccessibleToAllUsers.Add(item2);

        Assert.Throws<SuggestionLimitReachedException>(() => svc.AddSuggestion(creator, poll.Id, item2));
    }

    [Fact]
    public void AddSuggestion_HappyPath_StoresMetadata()
    {
        var (svc, repo, lib, _) = Build();
        var creator = NewUser(Guid.NewGuid());
        var poll = OpenPoll(creator);
        repo.CreatePoll(poll);

        var item = Guid.NewGuid();
        lib.Items[item] = new Movie { Name = "Arrival", ProductionYear = 2016, Id = item };
        lib.AccessibleToAllUsers.Add(item);

        var suggestion = svc.AddSuggestion(creator, poll.Id, item);
        Assert.Equal("Movie", suggestion.ItemType);
        Assert.Equal("Arrival", suggestion.ItemName);
        Assert.Equal(2016, suggestion.ItemYear);
        Assert.Equal(creator.Id, suggestion.SuggestedBy);
    }

    // ---------- remove suggestion ----------

    [Fact]
    public void RemoveSuggestion_ByStranger_Denied()
    {
        var (svc, repo, lib, _) = Build();
        var creator = NewUser(Guid.NewGuid());
        var suggester = NewUser(Guid.NewGuid());
        var stranger = NewUser(Guid.NewGuid());
        var poll = OpenPoll(creator);
        repo.CreatePoll(poll);

        var item = Guid.NewGuid();
        lib.Items[item] = new Movie { Name = "A", Id = item };
        lib.AccessibleToAllUsers.Add(item);
        var suggestion = svc.AddSuggestion(suggester, poll.Id, item);

        Assert.Throws<AccessDeniedException>(() => svc.RemoveSuggestion(stranger, poll.Id, suggestion.Id));
    }

    [Fact]
    public void RemoveSuggestion_ByOwnSuggester_Allowed()
    {
        var (svc, repo, lib, _) = Build();
        var creator = NewUser(Guid.NewGuid());
        var suggester = NewUser(Guid.NewGuid());
        var poll = OpenPoll(creator);
        repo.CreatePoll(poll);

        var item = Guid.NewGuid();
        lib.Items[item] = new Movie { Name = "A", Id = item };
        lib.AccessibleToAllUsers.Add(item);
        var suggestion = svc.AddSuggestion(suggester, poll.Id, item);

        svc.RemoveSuggestion(suggester, poll.Id, suggestion.Id);
        Assert.Null(repo.GetSuggestion(poll.Id, suggestion.Id));
    }

    [Fact]
    public void RemoveSuggestion_OnClosedPoll_Throws()
    {
        var (svc, repo, lib, _) = Build();
        var creator = NewUser(Guid.NewGuid());
        var poll = OpenPoll(creator);
        repo.CreatePoll(poll);

        var item = Guid.NewGuid();
        lib.Items[item] = new Movie { Name = "A", Id = item };
        lib.AccessibleToAllUsers.Add(item);
        var suggestion = svc.AddSuggestion(creator, poll.Id, item);
        repo.ClosePoll(poll.Id, creator.Id);

        Assert.Throws<PollClosedException>(() => svc.RemoveSuggestion(creator, poll.Id, suggestion.Id));
    }

    // ---------- ballots ----------

    [Fact]
    public void SaveBallot_DuplicateIds_Throws()
    {
        var (svc, repo, lib, _) = Build();
        var creator = NewUser(Guid.NewGuid());
        var poll = OpenPoll(creator);
        repo.CreatePoll(poll);
        var item = Guid.NewGuid();
        lib.Items[item] = new Movie { Name = "A", Id = item };
        lib.AccessibleToAllUsers.Add(item);
        var s = svc.AddSuggestion(creator, poll.Id, item);

        Assert.Throws<ValidationException>(() => svc.SaveBallot(creator, poll.Id, new[] { s.Id, s.Id }));
    }

    // ---------- standings visibility ----------

    [Fact]
    public void BuildDetail_LiveStandingsDisabled_OpenPollHidesStandings()
    {
        var (svc, repo, lib, config) = Build();
        config.Current.ShowLiveStandings = false;
        var creator = NewUser(Guid.NewGuid());
        var poll = OpenPoll(creator);
        repo.CreatePoll(poll);
        var item = Guid.NewGuid();
        lib.Items[item] = new Movie { Name = "A", Id = item };
        lib.AccessibleToAllUsers.Add(item);
        var s = svc.AddSuggestion(creator, poll.Id, item);
        repo.UpsertBallot(poll.Id, creator.Id, new[] { s.Id });

        var detail = svc.BuildDetail(creator, poll.Id);
        Assert.Empty(detail.Standings);

        repo.ClosePoll(poll.Id, creator.Id);
        var closed = svc.BuildDetail(creator, poll.Id);
        Assert.NotEmpty(closed.Standings);
        Assert.Equal(1, closed.Standings[0].Rank);
    }

    [Fact]
    public void GetResults_PodiumMappedCorrectly()
    {
        var (svc, repo, lib, _) = Build();
        var creator = NewUser(Guid.NewGuid());
        var u2 = NewUser(Guid.NewGuid());
        var poll = OpenPoll(creator);
        repo.CreatePoll(poll);

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        lib.Items[a] = new Movie { Name = "A", Id = a };
        lib.Items[b] = new Movie { Name = "B", Id = b };
        lib.AccessibleToAllUsers.Add(a);
        lib.AccessibleToAllUsers.Add(b);

        var sa = svc.AddSuggestion(creator, poll.Id, a);
        var sb = svc.AddSuggestion(u2, poll.Id, b);
        repo.UpsertBallot(poll.Id, creator.Id, new[] { sa.Id, sb.Id });
        repo.UpsertBallot(poll.Id, u2.Id, new[] { sa.Id, sb.Id });

        var results = svc.GetResults(poll.Id);
        Assert.NotNull(results.Gold);
        Assert.Equal("A", results.Gold!.Name);
        Assert.Equal(sa.Id.ToString(), results.Gold.SuggestionId);
        Assert.NotNull(results.Silver);
        Assert.Equal("B", results.Silver!.Name);
        Assert.Null(results.Bronze);
    }
}
