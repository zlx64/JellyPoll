using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Jellyfin.Database.Implementations.Entities;

namespace Jellyfin.Plugin.JellyPoll.Library;

/// <summary>
/// Resolves library items and validates the calling user's access (doc 01 §5).
/// Access check re-queries the item with the user attached so Jellyfin's own
/// access/parental controls apply.
/// </summary>
public sealed class LibraryAccessValidator
{
    private readonly ILibraryManager _libraryManager;

    public LibraryAccessValidator(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <summary>Resolves a base item by id, or null when it no longer exists.</summary>
    public BaseItem? ResolveItem(Guid itemId) => _libraryManager.GetItemById(itemId);

    /// <summary>
    /// True when the item exists and is visible to the user through a query with the user attached.
    /// </summary>
    public bool CanAccess(User user, Guid itemId)
    {
        var item = ResolveItem(itemId);
        if (item is null)
        {
            return false;
        }

        var query = new InternalItemsQuery(user)
        {
            ItemIds = new[] { itemId },
            Recursive = true,
            Limit = 1
        };
        return _libraryManager.GetItemList(query).Count > 0;
    }

    /// <summary>Suggestion type name for a resolved item, or null when type unsupported.</summary>
    public static string? TypeName(BaseItem item) => item switch
    {
        Movie => "Movie",
        Episode => "Episode",
        Series => "Series",
        _ => null
    };
}
