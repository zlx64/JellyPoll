using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JellyPoll.Services;

/// <summary>User-name resolution contract (extracted for M4 testability).</summary>
public interface IUserNameResolver
{
    /// <summary>Returns a display name, falling back to "Unknown user".</summary>
    string GetName(Guid userId);
}

/// <summary>Bridges IUserManager into DI.</summary>
public sealed class UserNameResolver : IUserNameResolver
{
    private readonly IUserManager _userManager;

    public UserNameResolver(IUserManager userManager)
    {
        _userManager = userManager;
    }

    public string GetName(Guid userId) => _userManager.GetUserById(userId)?.Username ?? "Unknown user";
}
