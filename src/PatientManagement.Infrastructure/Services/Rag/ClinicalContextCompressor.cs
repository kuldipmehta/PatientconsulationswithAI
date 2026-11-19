using System.Text.RegularExpressions;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Infrastructure.Services.Rag;

public class ClinicalContextCompressor : IRagContextCompressor
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "but", "if", "then", "else", "when",
        "at", "by", "for", "with", "about", "against", "between", "into", "through",
        "during", "before", "after", "above", "below", "to", "from", "up", "down",
        "in", "out", "on", "off", "over", "under", "again", "further", "then", "once",
        "here", "there", "all", "any", "both", "each", "few", "more", "most", "other",
        "some", "such", "no", "nor", "not", "only", "own", "same", "so", "than", "too",
        "very", "s", "t", "can", "will", "just", "don", "should", "now", "is", "are",
        "was", "were", "be", "been", "being", "have", "has", "had", "do", "does", "did",
        "patient", "report", "notes", "record"
    };

    private static readonly Regex BoilerplateRegex = new(
        @"(?i)(hospital|clinic|department|room\s*\#?\d+|page\s*\d+\s*(of|\/)\s*\d+|attending\s*physician|confidentiality\s*notice|electronically\s*signed|all\s*rights\s*reserved|printed\s*on|fax\s*number|phone\s*number|mrn\s*\#?\d+|facility)",
        RegexOptions.Compiled);

    private static readonly Regex ClinicalMetricRegex = new(
        @"(?i)\b\d+(\.\d+)?\s*(%|mg|mcg|ml|g|kg|lbs?|bpm|mmhg|mmol\/l|mg\/dl|meq\/l|celsius|fahrenheit|\/min)\b|\b(bp\s*\d+\/\d+)\b",
        RegexOptions.Compiled);

    private static readonly Regex SentenceSplitter = new(
        @"(?<=[.!?])\s+|\r?\n+",
        RegexOptions.Compiled);

    public string Compress(string query, string content, int maxWords = 80)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var rawWords = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (rawWords.Length <= maxWords)
        {
            return content.Trim();
        }

        var queryTerms = ExtractSignificantTerms(query);
        var rawSentences = SentenceSplitter.Split(content)
            .Select(s => s.Trim())
            .Where(s => s.Length > 10)
            .ToList();

        if (rawSentences.Count <= 1)
        {
            return TruncateWords(content, maxWords);
        }

        var scored = new List<(int Index, string Sentence, double Score)>();

        for (var i = 0; i < rawSentences.Count; i++)
        {
            var sentence = rawSentences[i];
            var score = ScoreSentence(sentence, query, queryTerms);
            scored.Add((i, sentence, score));
        }

        // Rank by score descending
        var selected = new List<(int Index, string Sentence, double Score)>();
        var currentWords = 0;

        foreach (var item in scored.OrderByDescending(s => s.Score))
        {
            if (item.Score <= 0.0 && selected.Count > 0)
            {
                // Skip non-positive scoring boilerplate when we already have relevant clinical content
                continue;
            }

            var wordCount = item.Sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (currentWords + wordCount > maxWords && selected.Count > 0)
            {
                continue;
            }

            selected.Add(item);
            currentWords += wordCount;

            if (currentWords >= maxWords)
            {
                break;
            }
        }

        if (selected.Count == 0)
        {
            return TruncateWords(content, maxWords);
        }

        // Re-order by original chronological index
        var ordered = selected.OrderBy(s => s.Index).Select(s => s.Sentence).ToList();
        return string.Join(" ", ordered);
    }

    private static double ScoreSentence(string sentence, string originalQuery, HashSet<string> queryTerms)
    {
        var score = 0.0;

        // Penalty for pure boilerplate
        if (BoilerplateRegex.IsMatch(sentence))
        {
            score -= 6.0;
        }

        // Boost for whole query exact phrase match
        if (originalQuery.Length > 4 && sentence.Contains(originalQuery, StringComparison.OrdinalIgnoreCase))
        {
            score += 6.0;
        }

        // Query term overlaps
        var matchedTerms = 0;
        foreach (var term in queryTerms)
        {
            if (sentence.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 3.0;
                matchedTerms++;
            }
        }

        if (matchedTerms > 1)
        {
            score += matchedTerms * 1.5; // Density bonus
        }

        // Boost for clinical metrics (lab values, doses, vitals)
        var metricMatches = ClinicalMetricRegex.Matches(sentence);
        score += metricMatches.Count * 2.0;

        // Clinical action terms
        if (Regex.IsMatch(sentence, @"(?i)\b(diagnos|prescrib|allerg|treatment|plan|assess|history|denies|reports|admit|discharge)\b"))
        {
            score += 1.5;
        }

        return score;
    }

    private static HashSet<string> ExtractSignificantTerms(string text)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return terms;

        var tokens = Regex.Matches(text, @"\b[a-zA-Z0-9_\-\.]{2,}\b")
            .Select(m => m.Value.ToLowerInvariant());

        foreach (var t in tokens)
        {
            if (!StopWords.Contains(t))
            {
                terms.Add(t);
            }
        }

        return terms;
    }

    private static string TruncateWords(string text, int maxWords)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= maxWords) return text.Trim();
        return string.Join(" ", words.Take(maxWords)) + "...";
    }
}
