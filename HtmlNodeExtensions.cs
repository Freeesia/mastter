using System.Text;
using HtmlAgilityPack;

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
