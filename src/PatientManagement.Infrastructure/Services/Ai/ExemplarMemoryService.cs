using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Infrastructure.Services.Ai;

public class ExemplarMemoryService : IExemplarMemoryService
{
    private readonly ITextEmbeddingService _textEmbeddingService;
    private readonly ILogger<ExemplarMemoryService> _logger;

    private sealed record InternalExemplar(
        ClinicalExemplarDto Dto,
        float[]? Embedding = null
    );

    private readonly List<InternalExemplar> _exemplars = new();
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private static readonly IReadOnlyList<ClinicalExemplarDto> CuratedExemplars = new List<ClinicalExemplarDto>
    {
        new(
            Id: "EX-PHARMACO-01",
            Domain: "Pharmacovigilance",
            ScenarioTitle: "Anticoagulant & NSAID Bleeding Risk Mitigation",
            Query: "Patient is taking Warfarin 5mg daily and reports acute knee swelling and moderate pain. Can we prescribe Ibuprofen 400mg three times daily?",
            IdealOutput: "CONTRAINDICATION WARNING: Ibuprofen (and all systemic NSAIDs) is strictly contraindicated for patients on Warfarin due to high risk of major gastrointestinal hemorrhage. I recommend Paracetamol / Acetaminophen 500mg-1000mg up to three times daily as a safe alternative (maximum 3g/24h). We should schedule an urgent orthopedic evaluation to assess the acute knee effusion.",
            ClinicalRationale: "NSAIDs in combination with Warfarin exponentially increase upper GI bleeding and hemorrhage risk due to combined platelet inhibition and hypoprothrombinemia. Acetaminophen/Paracetamol is the frontline safer alternative, with orthopedic consultation for knee effusion."
        ),
        new(
            Id: "EX-TRIAGE-01",
            Domain: "Emergency Triage",
            ScenarioTitle: "Acute Retrosternal Chest Pain Emergency Protocol",
            Query: "Patient reports sudden crushing chest pressure radiating down left arm with shortness of breath and diaphoresis.",
            IdealOutput: "EMERGENCY ALERT: Call emergency services immediately (911 / 112 / 108) or go to the nearest Emergency Department immediately. Do NOT attempt to drive yourself. Symptoms of crushing chest pressure radiating to the arm with sweating indicate suspected Acute Coronary Syndrome requiring emergency ECG and cardiac enzymes.",
            ClinicalRationale: "Classic acute coronary syndrome / STEMI presentation. Requires immediate zero-latency escalation to emergency services (911/112/108) with instructions not to drive."
        ),
        new(
            Id: "EX-ALLERGY-01",
            Domain: "Allergy Safety",
            ScenarioTitle: "Severe Beta-Lactam / Penicillin Hypersensitivity Cross-Check",
            Query: "Patient with documented severe penicillin allergy presents with acute bacterial sinusitis. Can we prescribe Augmentin (Amoxicillin/Clavulanate)?",
            IdealOutput: "ALLERGY CONTRAINDICATION: Augmentin contains Amoxicillin and is strictly contraindicated due to the patient's documented penicillin allergy, which carries a severe risk of anaphylaxis. I recommend considering Azithromycin 500mg daily or Doxycycline 100mg twice daily as safe non-beta-lactam alternatives.",
            ClinicalRationale: "Augmentin contains Amoxicillin, an aminopenicillin. Administration to a penicillin-allergic patient carries severe risk of IgE-mediated anaphylaxis. Safe alternatives include Azithromycin, Doxycycline, or Levofloxacin."
        ),
        new(
            Id: "EX-SUMMARY-01",
            Domain: "Clinical Summary",
            ScenarioTitle: "De-Identified Multi-Morbidity Chronic Care Synthesis",
            Query: "Summarize clinical consultation history for Patient #1 presenting with hypertension and type 2 diabetes.",
            IdealOutput: "Patient #1 is a 68-year-old managed for hypertension and type 2 diabetes. Active regimen includes Lisinopril 20mg daily. Most recent vitals demonstrate controlled blood pressure at 120/80 mmHg and pulse 72 bpm, with stable glycemic control (HbA1c 6.5%). Recommended actions: schedule routine 6-month renal panel and HbA1c surveillance.",
            ClinicalRationale: "Synthesize longitudinal consultation history, extract vital metrics (BP 120/80 mmHg, HbA1c 6.5%), track active medications (Lisinopril 20mg), and explicitly withhold direct patient identifiers (PII)."
        ),
        new(
            Id: "EX-SCHED-01",
            Domain: "Specialist Scheduling",
            ScenarioTitle: "Human-in-the-Loop Specialty Routing for Post-Surgical Follow-up",
            Query: "Patient has persistent knee swelling and instability 4 weeks following arthroscopic meniscectomy. Need follow-up appointment.",
            IdealOutput: "I have identified Dr. Sarah Jenkins (Orthopedic Surgery) for this post-operative evaluation. I have created a pending scheduling proposal for consultation review: 'Book Dr. Sarah Jenkins for Post-Arthroscopic Evaluation'. Please confirm or cancel this proposal to finalize the calendar booking.",
            ClinicalRationale: "Route to Orthopedic Surgery specialty matching primary surgeon Dr. Sarah Jenkins; propose appointment slot requiring clinician confirmation."
        )
    };

