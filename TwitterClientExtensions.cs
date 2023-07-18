using System.Text;
using HtmlAgilityPack;
using Mastonet;
using Mastonet.Entities;
using Microsoft.Extensions.Logging;
using Tweetinvi;
using Tweetinvi.Models;

static class TwitterClientExtensions
{
    public static async Task CrossPost(this TwitterClient twitter, string id, Status status, ILogger logger)
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
