using System.Text;
using HtmlAgilityPack;
using Mastonet;
using Mastonet.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Tweetinvi;
using Tweetinvi.Models;

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
    ust.OnUpdate += async (sender, e) => await Post(logger, mastodonMe.Id, e.Status, twitter);
    await ust.Start();
    // await Post(logger, mastodonMe.Id, (await mastodon.GetAccountStatuses(mastodonMe.Id)).First(), twitter);
}

static async Task Post(ILogger logger, string id, Status status, TwitterClient twitter)
{
    if (status.Account.Id != id ||
        status.InReplyToAccountId != null ||
        status.InReplyToId != null ||
        status.Mentions.Any() ||
        status.Visibility != Visibility.Public)
    {
        return;
    }
    logger.LogInformation($"Posted from Mastodon {status.Id}");
    // 画像があったらダウンロードしてTwitterにアップロードする
    var medias = new List<string>();
    foreach (var media in status.MediaAttachments)
    {
        using var httpClient = new HttpClient();
        var mediaBytes = await httpClient.GetByteArrayAsync(media.Url);
        var mediaRes = await twitter.Upload.UploadBinaryAsync(mediaBytes);
        medias.Add(mediaRes.UploadedMediaInfo.MediaIdStr);
    }
    var htmlDoc = new HtmlDocument();
    htmlDoc.LoadHtml(status.Content);
    var text = htmlDoc.DocumentNode.InnerText();
    var res = await twitter.PostStatusAsync(text, medias);
    logger.LogInformation($"Posted to Twitter {res.Id}");
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
static class TwitterClientExtensions
{
    public static async Task<TweetV2Response> PostStatusAsync(this TwitterClient twitter, string text, IReadOnlyList<string> mediaIds)
    {
        var tweetParams = twitter.Json.Serialize(new TweetV2(text, mediaIds.Any() ? new(mediaIds) : null));
        var res = await twitter.Execute.AdvanceRequestAsync<TwitterResponse<TweetV2Response>>(
                (ITwitterRequest request) =>
                {
                    // Technically this implements IDisposable,
                    // but if we wrap this in a using statement,
                    // we get ObjectDisposedExceptions,
                    // even if we create this in the scope of PostTweet.
                    //
                    // However, it *looks* like this is fine.  It looks
                    // like Microsoft's HTTP stuff will call
                    // dispose on requests for us (responses may be another story).
                    // See also: https://stackoverflow.com/questions/69029065/does-stringcontent-get-disposed-with-httpresponsemessage
                    var content = new StringContent(tweetParams, Encoding.UTF8, "application/json");

                    request.Query.Url = "https://api.twitter.com/2/tweets";
                    request.Query.HttpMethod = Tweetinvi.Models.HttpMethod.POST;
                    request.Query.HttpContent = content;
                }
            );
        return res.Model.Data;
    }
}
static class HtmlNodeExtensions
{
    public static string InnerText(this HtmlNode node)
    {
        var sb = new StringBuilder();
        node.InnerTextCore(sb);
        return sb.ToString();
    }

    private static void InnerTextCore(this HtmlNode node, StringBuilder sb)
    {
        if (node.HasChildNodes)
        {
            foreach (var child in node.ChildNodes)
            {
                child.InnerTextCore(sb);
            }
        }
        else if (node.NodeType == HtmlNodeType.Text)
        {
            sb.Append(node.InnerText);
        }
        else if (node.NodeType == HtmlNodeType.Element && node.Name == "br")
        {
            sb.AppendLine();
        }
    }
}
