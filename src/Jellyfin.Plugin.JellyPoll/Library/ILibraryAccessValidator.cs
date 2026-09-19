using MediaBrowser.Controller.Entities;
using Jellyfin.Database.Implementations.Entities;

namespace Jellyfin.Plugin.JellyPoll.Library;

/// <summary>Library resolution/access contract (extracted for M4 testability).</summary>
public interface ILibraryAccessValidator
{
    /// <summary>Resolves a base item by id, or null when it no longer exists.</summary>
    BaseItem? ResolveItem(Guid itemId);

    /// <summary>True when the item exists and is visible to the user.</summary>
    bool CanAccess(User user, Guid itemId);

    /// <summary>Suggestion type name for a resolved item, or null when type unsupported.</summary>
    string? GetTypeName(BaseItem item);

    /// <summary>
    /// Movie children of a collection that the user can access (ordered by sort name).
    /// Empty for non-collection ids.
    /// </summary>
    IReadOnlyList<BaseItem> GetCollectionMovies(User user, Guid collectionId);

    /// <summary>Collections (BoxSets) visible to the user, ordered by sort name.</summary>
    IReadOnlyList<BaseItem> ListCollections(User user);
}
