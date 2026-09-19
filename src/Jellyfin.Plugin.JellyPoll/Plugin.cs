using Jellyfin.Plugin.JellyPoll.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyPoll;

/// <summary>
/// Plugin entry point. Exposes the dashboard configuration page via IHasWebPages (doc 06 §2).
/// Services (SQLite repository etc.) are registered by PluginServiceRegistrator — independent
/// of this instance's construction order.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, MediaBrowser.Model.Serialization.IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>Singleton access for config-dependent services.</summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "JellyPoll";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("3734440f-4b20-4c3a-86a3-d931b45b7248");

    /// <inheritdoc />
    public override string Description => "Group polls to choose what to watch together: suggest, rank, and crown the top 3.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages() => new[]
    {
        new PluginPageInfo
        {
            Name = "jellypoll",
            DisplayName = "Jelly Polls",
            EmbeddedResourcePath = "Jellyfin.Plugin.JellyPoll.Configuration.configPage.html",
            // Officially renders in the web client's navigation (dashboard drawer "Plugins"
            // section) — jellyfin-web fetches pages with enableInMainMenu=true and links to
            // /configurationpage?name=jellypoll. No webroot file modification needed.
            EnableInMainMenu = true,
            MenuIcon = "how_to_vote"
        }
    };
}
