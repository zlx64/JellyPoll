using Jellyfin.Plugin.JellyPoll.Api;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyPoll.Api;

/// <summary>Shared plumbing for JellyPoll API controllers: auth resolution + error mapping.</summary>
[ApiController]
public abstract class JellyPollControllerBase : ControllerBase
{
    protected readonly IAuthorizationContext AuthorizationContext;

    protected JellyPollControllerBase(IAuthorizationContext authorizationContext)
    {
        AuthorizationContext = authorizationContext;
    }

    /// <summary>Resolves the calling Jellyfin user; null when unauthenticated.</summary>
    protected async Task<Jellyfin.Database.Implementations.Entities.User?> ResolveUserAsync()
    {
        var info = await AuthorizationContext.GetAuthorizationInfo(Request).ConfigureAwait(false);
        return info.IsApiKey ? null : info.User;
    }

    protected ObjectResult Error(int statusCode, string code, string message)
        => StatusCode(statusCode, new ApiError(code, message));

    protected ObjectResult UnauthorizedError() => Error(401, "unauthenticated", "Authentication required.");

    protected ObjectResult ForbiddenError() => Error(403, "forbidden", "You are not allowed to perform this action.");

    protected ObjectResult NotFoundError() => Error(404, "not_found", "Not found.");
}
