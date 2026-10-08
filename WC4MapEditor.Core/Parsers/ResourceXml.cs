using System.Xml;

namespace WC4MapEditor.Core.Parsers;

public static class ResourceXml
{
    public static XmlDocument ParseFragments(string text)
    {
        text = text.TrimStart('\uFEFF').Trim();
        if (text.StartsWith("<?xml", StringComparison.Ordinal))
        {
            int end = text.IndexOf("?>", StringComparison.Ordinal);
            if (end < 0) throw new InvalidDataException("XML 声明未闭合。");
            text = text[(end + 2)..];
        }
        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader("<Resource>" + text + "</Resource>"),
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        document.Load(reader);
        return document;
    }
}