    public ExemplarMemoryService(
        ITextEmbeddingService textEmbeddingService,
        ILogger<ExemplarMemoryService> logger)
    {
        _textEmbeddingService = textEmbeddingService;
        _logger = logger;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            foreach (var ex in CuratedExemplars)
            {
                var contentToEmbed = $"{ex.Domain} {ex.ScenarioTitle}: {ex.Query} {ex.ClinicalRationale}";
                float[]? embedding = null;
                try
                {
                    embedding = await _textEmbeddingService.GenerateEmbeddingAsync(contentToEmbed, cancellationToken);
                }
                catch (Exception exx)
                {
                    _logger.LogWarning(exx, "Could not generate vector embedding for exemplar {ExemplarId}. Falling back to lexical matching.", ex.Id);
                }

                _exemplars.Add(new InternalExemplar(ex, embedding));
            }

            _initialized = true;
            _logger.LogInformation("Exemplar Memory Service initialized with {Count} gold-standard clinical exemplars.", _exemplars.Count);
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<IReadOnlyList<ClinicalExemplarDto>> GetRelevantExemplarsAsync(
        string query,
        string? domain = null,
        int topK = 2,
        double minSimilarity = 0.55,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<ClinicalExemplarDto>();
        }

        await EnsureInitializedAsync(cancellationToken);

        float[]? queryEmbedding = null;
        try
        {
            queryEmbedding = await _textEmbeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to embed query for exemplar search; using lexical fallback.");
        }

        var candidates = _exemplars
            .Where(e => string.IsNullOrWhiteSpace(domain) || string.Equals(e.Dto.Domain, domain, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var queryTokens = Tokenize(query);
        var scored = new List<(ClinicalExemplarDto Dto, double Score)>();

        foreach (var c in candidates)
        {
            double vectorSim = 0.0;
            if (queryEmbedding != null && c.Embedding != null && queryEmbedding.Length == c.Embedding.Length)
            {
                vectorSim = CosineSimilarity(queryEmbedding, c.Embedding);
            }

            // Keyword / Lexical similarity
            var exemplarText = $"{c.Dto.Domain} {c.Dto.ScenarioTitle} {c.Dto.Query} {c.Dto.ClinicalRationale}".ToLowerInvariant();
            var exemplarTokens = Tokenize(exemplarText);
            var intersection = queryTokens.Intersect(exemplarTokens).Count();
            var union = queryTokens.Union(exemplarTokens).Count();
            double jaccardSim = union > 0 ? (double)intersection / union : 0.0;

            // Hybrid score: weighted combination
            double finalScore = vectorSim > 0.0
                ? (vectorSim * 0.7) + (jaccardSim * 0.3)
                : jaccardSim * 2.5; // Scale up lexical when no vector

            // Domain exact match bonus
            if (!string.IsNullOrWhiteSpace(domain) && string.Equals(c.Dto.Domain, domain, StringComparison.OrdinalIgnoreCase))
            {
                finalScore += 0.1;
            }

            if (finalScore >= minSimilarity)
            {
                scored.Add((c.Dto with { RelevanceScore = Math.Round(finalScore, 3) }, finalScore));
            }
        }

        return scored
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .Select(s => s.Dto)
            .ToList();
    }

    public string FormatFewShotPrompt(IReadOnlyList<ClinicalExemplarDto> exemplars)
    {
        if (exemplars == null || exemplars.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[CLINICAL EXEMPLAR GUIDANCE - GOLD-STANDARD CLINICAL PATTERNS]");
        sb.AppendLine("The following verified clinical exemplars demonstrate the required safety precautions, clinical rationale, and objective tone:");
        sb.AppendLine();

        int index = 1;
        foreach (var ex in exemplars)
        {
            sb.AppendLine($"--- Exemplar {index} (ID: {ex.Id} | Domain: {ex.Domain} | Match: {ex.RelevanceScore:P0}) ---");
            sb.AppendLine($"Scenario: {ex.ScenarioTitle}");
            sb.AppendLine($"Clinician Query: {ex.Query}");
            sb.AppendLine($"Clinical Rationale: {ex.ClinicalRationale}");
            sb.AppendLine($"Gold-Standard Reference Response:\n{ex.IdealOutput}");
            sb.AppendLine();
            index++;
        }

        sb.AppendLine("[END CLINICAL EXEMPLAR GUIDANCE]");
        return sb.ToString();
    }

    public IReadOnlyList<ClinicalExemplarDto> GetAllExemplars()
    {
        return CuratedExemplars;
    }

    private static HashSet<string> Tokenize(string text)
    {
        var matches = Regex.Matches(text.ToLowerInvariant(), @"\b[a-z0-9]{3,}\b");
        var set = new HashSet<string>();
        foreach (Match m in matches)
        {
            set.Add(m.Value);
        }
        return set;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0.0;
        double normA = 0.0;
        double normB = 0.0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA <= 0.0 || normB <= 0.0) return 0.0;
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
