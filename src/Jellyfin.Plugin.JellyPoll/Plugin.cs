using Jellyfin.Plugin.JellyPoll.Configuration;
using Jellyfin.Plugin.JellyPoll.Data;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyPoll;

/// <summary>
/// Plugin entry point. Owns the SQLite repository (created at construction so
/// PluginServiceRegistrator can register the instance) and exposes the dashboard
/// configuration page via IHasWebPages (doc 06 §2).
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, MediaBrowser.Model.Serialization.IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        Repository = new SqlitePollRepository(new Db(applicationPaths));
        try
        {
            Repository.Initialize();
        }
        catch (Exception ex)
        {
            // Storage failure disables functionality; API responds 503 (doc 02 §5).
            Console.Error.WriteLine($"[JellyPoll] storage init failed: {ex.Message}");
        }
    }

    /// <summary>Singleton access for the service registrator and config-dependent services.</summary>
    public static Plugin? Instance { get; private set; }

    public SqlitePollRepository Repository { get; }

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
            DisplayName = "JellyPoll",
            EmbeddedResourcePath = "Jellyfin.Plugin.JellyPoll.Configuration.configPage.html"
        }
    };
}
