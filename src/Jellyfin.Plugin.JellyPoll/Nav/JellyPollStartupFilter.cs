using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyPoll.Nav;

/// <summary>
/// Standard ASP.NET Core IStartupFilter, registered into the host DI container
/// by PluginServiceRegistrator. Jellyfin invokes it while building the host,
/// which lets the plugin add middleware to the HTTP pipeline — the same
/// mechanism the JellyfinSecurity (TwoFactorAuth) plugin uses to inject its
/// 2FA script into the web UI.
/// </summary>
public sealed class JellyPollStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<NavInjectionMiddleware>();
            next(app);
        };
    }
}
