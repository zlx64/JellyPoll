namespace Jellyfin.Plugin.JellyPoll.Data;

/// <summary>Base for domain errors the API layer maps to HTTP status codes (doc 04 §2).</summary>
public abstract class JellyPollException(string message) : Exception(message);

public sealed class PollNotFoundException() : JellyPollException("Poll not found");

public sealed class SuggestionNotFoundException() : JellyPollException("Suggestion not found");

public sealed class PollClosedException() : JellyPollException("Poll is closed");

public sealed class DuplicateSuggestionException() : JellyPollException("This item has already been suggested");

public sealed class ItemTypeNotAllowedException(string itemType)
    : JellyPollException($"Item type '{itemType}' is not allowed in this poll");

public sealed class AccessDeniedException() : JellyPollException("You do not have access to this item");

public sealed class SuggestionLimitReachedException(int limit)
    : JellyPollException($"Suggestion limit reached ({limit} per user)");

public sealed class ValidationException(string message) : JellyPollException(message);

public sealed class StorageUnavailableException(string message) : JellyPollException(message);
