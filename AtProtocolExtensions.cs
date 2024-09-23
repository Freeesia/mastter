using Cysharp.Text;
using FishyFlip;
using FishyFlip.Models;
using HtmlAgilityPack;
using Mastonet.Entities;
using Microsoft.Extensions.Logging;
using OpenGraphNet;
using static FishyFlip.Constants;

static class AtProtocolExtensions
{
    public static async Task CrossPost(this ATProtocol atProtocol, Status status, IStatusLogStore store, ILogger logger)
    {
        // 画像があったらダウンロードしてBlueskeyにアップロードする
        Embed? embed = null;
        if (status.MediaAttachments.Any(media => media.Type == "image"))
        {
            var images = new List<ImageEmbed>();
            foreach (var media in status.MediaAttachments.Where(m => m.Type == "image"))
            {
                var image = await UploadImage(atProtocol, media.Url, logger);
                if (image is null)
                {
                    continue;
                }
                images.Add(new(image, media.Description ?? string.Empty));
            }
            embed = new ImagesEmbed([.. images]);
        }
        // 動画があったらダウンロードしてBlueskeyにアップロードする
        else if (status.MediaAttachments.ToArray() is [{ Type: "video" or "gifv" } media])
        {
            var video = await UploadVideo(atProtocol, media.Url, logger);
            if (video is not null)
            {
                embed = new VideoEmbed(video);
            }
        }
        // それ以外のメディアは未対応
        else
        {
            logger.LogWarning($"Unsupported media type, {status.Id}");
        }

        {
            var (text, facets) = status.GetContentText();
            var rep = await store.GetBlueskyPostAsync(status.InReplyToId);
            if (embed is null && facets.Where(f => f is { Features: [{ Type: FacetTypes.Link }] }).ToArray() is [{ Features: [{ Uri: string url }] }])
            {
                try
                {
                    var ogp = await OpenGraph.ParseUrlAsync(url).ConfigureAwait(false);
                    var image = ogp.Image is null ? null : await UploadImage(atProtocol, ogp.Image.AbsoluteUri, logger);
                    var desc = ogp.Metadata.TryGetValue("og:description", out var d) ? d.FirstOrDefault()?.Value : string.Empty;
                    embed = new ExternalEmbed(new External(image, ogp.Title, desc, ogp.Url?.AbsoluteUri ?? url));
                }
                catch (Exception)
                {
                    logger.LogWarning($"Failed to fetch OGP: {url}");
                }
            }
            var (res, error) = await atProtocol.Repo.CreatePostAsync(text, facets, embed);
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

    private static async Task<Video?> UploadVideo(this ATProtocol atProtocol, string mediaUrl, ILogger logger)
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
        return imageRes.Blob.ToVideo();
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
                var href = node.GetAttributeValue("href", null);
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
