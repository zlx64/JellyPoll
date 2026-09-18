using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyPoll.Configuration;

/// <summary>Plugin settings persisted as JellyPoll.xml by the framework (doc 06 §3).</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Default for new polls: episodes can be suggested.</summary>
    public bool DefaultAllowEpisodes { get; set; } = true;

    /// <summary>Default for new polls: series can be suggested.</summary>
    public bool DefaultAllowSeries { get; set; } = true;

    /// <summary>Max suggestions per user per poll; 0 = unlimited.</summary>
    public int MaxSuggestionsPerUser { get; set; } = 0;

    /// <summary>When false, standings are hidden until a poll closes (server-enforced).</summary>
    public bool ShowLiveStandings { get; set; } = true;
}
