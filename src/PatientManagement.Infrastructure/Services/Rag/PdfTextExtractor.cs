using System.Text;
using UglyToad.PdfPig;

namespace PatientManagement.Infrastructure.Services.Rag;

public static class PdfTextExtractor
{
    public static string ExtractText(Stream pdfStream)
    {
        var sb = new StringBuilder();
        using var document = PdfDocument.Open(pdfStream);
        foreach (var page in document.GetPages())
        {
            var text = page.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine($"[Page {page.Number}]");
                sb.AppendLine(text.Trim());
            }
        }
        return sb.ToString().Trim();
    }

    public static string ExtractText(byte[] pdfBytes)
    {
        using var ms = new MemoryStream(pdfBytes);
        return ExtractText(ms);
    }
}
