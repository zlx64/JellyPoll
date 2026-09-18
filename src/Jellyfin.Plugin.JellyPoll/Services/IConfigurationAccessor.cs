using Jellyfin.Plugin.JellyPoll.Configuration;

namespace Jellyfin.Plugin.JellyPoll.Services;

/// <summary>Indirection over plugin configuration so services are unit-testable (M4).</summary>
public interface IConfigurationAccessor
{
    PluginConfiguration Current { get; }
}

/// <summary>Bridges the static plugin singleton into DI.</summary>
public sealed class PluginConfigurationAccessor : IConfigurationAccessor
{
    public PluginConfiguration Current =>
        Plugin.Instance?.Configuration ?? new PluginConfiguration();
}
