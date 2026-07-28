using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace RagPipeline.Services;

public record ExtractedBlock(int PageNumber, string Content, bool IsTable);
public record ExtractedImage(int PageNumber, byte[] Bytes, string Format, double WidthPoints, double HeightPoints);

public class PdfExtractionService
{
    // Skip tiny embedded images (icons, bullets, logos in headers) - not diagrams worth describing.
    private const double MinImageDimensionPoints = 80;

    // Table-detection tuning. These are heuristics, not exact science - adjust if your
    // PDFs' tables are consistently mis-detected (see README for guidance).
    private const double RowClusterToleranceY = 3.0;      // words within this many points vertically = same row
    private const double ColumnGapMultiplier = 2.5;       // gap > (avg glyph width * this) = new column
    private const int MinRowsForTable = 3;                // fewer consecutive aligned rows = probably not a table
    private const int MinColumnsForTable = 2;

    /// <summary>
    /// Extracts text and tables page by page. Tables are detected via a word-position
    /// heuristic (row clustering + gap-based column splitting) since PdfPig has no
    /// built-in table extraction - it only exposes word/letter bounding boxes.
    /// For complex tables (merged cells, irregular layouts), consider swapping this
    /// for Azure AI Document Intelligence instead; see README.
    /// </summary>
    public (int PageCount, List<ExtractedBlock> Blocks) Extract(string pdfPath)
    {
        var blocks = new List<ExtractedBlock>();

        using var document = PdfDocument.Open(pdfPath);

        foreach (Page page in document.GetPages())
        {
            var words = page.GetWords().OrderByDescending(w => w.BoundingBox.Top).ToList();
            if (words.Count == 0) continue;

            var rows = ClusterIntoRows(words);
            var (tableRowGroups, nonTableRows) = SplitTableRowsFromText(rows);

            foreach (var tableRows in tableRowGroups)
            {
                var markdown = RenderRowsAsMarkdownTable(tableRows);
                if (!string.IsNullOrWhiteSpace(markdown))
                    blocks.Add(new ExtractedBlock(page.Number, markdown, IsTable: true));
            }

            var plainText = string.Join("\n", nonTableRows.Select(r => string.Join(' ', r.Select(w => w.Text))));
            if (!string.IsNullOrWhiteSpace(plainText))
                blocks.Add(new ExtractedBlock(page.Number, plainText, IsTable: false));
        }

        return (document.NumberOfPages, blocks);
    }

    /// <summary>
    /// Extracts embedded raster images (charts, graphs, photos, diagrams) so they
    /// can be sent to a vision model for description.
    /// </summary>
    public List<ExtractedImage> ExtractImages(string pdfPath)
    {
        var images = new List<ExtractedImage>();

        using var document = PdfDocument.Open(pdfPath);

        foreach (Page page in document.GetPages())
        {
            foreach (IPdfImage image in page.GetImages())
            {
                if (image.Bounds.Width < MinImageDimensionPoints || image.Bounds.Height < MinImageDimensionPoints)
                    continue; // too small to be a meaningful chart/diagram

                byte[]? bytes = null;
                string format = "png";

                if (image.TryGetPng(out var pngBytes))
                {
                    bytes = pngBytes;
                }
                else if (image.RawBytes is { Length: > 0 })
                {
                    bytes = image.RawBytes.ToArray();
                    format = "jpeg";
                }

                if (bytes is null) continue;

                images.Add(new ExtractedImage(page.Number, bytes, format, image.Bounds.Width, image.Bounds.Height));
            }
        }

        return images;
    }

    /// <summary>Groups words on a page into text rows by vertical position.</summary>
    private static List<List<Word>> ClusterIntoRows(List<Word> wordsTopToBottom)
    {
        var rows = new List<List<Word>>();

        foreach (var word in wordsTopToBottom)
        {
            var wordTop = word.BoundingBox.Top;
            var row = rows.FirstOrDefault(r => Math.Abs(r[0].BoundingBox.Top - wordTop) <= RowClusterToleranceY);

            if (row is null)
            {
                rows.Add(new List<Word> { word });
            }
            else
            {
                row.Add(word);
            }
        }

        // Sort each row's words left-to-right.
        foreach (var row in rows)
            row.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));

        return rows;
    }

    /// <summary>
    /// Splits a page's rows into (probable table blocks) vs (everything else).
    /// A run of consecutive rows counts as a table when each row splits into
    /// roughly the same number of gap-separated columns (>= MinColumnsForTable),
    /// for at least MinRowsForTable rows in a row.
    /// </summary>
    private static (List<List<List<Word>>> TableGroups, List<List<Word>> TextRows) SplitTableRowsFromText(
        List<List<Word>> rows)
    {
        var tableGroups = new List<List<List<Word>>>();
        var textRows = new List<List<Word>>();

        var rowColumnCounts = rows.Select(CountColumns).ToList();

        int i = 0;
        while (i < rows.Count)
        {
            if (rowColumnCounts[i] >= MinColumnsForTable)
            {
                int start = i;
                int expectedColumns = rowColumnCounts[i];

                while (i < rows.Count && rowColumnCounts[i] == expectedColumns)
                    i++;

                int runLength = i - start;
                if (runLength >= MinRowsForTable)
                {
                    tableGroups.Add(rows.GetRange(start, runLength));
                }
                else
                {
                    textRows.AddRange(rows.GetRange(start, runLength));
                }
            }
            else
            {
                textRows.Add(rows[i]);
                i++;
            }
        }

        return (tableGroups, textRows);
    }

    /// <summary>Splits one row into columns based on horizontal gaps between words.</summary>
    private static int CountColumns(List<Word> row) => SplitRowIntoColumns(row).Count;

    private static List<string> SplitRowIntoColumns(List<Word> row)
    {
        if (row.Count == 0) return new List<string>();

        var avgWordWidth = row.Average(w => w.BoundingBox.Width / Math.Max(1, w.Text.Length));
        var gapThreshold = avgWordWidth * ColumnGapMultiplier;

        var columns = new List<string>();
        var current = new StringBuilder(row[0].Text);

        for (int i = 1; i < row.Count; i++)
        {
            var gap = row[i].BoundingBox.Left - row[i - 1].BoundingBox.Right;
            if (gap > gapThreshold)
            {
                columns.Add(current.ToString());
                current = new StringBuilder(row[i].Text);
            }
            else
            {
                current.Append(' ').Append(row[i].Text);
            }
        }
        columns.Add(current.ToString());

        return columns;
    }

    private static string RenderRowsAsMarkdownTable(List<List<Word>> tableRows)
    {
        var rowsAsColumns = tableRows.Select(SplitRowIntoColumns).ToList();
        var columnCount = rowsAsColumns.Max(r => r.Count);
        if (columnCount == 0) return string.Empty;

        var sb = new StringBuilder();

        for (int r = 0; r < rowsAsColumns.Count; r++)
        {
            var cols = rowsAsColumns[r];
            sb.Append("| ");
            for (int c = 0; c < columnCount; c++)
                sb.Append((c < cols.Count ? cols[c] : string.Empty).Replace("|", "/")).Append(" | ");
            sb.AppendLine();

            if (r == 0)
            {
                sb.Append('|');
                for (int c = 0; c < columnCount; c++) sb.Append(" --- |");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}