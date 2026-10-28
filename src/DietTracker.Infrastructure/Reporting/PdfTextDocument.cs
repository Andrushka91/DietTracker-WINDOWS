using System.Globalization;
using System.Text;

namespace DietTracker.Infrastructure.Reporting;

/// <summary>
/// Minimal, dependency free PDF writer for text documents: title, paragraphs and table-like rows.
/// </summary>
/// <remarks>
/// The application ships without a PDF library, so the report is written with the two standard
/// PostScript fonts (Helvetica and Helvetica-Bold) that every reader has built in - nothing has to be
/// embedded and a full year of meals stays a few kilobytes. Text is emitted with the WinAnsi
/// encoding, which keeps the Latin-1 characters of English and Spanish (including á, ñ, ¿ and ¡)
/// exactly as they are, and reduces a character it does not know to its base letter - so the
/// Romanian "Mic dejun cu brânză" is printed as "Mic dejun cu branza" instead of a row of question
/// marks, and no glyph is ever missing. The document only uses absolute positioning, so the caller
/// decides where a line starts; <see cref="Wrap"/> splits a text that does not fit.
/// </remarks>
public sealed class PdfTextDocument
{
    /// <summary>Width of an A4 page in PDF points (72 per inch).</summary>
    public const double A4Width = 595.28;

    /// <summary>Height of an A4 page in PDF points.</summary>
    public const double A4Height = 841.89;

    private const string RegularFont = "F1";
    private const string BoldFont = "F2";

