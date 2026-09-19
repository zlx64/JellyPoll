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
public sealed class LibraryAccessValidator : ILibraryAccessValidator
{
    private readonly ILibraryManager _libraryManager;

    public LibraryAccessValidator(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <summary>Resolves a base item by id, or null when it no longer exists.
    /// Guid.Empty is rejected by the server with ArgumentException — treat as missing.</summary>
    public BaseItem? ResolveItem(Guid itemId)
        => itemId == Guid.Empty ? null : _libraryManager.GetItemById(itemId);

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

    /// <summary>Collections (BoxSets) visible to the user, ordered by sort name.
    /// Empirically on JF 12.1 (verified live): CollapseBoxSetItems=false EXPANDS boxsets
    /// (members surface, the BoxSet items disappear) — the null-default in the HTTP path
    /// resolves to the mode that shows BoxSets. Set it explicitly.</summary>
    public IReadOnlyList<BaseItem> ListCollections(User user)
    {
        var query = new InternalItemsQuery(user)
        {
            Recursive = true,
            CollapseBoxSetItems = true
        };
        var root = _libraryManager.GetUserRootFolder();
        return root.GetItems(query)
            .Items
            .OfType<BoxSet>()
            .OrderBy(b => b.SortName ?? b.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Suggestion type name for a resolved item, or null when type unsupported.</summary>
    public string? GetTypeName(BaseItem item) => item switch
    {
        Movie => "Movie",
        Episode => "Episode",
        Series => "Series",
        BoxSet => "Collection",
        _ => null
    };

    /// <summary>Movie children of a collection the user can access, ordered by sort name.</summary>
    public IReadOnlyList<BaseItem> GetCollectionMovies(User user, Guid collectionId)
    {
        if (collectionId == Guid.Empty || ResolveItem(collectionId) is not BoxSet box)
        {
            return Array.Empty<BaseItem>();
        }

        // BoxSet members are linked items (not folder children); access-filter each.
        return box.GetLinkedChildren()
            .OfType<Movie>()
            .Where(m => CanAccess(user, m.Id))
            .OrderBy(m => m.SortName ?? m.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
