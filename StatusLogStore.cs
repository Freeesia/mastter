

class StatusLogStore : IStatusLogStore
{
    private readonly Dictionary<string, string> statusPairs = new();
    public ValueTask AddStatusAsync(string mastodonId, string twitterId)
    {
        statusPairs.Add(mastodonId, twitterId);
        return default;
    }

    public ValueTask<string?> GetStatusAsync(string? mastodonId)
        => new(statusPairs.TryGetValue(mastodonId ?? string.Empty, out var twitterId) ? twitterId : null);
}

interface IStatusLogStore
{
    /// <summary>
    /// Retrieves the Twitter status ID associated with the specified Mastodon status ID.
    /// </summary>
    /// <param name="mastodonId">The Mastodon status ID.</param>
    /// <returns>The associated Twitter status ID, or null if no association exists.</returns>
    ValueTask<string?> GetStatusAsync(string? mastodonId);

    /// <summary>
    /// Adds a mapping between a Twitter status ID and a Mastodon status ID.
    /// </summary>
    /// <param name="twitterId">The Twitter status ID.</param>
    /// <param name="mastodonId">The Mastodon status ID.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask AddStatusAsync(string twitterId, string mastodonId);
}