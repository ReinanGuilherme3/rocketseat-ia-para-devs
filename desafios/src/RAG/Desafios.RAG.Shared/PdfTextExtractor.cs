using System.Text;
using UglyToad.PdfPig;

namespace Desafios.RAG.Shared;

public static class PdfTextExtractor
{
    public static string ExtractText(string pdfPath)
    {
        using var document = PdfDocument.Open(pdfPath);
        var text = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            text.AppendLine(page.Text);
        }

        return text.ToString();
    }
}
