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

    /// <summary>
    /// When true (default), a "Jelly Polls" entry is injected into the web UI's
    /// avatar menu / sidebar for ALL users via an index.html response transform
    /// (no webroot modification).
    /// </summary>
    public bool ShowMainNavEntry { get; set; } = true;

    /// <summary>
    /// Global UI language override for the SPA and the dashboard config page
    /// (docs/localization-plan.md §4.2). "" = auto — each viewer's own
    /// Jellyfin/browser language is used. Supported: "en", "uk".
    /// </summary>
    public string DisplayLanguage { get; set; } = "";
}
