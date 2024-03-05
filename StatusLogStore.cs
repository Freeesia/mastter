using FishyFlip.Models;

class StatusLogStore : IStatusLogStore
{
    private readonly Dictionary<string, string> mastodonTwitter = new();
    private readonly Dictionary<string, Reply> mastodonBluesky = new();

    public ValueTask AddBlueskyPostAsync(string mastodonId, Reply rep)
    {
        mastodonBluesky.Add(mastodonId, rep);
        return default;
    }

    public ValueTask AddTwitterStatusAsync(string mastodonId, string twitterId)
    {
        mastodonTwitter.Add(mastodonId, twitterId);
        return default;
    }

    public ValueTask<Reply?> GetBlueskyPostAsync(string? mastodonId)
        => new(mastodonBluesky.TryGetValue(mastodonId ?? string.Empty, out var blueskyUri) ? blueskyUri : null);

    public ValueTask<string?> GetTwitterStatusAsync(string? mastodonId)
        => new(mastodonTwitter.TryGetValue(mastodonId ?? string.Empty, out var twitterId) ? twitterId : null);
}

interface IStatusLogStore
{
    /// <summary>
    /// Retrieves the Twitter status ID associated with the specified Mastodon status ID.
    /// </summary>
    /// <param name="mastodonId">The Mastodon status ID.</param>
    /// <returns>The associated Twitter status ID, or null if no association exists.</returns>
    ValueTask<string?> GetTwitterStatusAsync(string? mastodonId);

    /// <summary>
    /// Adds a mapping between a Twitter status ID and a Mastodon status ID.
    /// </summary>
    /// <param name="mastodonId">The Mastodon status ID.</param>
    /// <param name="twitterId">The Twitter status ID.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask AddTwitterStatusAsync(string mastodonId, string twitterId);

    /// <summary>
    /// Retrieves the Bluesky status URI associated with the specified Mastodon status ID.
    /// </summary>
    /// <param name="mastodonId">The Mastodon status ID.</param>
    /// <returns>The associated Twitter status ID, or null if no association exists.</returns>
    ValueTask<Reply?> GetBlueskyPostAsync(string? mastodonId);

    /// <summary>
    /// Adds a mapping between a Mastodon status ID and a Bluesky status URI.
    /// </summary>
    /// <param name="mastodonId">The Mastodon status ID.</param>
    /// <param name="rep">The Bluesky status URI.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask AddBlueskyPostAsync(string mastodonId, Reply rep);
}