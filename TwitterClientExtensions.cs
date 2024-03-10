using System.Text;
using Mastonet;
using Mastonet.Entities;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tweetinvi;

static class TwitterClientExtensions
{
    public static async Task CrossPost(this TwitterClient twitter, Status status, IStatusLogStore store, ILogger logger)
    {
        // 画像があったらダウンロードしてTwitterにアップロードする
        var medias = new List<string>();
        foreach (var media in status.MediaAttachments)
        {
            using var httpClient = new HttpClient();
            var mediaBytes = await httpClient.GetByteArrayAsync(media.Url);
            var mediaRes = media.Type switch
            {
                "image" => await twitter.Upload.UploadTweetImageAsync(mediaBytes),
                "video" => await twitter.Upload.UploadTweetVideoAsync(mediaBytes),
                _ => throw new NotSupportedException($"Unsupported media type: {media.Type}")
            };
            medias.Add(mediaRes.UploadedMediaInfo.MediaIdStr);
        }
        var text = status.GetContentText();
        var repId = await store.GetTwitterStatusAsync(status.InReplyToId);
        var res = await twitter.PostStatusAsync(text, medias, repId);
        await store.AddTwitterStatusAsync(status.Id, res.Id);
        logger.LogInformation($"Posted to Twitter {res.Id}");
    }

    public static async Task<TweetV2Response> PostStatusAsync(this TwitterClient twitter, string text, IReadOnlyList<string> mediaIds, string? inReplyToStatusId)
    {
        var tweetParams = twitter.Json.Serialize(
            new TweetV2(
                text,
                mediaIds.Any() ? new(mediaIds) : null,
                string.IsNullOrEmpty(inReplyToStatusId) ? null : new(inReplyToStatusId)));
        var res = await twitter.Execute.AdvanceRequestAsync<TwitterResponse<TweetV2Response>>(
            request =>
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
            });
        return res.Model.Data;
    }

    record TweetV2(
        [property: JsonProperty("text")] string Text,
        [property: JsonProperty("media", NullValueHandling = NullValueHandling.Ignore)] MediaV2? Media,
        [property: JsonProperty("reply", NullValueHandling = NullValueHandling.Ignore)] ReplyV2? Reply);
    record MediaV2([property: JsonProperty("media_ids")] IReadOnlyList<string> MediaIds);
    record ReplyV2([property: JsonProperty("in_reply_to_tweet_id")] string InReplyToTweetId);
    record TwitterResponse<T>([property: JsonProperty("data")] T Data);
}

record TweetV2Response(
    [property: JsonProperty("id")] string Id,
    [property: JsonProperty("text")] string Text,
    [property: JsonProperty("edit_history_tweet_ids")] IReadOnlyList<string> EditHistoryTweetIds);
