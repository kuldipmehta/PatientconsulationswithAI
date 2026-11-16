using System.Text;
using System.Text.RegularExpressions;

namespace PatientManagement.Infrastructure.Services.Rag;

public static class TextChunker
{
    private static readonly Regex SentenceSplitter = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    public static List<string> ChunkText(string rawText, int maxChunkSize = 500, int overlapSize = 100)
    {
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return chunks;
        }

        // Clean redundant whitespaces
        var cleaned = rawText
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Trim();

        if (cleaned.Length <= maxChunkSize)
        {
            chunks.Add(cleaned);
            return chunks;
        }

        var paragraphs = cleaned.Split(new[] { "\n\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var currentChunk = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            var sentences = SentenceSplitter.Split(paragraph);
            foreach (var sentence in sentences)
            {
                var s = sentence.Trim();
                if (string.IsNullOrEmpty(s)) continue;

                if (currentChunk.Length + s.Length + 1 > maxChunkSize && currentChunk.Length > 0)
                {
                    var chunkStr = currentChunk.ToString().Trim();
                    if (!string.IsNullOrEmpty(chunkStr))
                    {
                        chunks.Add(chunkStr);
                    }

                    // Prepare overlap for continuity
                    var overlap = GetTrailingOverlap(chunkStr, overlapSize);
                    currentChunk.Clear();
                    if (!string.IsNullOrEmpty(overlap))
                    {
                        currentChunk.Append(overlap).Append(' ');
                    }
                }

                if (currentChunk.Length > 0)
                {
                    currentChunk.Append(' ');
                }
                currentChunk.Append(s);
            }
        }

        if (currentChunk.Length > 0)
        {
            var finalStr = currentChunk.ToString().Trim();
            if (!string.IsNullOrEmpty(finalStr) && !chunks.Contains(finalStr))
            {
                chunks.Add(finalStr);
            }
        }

        return chunks;
    }

    private static string GetTrailingOverlap(string text, int overlapSize)
    {
        if (string.IsNullOrWhiteSpace(text) || overlapSize <= 0)
        {
            return string.Empty;
        }

        if (text.Length <= overlapSize)
        {
            return text;
        }

        var start = text.Length - overlapSize;
        var spaceIndex = text.IndexOf(' ', start);
        if (spaceIndex != -1 && spaceIndex < text.Length - 1)
        {
            return text[(spaceIndex + 1)..];
        }

        return text[start..];
    }
}
