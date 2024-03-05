using FishyFlip;
using FishyFlip.Models;
using Mastonet.Entities;
using Microsoft.Extensions.Logging;

static class AtProtocolExtensions
{
    public static async Task CrossPost(this ATProtocol atProtocol, Status status, IStatusLogStore store, ILogger logger)
    {
        // 画像があったらダウンロードしてBlueskeyにアップロードする
        var medias = new List<ImageEmbed>();
        foreach (var media in status.MediaAttachments)
        {
            using var httpClient = new HttpClient();
            using var res = await httpClient.GetAsync(media.Url);
            res.EnsureSuccessStatusCode();
            using var stream = await res.Content.ReadAsStreamAsync();
            using var content = new StreamContent(stream);
            content.Headers.ContentType = res.Content.Headers.ContentType;
            var (mediaRes, error) = await atProtocol.Repo.UploadBlobAsync(content);
            if (mediaRes is null)
            {
                logger.LogError($"Failed to upload media: {error?.StatusCode} {error?.Detail}");
                continue;
            }
            medias.Add(new(mediaRes.Blob.ToImage(), media.Description ?? string.Empty));
        }

        {
            var text = status.GetContentText();
            var rep = await store.GetBlueskyPostAsync(status.InReplyToId);
            var embed = medias.Any() ? new ImagesEmbed(medias.ToArray()) : null;
            var (res, error) = await atProtocol.Repo.CreatePostAsync(text, rep, embed: embed);
            if (res is null)
            {
                logger.LogError($"Failed to post to Blueskey: {error?.StatusCode} {error?.Detail}");
                return;
            }
            await store.AddBlueskyPostAsync(status.Id, new(rep?.Root ?? new(res.Cid!, res.Uri!), new(res.Cid!, res.Uri!)));
            logger.LogInformation($"Posted to Bluesky {res.Uri}");
        }
    }

    public static void Deconstruct<T>(this Result<T> result, out T? value, out ATError? error)
    {
        value = result.IsT0 ? result.AsT0 : default;
        error = result.IsT1 ? result.AsT1 : default;
    }
}
