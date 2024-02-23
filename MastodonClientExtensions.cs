using HtmlAgilityPack;
using Mastonet.Entities;

static class MastodonClientExtensions
{
    public static string GetContentText(this Status status)
    {
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(status.Content);
        return htmlDoc.DocumentNode.InnerText();
    }
}