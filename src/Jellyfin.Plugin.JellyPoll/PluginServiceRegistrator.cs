using Jellyfin.Plugin.JellyPoll.Api;
using Jellyfin.Plugin.JellyPoll.Data;
using Jellyfin.Plugin.JellyPoll.Library;
using Jellyfin.Plugin.JellyPoll.Menu;
using Jellyfin.Plugin.JellyPoll.Nav;
using Jellyfin.Plugin.JellyPoll.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyPoll;

/// <summary>Registers plugin services in the host DI container (doc 06 §2).</summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Self-contained registrations: do NOT depend on Plugin.Instance being constructed yet
        // (the registrator can run before plugin instantiation; M4 E2E discovered this the hard way).
        serviceCollection.AddSingleton<Db>();
        serviceCollection.AddSingleton<IPollRepository>(sp =>
        {
            var repo = new SqlitePollRepository(sp.GetRequiredService<Db>());
            repo.Initialize();
            return repo;
        });
        serviceCollection.AddSingleton<ILibraryAccessValidator, LibraryAccessValidator>();
        serviceCollection.AddSingleton<IConfigurationAccessor, PluginConfigurationAccessor>();
        serviceCollection.AddSingleton<IUserNameResolver, UserNameResolver>();
        serviceCollection.AddSingleton<PollService>();
        serviceCollection.AddSingleton<MenuLinkInstaller>();
        // IStartupFilter: Jellyfin invokes it when building the host, letting the
        // plugin add middleware (index.html nav injection) to the HTTP pipeline —
        // the same mechanism JellyfinSecurity (TwoFactorAuth) uses. Must not
        // depend on Plugin.Instance at registration time (see comment above).
        serviceCollection.AddSingleton<IStartupFilter, JellyPollStartupFilter>();
    }
}
