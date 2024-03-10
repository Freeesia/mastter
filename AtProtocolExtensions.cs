using FishyFlip;
using FishyFlip.Models;
using Mastonet.Entities;
using Microsoft.Extensions.Logging;

static class AtProtocolExtensions
{
    public static async Task CrossPost(this ATProtocol atProtocol, Status status, IStatusLogStore store, ILogger logger)
    {
        // 画像があったらダウンロードしてBlueskeyにアップロードする
        Embed? embed = null;
        if (status.MediaAttachments.All(media => media.Type == "image"))
        {
            var images = new List<ImageEmbed>();
            foreach (var media in status.MediaAttachments)
            {
                var image = await UploadImage(atProtocol, media.Url, logger);
                if (image is null)
                {
                    continue;
                }
                images.Add(new(image, media.Description ?? string.Empty));
            }
            embed = new ImagesEmbed(images.ToArray());
        }
        // 動画があったらプレビュー画像をダウンロードしてBlueskeyにアップロードする
        else if (status.MediaAttachments.ToArray() is [{ Type: "video" } video])
        {
            var image = await UploadImage(atProtocol, video.PreviewUrl, logger);
            embed = new ExternalEmbed(new External(image, "", video.Description, status.Url), "app.bsky.embed.external");
        }
        // それ以外のメディアは未対応
        else
        {
            logger.LogWarning($"Unsupported media type, {status.Id}");
        }

        {
            var text = status.GetContentText();
            var rep = await store.GetBlueskyPostAsync(status.InReplyToId);
            var (res, error) = await atProtocol.Repo.CreatePostAsync(text, embed: embed);
            if (res is null)
            {
                logger.LogError($"Failed to post to Blueskey: {error?.StatusCode} {error?.Detail}");
                return;
            }
            await store.AddBlueskyPostAsync(status.Id, new(rep?.Root ?? new(res.Cid!, res.Uri!), new(res.Cid!, res.Uri!)));
            logger.LogInformation($"Posted to Bluesky {res.Uri}");
        }
    }

    private static async Task<Image?> UploadImage(this ATProtocol atProtocol, string mediaUrl, ILogger logger)
    {
        using var httpClient = new HttpClient();
        var res = await httpClient.GetAsync(mediaUrl);
        res.EnsureSuccessStatusCode();
        using var stream = await res.Content.ReadAsStreamAsync();
        using var content = new StreamContent(stream);
        content.Headers.ContentType = res.Content.Headers.ContentType;
        var (imageRes, error) = await atProtocol.Repo.UploadBlobAsync(content);
        if (imageRes is null)
        {
            logger.LogError($"Failed to upload media: {error?.StatusCode} {error?.Detail}");
            return null;
        }
        return imageRes.Blob.ToImage();
    }

    public static void Deconstruct<T>(this Result<T> result, out T? value, out FishyFlip.Models.Error? error)
    {
        value = result.IsT0 ? result.AsT0 : default;
        error = result.IsT1 ? result.AsT1 : default;
    }
}
