using Cysharp.Text;
using FishyFlip;
using FishyFlip.Lexicon;
using FishyFlip.Lexicon.App.Bsky.Embed;
using FishyFlip.Lexicon.App.Bsky.Richtext;
using FishyFlip.Models;
using HtmlAgilityPack;
using Mastonet.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using OpenGraphNet;
using static FishyFlip.Constants;

static class AtProtocolExtensions
{
    public static async Task CrossPost(this ATProtocol atProtocol, Status status, IStatusLogStore store, ILogger logger)
    {
        // 画像があったらダウンロードしてBlueskeyにアップロードする
        ATObject? embed = null;
        if (status.MediaAttachments.Any(media => media.Type == "image"))
        {
            var images = new List<Image>();
            foreach (var media in status.MediaAttachments.Where(m => m.Type == "image"))
            {
                var image = await UploadImage(atProtocol, media, logger);
                if (image is null)
                {
                    continue;
                }
                images.Add(image);
            }
            embed = new EmbedImages([.. images]);
        }
        // 動画があったらダウンロードしてBlueskeyにアップロードする
        else if (status.MediaAttachments.ToArray() is [{ Type: "video" or "gifv" } media])
        {
            embed = await UploadVideo(atProtocol, media, logger);
        }
        // それ以外のメディアは未対応
        else
        {
            logger.LogWarning($"Unsupported media type, {status.Id}");
        }

        {
            var (text, facets) = status.GetContentText();
            var rep = await store.GetBlueskyPostAsync(status.InReplyToId);
            if (embed is null && facets.Where(f => f is { Features: [{ Type: Link.RecordType }] }).ToArray() is [{ Features: [Link { Uri: string url }] }])
            {
                try
                {
                    embed = await atProtocol.OpenGraphParser.GenerateEmbedExternal(url).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    logger.LogWarning($"Failed to fetch OGP: {url}");
                }
            }
            var (res, error) = await atProtocol.Feed.CreatePostAsync(text, [.. facets], embed: embed);
            if (res is null)
            {
                logger.LogError($"Failed to post to Blueskey: {error?.StatusCode} {error?.Detail}");
                return;
            }
            await store.AddBlueskyPostAsync(status.Id, new(rep?.Root ?? new(res.Uri!, res.Cid!), new(res.Uri!, res.Cid!)));
            logger.LogInformation($"Posted to Bluesky {res.Uri}");
        }
    }

    private static async Task<Image?> UploadImage(this ATProtocol atProtocol, Attachment media, ILogger logger)
    {
        using var httpClient = new HttpClient();
        var res = await httpClient.GetAsync(media.Url);
        res.EnsureSuccessStatusCode();
        using var stream = await res.Content.ReadAsStreamAsync();
        using var content = new StreamContent(stream);
        content.Headers.ContentLength = res.Content.Headers.ContentLength;
        content.Headers.ContentType = res.Content.Headers.ContentType;
        var (imageRes, error) = await atProtocol.Repo.UploadBlobAsync(content);
        if (imageRes is null)
        {
            logger.LogError($"Failed to upload media: {error?.StatusCode} {error?.Detail}");
            return null;
        }
        return new(imageRes.Blob, media.Description!);
    }

    private static async Task<EmbedVideo?> UploadVideo(this ATProtocol atProtocol, Attachment media, ILogger logger)
    {
        using var httpClient = new HttpClient();
        var res = await httpClient.GetAsync(media.Url);
        res.EnsureSuccessStatusCode();
        using var stream = await res.Content.ReadAsStreamAsync();
        using var content = new StreamContent(stream);
        content.Headers.ContentLength = res.Content.Headers.ContentLength;
        content.Headers.ContentType = res.Content.Headers.ContentType;
        var (videoRes, error) = await atProtocol.Repo.UploadBlobAsync(content);
        if (videoRes is null)
        {
            logger.LogError($"Failed to upload media: {error?.StatusCode} {error?.Detail}");
            return null;
        }
        return new(videoRes.Blob, alt: media.Description!);
    }

    private static (string text, Facet[] facets) GetContentText(this Status status)
    {
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(status.Content);
        var facets = new List<Facet>();
        var builder = ZString.CreateUtf8StringBuilder();
        try
        {
            builder.Build(htmlDoc.DocumentNode, facets);
            return (builder.ToString().Trim(), [.. facets]);
        }
        finally
        {
            builder.Dispose();
        }
    }

    private static Utf8ValueStringBuilder Build(this ref Utf8ValueStringBuilder builder, HtmlNode node, List<Facet> facets)
    {
        switch (node)
        {
            case HtmlTextNode textNode:
                builder.Append(textNode.Text);
                break;
            case HtmlNode when node.Name == "a":
                var href = node.GetAttributeValue("href", string.Empty);
                var start = builder.Length;
                foreach (var child in node.ChildNodes)
                {
                    builder.Build(child, facets);
                }
                facets.Add(Facet.CreateFacetLink(start, builder.Length, href));
                break;
            case HtmlNode when node.Name == "p":
                foreach (var child in node.ChildNodes)
                {
                    builder.Build(child, facets);
                }
                builder.AppendLine();
                break;
            case HtmlNode when node.Name == "br":
                builder.AppendLine();
                break;
            case HtmlNode when node.NodeType == HtmlNodeType.Document || node.Name == "span":
                foreach (var child in node.ChildNodes)
                {
                    builder.Build(child, facets);
                }
                break;
            default:
                builder.Append(node.InnerText());
                break;
        }
        return builder;
    }

    public static void Deconstruct<T>(this Result<T> result, out T? value, out ATError? error)
    {
        value = result.IsT0 ? result.AsT0 : default;
        error = result.IsT1 ? result.AsT1 : default;
    }
}
