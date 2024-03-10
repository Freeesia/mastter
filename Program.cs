using FishyFlip;
using Mastonet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tweetinvi;

var app = ConsoleApp.CreateBuilder(args)
    .ConfigureServices((ctx, services)
        => services.Configure<ConsoleOptions>(ctx.Configuration)
            .AddSingleton<IStatusLogStore, StatusLogStore>())
    .Build();
app.AddRootCommand(Run);
app.AddCommand("post-twitter", PostToTwitter);
app.AddCommand("post-bluesky", PostToBluesky);
await app.RunAsync();

static async Task Run(ILogger<Program> logger, IOptions<ConsoleOptions> options, IStatusLogStore store)
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

    var atProtocolBuilder = new ATProtocolBuilder()
        .EnableAutoRenewSession(true)
        .WithLogger(logger);
    var atProtocol = atProtocolBuilder.Build();
    var (atProtocolMe, error) = await atProtocol.Server.CreateSessionAsync(value.BlueskyIdentifier, value.BlueskyAppPassword);
    logger.LogInformation($"Logged in Bluesky as {atProtocolMe?.Did} (@{atProtocolMe?.Handle})");

    var mastodon = new MastodonClient(value.MastodonUrl, value.MastodonToken);
    var mastodonMe = await mastodon.GetCurrentUser();
    logger.LogInformation($"Logged in Mastodon as {mastodonMe.DisplayName} (@{mastodonMe.UserName})");
    var ust = mastodon.GetUserStreaming();
    ust.OnUpdate += async (sender, e) =>
    {
        var status = e.Status;
        // 自分じゃない投稿は無視する
        if (status.Account.Id != mastodonMe.Id ||
            // 返信ではない投稿もしくは自分への返信だけを対象にする
            !(status.InReplyToAccountId == mastodonMe.Id || status.InReplyToAccountId == null) ||
            // メンションされてたら無視する
            status.Mentions.Any() ||
            // 公開範囲が非公開だったら無視する
            status.Visibility != Visibility.Public)
        {
            return;
        }
        logger.LogInformation($"Posted from Mastodon {status.Id}");
        await twitter.CrossPost(status, store, logger);
        await atProtocol.CrossPost(status, store, logger);
    };
    await ust.Start();
    // await Post(logger, mastodonMe.Id, (await mastodon.GetAccountStatuses(mastodonMe.Id)).First(), twitter);
}

static async Task PostToTwitter(ILogger<Program> logger, IOptions<ConsoleOptions> options, [Option(0)]string id)
{
    var value = options.Value;
    var twitter = new TwitterClient(value.TwitterConsumerKey, value.TwitterConsumerSecret, value.TwitterAccessToken, value.TwitterAccessTokenSecret);
    var twitterMe = await twitter.Users.GetAuthenticatedUserAsync();
    logger.LogInformation($"Logged in Twitter as {twitterMe.Name} (@{twitterMe.ScreenName})");
    var mastodon = new MastodonClient(value.MastodonUrl, value.MastodonToken);
    var mastodonMe = await mastodon.GetCurrentUser();
    logger.LogInformation($"Logged in Mastodon as {mastodonMe.DisplayName} (@{mastodonMe.UserName})");

    var status = await mastodon.GetStatus(id);
    await twitter.CrossPost(status, new StatusLogStore(), logger);
}

static async Task PostToBluesky(ILogger<Program> logger, IOptions<ConsoleOptions> options, [Option(0)]string id)
{
    var value = options.Value;
    var atProtocolBuilder = new ATProtocolBuilder()
        .EnableAutoRenewSession(true)
        .WithLogger(logger);
    var atProtocol = atProtocolBuilder.Build();
    var (atProtocolMe, error) = await atProtocol.Server.CreateSessionAsync(value.BlueskyIdentifier, value.BlueskyAppPassword);
    logger.LogInformation($"Logged in Bluesky as {atProtocolMe?.Did} (@{atProtocolMe?.Handle})");
    var mastodon = new MastodonClient(value.MastodonUrl, value.MastodonToken);
    var mastodonMe = await mastodon.GetCurrentUser();
    logger.LogInformation($"Logged in Mastodon as {mastodonMe.DisplayName} (@{mastodonMe.UserName}");

    var status = await mastodon.GetStatus(id);
    await atProtocol.CrossPost(status, new StatusLogStore(), logger);
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
    public required string BlueskyIdentifier { get; init; }
    public required string BlueskyAppPassword { get; init; }
}
