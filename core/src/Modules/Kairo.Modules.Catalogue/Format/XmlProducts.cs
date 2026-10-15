using System.Xml;
using System.Xml.Linq;

namespace Kairo.Modules.Catalogue.Format;

internal static class XmlProducts
{
    private static readonly XNamespace Google = "http://base.google.com/ns/1.0";
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
    };

    public static List<RawRow> Read(byte[] content, int start)
    {
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(content, start, content.Length - start), Settings);
            reader.MoveToContent();
            var isAtom = reader.LocalName == "feed" && reader.NamespaceURI == Atom.NamespaceName;
            if (!isAtom && reader.LocalName != "rss")
            {
                throw CatalogueFileException.Unreadable();
            }

            var itemName = isAtom ? "entry" : "item";
            var rows = new List<RawRow>();
            reader.Read();
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == itemName)
                {
                    var item = (XElement)XNode.ReadFrom(reader);
                    CatalogueFile.CheckCount(rows.Count + 1);
                    rows.Add(RowOf(rows.Count + 1, item, isAtom));
                }
                else
                {
                    reader.Read();
                }
            }

            return rows;
        }
        catch (XmlException)
        {
            throw CatalogueFileException.Unreadable();
        }
    }

    private static RawRow RowOf(int number, XElement item, bool isAtom)
    {
        var row = new RawRow(number);
        foreach (var element in item.Elements().Where(element => element.Name.Namespace == Google))
        {
            row.Add(Attributes.Normalise(element.Name.LocalName), element.Value);
        }

        var plain = isAtom ? Atom : XNamespace.None;
        AddMissing(row, Attributes.Title, item.Element(plain + "title")?.Value);
        AddMissing(
            row,
            Attributes.Link,
            isAtom
                ? item.Elements(Atom + "link").Select(link => (string?)link.Attribute("href")).FirstOrDefault(href => href is not null)
                : item.Element(plain + "link")?.Value);
        AddMissing(row, Attributes.Description, item.Element(plain + (isAtom ? "summary" : "description"))?.Value);
        return row;
    }

    private static void AddMissing(RawRow row, string attribute, string? value)
    {
        if (row.First(attribute) is null && value is not null)
        {
            row.Add(attribute, value);
        }
    }
}
