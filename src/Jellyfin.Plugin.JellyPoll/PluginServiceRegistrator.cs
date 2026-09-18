using Jellyfin.Plugin.JellyPoll.Api;
using Jellyfin.Plugin.JellyPoll.Library;
using Jellyfin.Plugin.JellyPoll.Menu;
using Jellyfin.Plugin.JellyPoll.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyPoll;

/// <summary>Registers plugin services in the host DI container (doc 06 §2).</summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        var instance = Plugin.Instance;
        if (instance is null)
        {
            // Plugin failed to construct; keep server booting but without JellyPoll services.
            return;
        }

        serviceCollection.AddSingleton(instance.Repository);
        serviceCollection.AddSingleton<LibraryAccessValidator>();
        serviceCollection.AddSingleton<PollService>();
        serviceCollection.AddSingleton<MenuLinkInstaller>();
    }
}
