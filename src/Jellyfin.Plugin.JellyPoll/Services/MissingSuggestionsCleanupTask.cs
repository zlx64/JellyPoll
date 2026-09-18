using Jellyfin.Plugin.JellyPoll.Data;
using Jellyfin.Plugin.JellyPoll.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.JellyPoll.Services;

/// <summary>
/// Scheduled maintenance: removes suggestions whose library item no longer exists
/// (doc 01 §5, M4 hardening). Discovered automatically by the server.
/// </summary>
public sealed class MissingSuggestionsCleanupTask : IScheduledTask
{
    private readonly IPollRepository _repo;
    private readonly ILibraryAccessValidator _library;

    public MissingSuggestionsCleanupTask(IPollRepository repo, ILibraryAccessValidator library)
    {
        _repo = repo;
        _library = library;
    }

    public string Name => "JellyPoll: remove suggestions whose media is missing";

    public string Description => "Removes poll suggestions that no longer exist in any Jellyfin library.";

    public string Category => "JellyPoll";

    public string Key => "JellyPollMissingSuggestionsCleanup";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var missing = new List<Guid>();
        foreach (var poll in _repo.ListPolls())
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var suggestion in _repo.ListSuggestions(poll.Id))
            {
                if (_library.ResolveItem(suggestion.ItemId) is null)
                {
                    missing.Add(suggestion.Id);
                }
            }
        }

        var removed = _repo.RemoveSuggestions(missing);
        progress.Report(100);
        return Task.CompletedTask;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => new[]
    {
        new TaskTriggerInfo
        {
            Type = MediaBrowser.Model.Tasks.TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
        }
    };
}
