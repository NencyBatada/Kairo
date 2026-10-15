using System.IO.Compression;

namespace Kairo.Modules.Catalogue.Format;

internal static class CatalogueFile
{
    public const int MaxBytes = 50 * 1024 * 1024;
    public const int MaxProducts = 50_000;

    public static ParsedCatalogue Parse(byte[] body, string currency)
    {
        var content = Unpack(body);
        var textStart = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF ? 3 : 0;
        var first = textStart;
        while (first < content.Length && content[first] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            first++;
        }

        if (first == content.Length)
        {
            throw CatalogueFileException.Unreadable();
        }

        var rows = content[first] == (byte)'<' ? XmlProducts.Read(content, first) : DelimitedText.Read(content, textStart);
        return ProductRows.Check(rows, currency);
    }

    public static void CheckCount(int count)
    {
        if (count > MaxProducts)
        {
            throw CatalogueFileException.TooManyProducts();
        }
    }

    private static byte[] Unpack(byte[] body)
    {
        if (body.Length > MaxBytes)
        {
            throw CatalogueFileException.TooLarge();
        }

        if (body.Length < 2 || body[0] != 0x1F || body[1] != 0x8B)
        {
            return body;
        }

        try
        {
            using var input = new GZipStream(new MemoryStream(body), CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = input.Read(buffer)) > 0)
            {
                if (output.Length + read > MaxBytes)
                {
                    throw CatalogueFileException.TooLarge();
                }

                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            throw CatalogueFileException.Unreadable();
        }
    }
}