    /// <summary>
    /// Helvetica advance widths of the printable ASCII range (32..126) in 1/1000 em. Used to wrap
    /// text exactly; anything outside the range (accented letters and so on) is measured as 556.
    /// </summary>
    private static readonly short[] AsciiWidths =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333,
        389, 584, 278, 333, 278, 278, 556, 556, 556, 556,
        556, 556, 556, 556, 556, 556, 278, 278, 584, 584,
        584, 556, 1015, 667, 667, 722, 722, 667, 611, 778,
        722, 278, 500, 667, 556, 833, 722, 778, 667, 778,
        722, 667, 611, 722, 667, 944, 667, 667, 611, 278,
        278, 278, 469, 556, 333, 556, 556, 500, 556, 556,
        278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500,
        500, 334, 260, 334, 584,
    ];

    /// <summary>
    /// The 32 code points of the 0x80..0x9F range of the Windows-1252 (WinAnsi) code page. Those are
    /// the only characters whose WinAnsi byte differs from their Unicode value, so a small table is
    /// enough to encode any string the report can contain.
    /// </summary>
    private static readonly char[] WinAnsiHigh =
    [
        '\u20AC', '\u0081', '\u201A', '\u0192', '\u201E', '\u2026', '\u2020', '\u2021',
        '\u02C6', '\u2030', '\u0160', '\u2039', '\u0152', '\u008D', '\u017D', '\u008F',
        '\u0090', '\u2018', '\u2019', '\u201C', '\u201D', '\u2022', '\u2013', '\u2014',
        '\u02DC', '\u2122', '\u0161', '\u203A', '\u0153', '\u009D', '\u017E', '\u0178',
    ];

    private static readonly Dictionary<char, int> WinAnsiCodes = BuildWinAnsiCodes();

    private readonly List<StringBuilder> _pages = [];
    private readonly string _title;

    /// <summary>Creates an empty document. A page is added by <see cref="AddPage"/>.</summary>
    public PdfTextDocument(string title)
    {
        _title = string.IsNullOrWhiteSpace(title) ? "Diet Tracker" : title.Trim();
    }

    /// <summary>Number of pages added so far.</summary>
    public int PageCount => _pages.Count;

    /// <summary>Appends a page and makes it the current one of <see cref="DrawText"/>.</summary>
    public void AddPage() => _pages.Add(new StringBuilder());

    /// <summary>Writes one line on the last page (see <see cref="DrawTextOnPage"/>).</summary>
    public void DrawText(double x, double y, string text, double fontSize, bool bold = false, double gray = 0d)
        => DrawTextOnPage(_pages.Count - 1, x, y, text, fontSize, bold, gray);

    /// <summary>
    /// Writes one line of text with its baseline at <paramref name="y"/>. The PDF origin is the
    /// bottom left corner of the page, so a larger y is higher on the page.
    /// </summary>
    /// <param name="pageIndex">Zero based page index.</param>
    /// <param name="x">Left edge of the text.</param>
    /// <param name="y">Baseline of the text.</param>
    /// <param name="text">The text; characters outside Latin-1 become "?" (the fonts are not embedded).</param>
    /// <param name="fontSize">Size in points.</param>
    /// <param name="bold">Use Helvetica-Bold instead of Helvetica.</param>
    /// <param name="gray">Ink of the text: 0 is black, 1 is white.</param>
    public void DrawTextOnPage(int pageIndex, double x, double y, string text, double fontSize, bool bold = false, double gray = 0d)
    {
        if (string.IsNullOrEmpty(text) || pageIndex < 0 || pageIndex >= _pages.Count)
        {
            return;
        }

        var content = _pages[pageIndex];
        content.Append(Number(gray)).Append(" g\n");
        content.Append("BT\n/").Append(bold ? BoldFont : RegularFont).Append(' ').Append(Number(fontSize)).Append(" Tf\n");
        content.Append(Number(x)).Append(' ').Append(Number(y)).Append(" Td\n");
        content.Append('(').Append(Encode(text)).Append(") Tj\nET\n");
    }

    /// <summary>Draws a horizontal rule on the last page.</summary>
    public void DrawLine(double x1, double y, double x2, double thickness = 0.6, double gray = 0.75)
    {
        if (_pages.Count == 0)
        {
            return;
        }

        _pages[^1]
            .Append(Number(gray)).Append(" G\n")
            .Append(Number(thickness)).Append(" w\n")
            .Append(Number(x1)).Append(' ').Append(Number(y)).Append(" m\n")
            .Append(Number(x2)).Append(' ').Append(Number(y)).Append(" l\nS\n");
    }

    /// <summary>Width of a text in points, using the Helvetica metrics.</summary>
    public static double MeasureWidth(string text, double fontSize, bool bold = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0d;
        }

        var thousandths = 0d;
        foreach (var character in text)
        {
            thousandths += GlyphWidth(character);
        }

        // Helvetica-Bold is a little wider than Helvetica; the small factor keeps a bold line inside
        // the same margin without a second width table.
        return thousandths / 1000d * fontSize * (bold ? 1.06d : 1d);
    }

    /// <summary>
    /// Splits a text into the lines that fit into <paramref name="maxWidth"/> points. Existing line
    /// breaks are kept, and a word longer than the column is broken instead of overflowing.
    /// </summary>
    public static IReadOnlyList<string> Wrap(string text, double fontSize, bool bold, double maxWidth)
    {
        if (string.IsNullOrWhiteSpace(text) || maxWidth <= 0)
        {
            return [];
        }

        var lines = new List<string>();
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var current = new StringBuilder();
            foreach (var word in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = current.Length == 0 ? word : $"{current} {word}";
                if (MeasureWidth(candidate, fontSize, bold) <= maxWidth)
                {
                    current.Clear().Append(candidate);
                    continue;
                }

                if (current.Length > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }

                // A single word wider than the column is cut where the column ends.
                var rest = word;
                while (MeasureWidth(rest, fontSize, bold) > maxWidth && rest.Length > 1)
                {
                    var take = 1;
                    while (take < rest.Length && MeasureWidth(rest[..(take + 1)], fontSize, bold) <= maxWidth)
                    {
                        take++;
                    }

                    lines.Add(rest[..take]);
                    rest = rest[take..];
                }

                current.Append(rest);
            }

            if (current.Length > 0)
            {
                lines.Add(current.ToString());
            }
        }

        return lines;
    }

    /// <summary>Serializes the document as a PDF 1.4 file.</summary>
    public byte[] ToArray()
    {
        if (_pages.Count == 0)
        {
            AddPage();
        }

        // Page objects and their content streams are interleaved: page i is object 5 + 2i and its
        // content stream is the object right after it.
        var contents = _pages.Select(page => page.ToString()).ToArray();
        var pageIds = contents.Select((_, index) => 5 + (index * 2)).ToArray();

        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', pageIds.Select(id => $"{id} 0 R"))}] /Count {contents.Length} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
        };

        for (var index = 0; index < contents.Length; index++)
        {
            objects.Add(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Number(A4Width)} {Number(A4Height)}] " +
                $"/Resources << /Font << /{RegularFont} 3 0 R /{BoldFont} 4 0 R >> >> " +
                $"/Contents {pageIds[index] + 1} 0 R >>");

            // The length is the byte count of the stream; the content is pure ASCII because every
            // character outside the printable range was written as an octal escape.
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(contents[index])} >>\nstream\n{contents[index]}\nendstream");
        }

        var infoId = objects.Count + 1;
        objects.Add(
            $"<< /Title ({Encode(_title)}) /Producer (Diet Tracker) /Creator (Diet Tracker) " +
            $"/CreationDate (D:{DateTimeOffset.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}) >>");

        using var buffer = new MemoryStream();
        WriteAscii(buffer, "%PDF-1.4\n");

        // Binary marker: tells tools (and transfer software) that the file is not a text file.
        buffer.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]);

        var offsets = new List<long>(objects.Count);
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(buffer.Position);
            WriteAscii(buffer, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var xrefOffset = buffer.Position;

        // Every cross reference entry is exactly 20 bytes, as the format requires.
        WriteAscii(buffer, $"xref\n0 {objects.Count + 1}\n");
        WriteAscii(buffer, "0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            WriteAscii(buffer, offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        WriteAscii(buffer, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Info {infoId} 0 R >>\n");
        WriteAscii(buffer, $"startxref\n{xrefOffset.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");

        return buffer.ToArray();
    }

    private static void WriteAscii(Stream stream, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static double GlyphWidth(char character) =>
        character is >= ' ' and <= '~' ? AsciiWidths[character - ' '] : 556;

    private static Dictionary<char, int> BuildWinAnsiCodes()
    {
        var codes = new Dictionary<char, int>(WinAnsiHigh.Length);
        for (var index = 0; index < WinAnsiHigh.Length; index++)
        {
            codes[WinAnsiHigh[index]] = 0x80 + index;
        }

        return codes;
    }

    /// <summary>
    /// Turns a string into a PDF literal string: the three delimiters are escaped and every
    /// character a built in font cannot show is written as an octal escape of its WinAnsi byte.
    /// </summary>
    private static string Encode(string text)
    {
        var builder = new StringBuilder(text.Length + 8);
        foreach (var character in text)
        {
            foreach (var code in WinAnsiBytes(character))
            {
                switch (code)
                {
                    case '(' or ')' or '\\':
                        builder.Append('\\').Append((char)code);
                        break;
                    case >= ' ' and <= '~':
                        builder.Append((char)code);
                        break;
                    default:
                        builder.Append('\\').Append(Convert.ToString(code, 8).PadLeft(3, '0'));
                        break;
                }
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// WinAnsi bytes of one character. Latin-1 is encoded as it is, the code points of the 0x80..0x9F
    /// range come from the table, and a letter the code page does not contain (the Romanian ș, ț and
    /// ă, for example) is reduced to its base letter so the word stays readable. A character with no
    /// base letter at all becomes "?".
    /// </summary>
    private static IEnumerable<int> WinAnsiBytes(char character)
    {
        if (character is > '\u009F' and <= '\u00FF')
        {
            yield return character;
            yield break;
        }

        if (WinAnsiCodes.TryGetValue(character, out var code))
        {
            yield return code;
            yield break;
        }

        if (character < ' ')
        {
            yield return ' ';
            yield break;
        }

        var mapped = false;
        foreach (var part in character.ToString().Normalize(NormalizationForm.FormD))
        {
            if (part is > '\u009F' and <= '\u00FF')
            {
                yield return part;
                mapped = true;
            }
            else if (part is >= ' ' and <= '~')
            {
                yield return part;
                mapped = true;
            }
        }

        if (!mapped)
        {
            yield return '?';
        }
    }
}
