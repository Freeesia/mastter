using Mastonet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Tweetinvi;

var app = ConsoleApp.CreateBuilder(args)
    .ConfigureServices((ctx, services) => services.Configure<ConsoleOptions>(ctx.Configuration))
    .Build();
app.AddRootCommand(Run);
await app.RunAsync();

static async Task Run(ILogger<Program> logger, IOptions<ConsoleOptions> options)
{
    var value = options.Value;
    var twitter = new TwitterClient(value.TwitterConsumerKey, value.TwitterConsumerSecret, value.TwitterBearerToken);
    if (value is { TwitterAccessToken: null })
    {
        var authenticationRequest = await twitter.Auth.RequestAuthenticationUrlAsync();
        logger.LogInformation($"Please visit {authenticationRequest.AuthorizationURL} and enter the PIN");
        var pin = Console.ReadLine();
        var userCredentials = await twitter.Auth.RequestCredentialsFromVerifierCodeAsync(pin, authenticationRequest);
        logger.LogInformation($"Twitter access token: {userCredentials.AccessToken}, secret: {userCredentials.AccessTokenSecret}");
        twitter = new(userCredentials);
    }
    else
    {
        twitter = new(value.TwitterConsumerKey, value.TwitterConsumerSecret, value.TwitterAccessToken, value.TwitterAccessTokenSecret);
    }
    var twitterMe = await twitter.Users.GetAuthenticatedUserAsync();
    logger.LogInformation($"Logged in Twitter as {twitterMe.Name} (@{twitterMe.ScreenName})");

    var mastodon = new MastodonClient(value.MastodonUrl, value.MastodonToken);
    var mastodonMe = await mastodon.GetCurrentUser();
    logger.LogInformation($"Logged in Mastodon as {mastodonMe.DisplayName} (@{mastodonMe.UserName})");
    var ust = mastodon.GetUserStreaming();
    ust.OnUpdate += async (sender, e) => await twitter.CrossPost(mastodonMe.Id, e.Status, logger);
    await ust.Start();
    // await Post(logger, mastodonMe.Id, (await mastodon.GetAccountStatuses(mastodonMe.Id)).First(), twitter);
}

record ConsoleOptions
{
    public required string MastodonUrl { get; init; }
    public required string MastodonToken { get; init; }
    public required string TwitterConsumerKey { get; init; }
    public required string TwitterConsumerSecret { get; init; }
    public required string TwitterBearerToken { get; init; }
    public string? TwitterAccessToken { get; init; }
    public string? TwitterAccessTokenSecret { get; init; }
}
record TweetV2(
    [property: JsonProperty("text")] string Text,
    [property: JsonProperty("media", NullValueHandling = NullValueHandling.Ignore)] MediaV2? Media);
record MediaV2([property: JsonProperty("media_ids")] IReadOnlyList<string> MediaIds);
record TwitterResponse<T>([property: JsonProperty("data")] T Data);
record TweetV2Response(
    [property: JsonProperty("id")] string Id,
    [property: JsonProperty("text")] string Text,
    [property: JsonProperty("edit_history_tweet_ids")] IReadOnlyList<string> EditHistoryTweetIds);
