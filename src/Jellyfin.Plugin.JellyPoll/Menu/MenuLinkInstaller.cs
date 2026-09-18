using System.Text.Json;
using System.Text.Json.Nodes;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.JellyPoll.Menu;

/// <summary>
/// Adds/removes the "Movie Polls" entry in the web client's config.json menuLinks
/// (doc 06 §4). Creates a one-time backup before first write.
/// </summary>
public sealed class MenuLinkInstaller
{
    public const string MenuName = "Movie Polls";
    public const string MenuUrl = "/JellyPoll/Web/";
    public const string MenuIcon = "how_to_vote";

    private readonly IApplicationPaths _applicationPaths;

    public MenuLinkInstaller(IApplicationPaths applicationPaths)
    {
        _applicationPaths = applicationPaths;
    }

    public string WebConfigPath => Path.Combine(_applicationPaths.WebPath, "config.json");

    /// <summary>Installs or removes the menu link. Returns true when the entry is present afterwards.</summary>
    public bool SetMenuLink(bool install)
    {
        var path = WebConfigPath;
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Web client config.json not found at " + path);
        }

        JsonNode root = JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidOperationException("config.json is not valid JSON");

        var menuLinks = root["menuLinks"] as JsonArray
            ?? throw new InvalidOperationException("config.json has no menuLinks array");

        var existingIndex = FindIndex(menuLinks);
        var present = install && existingIndex >= 0;

        if (existingIndex >= 0)
        {
            if (install)
            {
                return true; // already installed
            }

            menuLinks.RemoveAt(existingIndex);
        }
        else if (install)
        {
            BackupOnce(path);
            menuLinks.Add(new JsonObject
            {
                ["name"] = MenuName,
                ["url"] = MenuUrl,
                ["icon"] = MenuIcon
            });
        }
        else
        {
            return false; // already absent
        }

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return present;
    }

    public bool IsInstalled()
    {
        var path = WebConfigPath;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path));
            return FindIndex(root?["menuLinks"] as JsonArray) >= 0;
        }
        catch
        {
            return false;
        }
    }

    private static int FindIndex(JsonArray? menuLinks)
    {
        if (menuLinks is null)
        {
            return -1;
        }

        for (var i = 0; i < menuLinks.Count; i++)
        {
            if (menuLinks[i]?["url"]?.GetValue<string>() == MenuUrl)
            {
                return i;
            }
        }

        return -1;
    }

    private void BackupOnce(string path)
    {
        var backup = path + ".jellypoll.bak";
        if (!File.Exists(backup))
        {
            File.Copy(path, backup);
        }
    }
}
