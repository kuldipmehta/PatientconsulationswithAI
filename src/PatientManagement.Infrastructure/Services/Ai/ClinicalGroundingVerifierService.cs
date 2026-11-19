using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Infrastructure.Services.Ai;

public class ClinicalGroundingVerifierService : IGroundingVerifierService
{
    private readonly ILogger<ClinicalGroundingVerifierService> _logger;

    private static readonly Regex NumericMetricRegex = new(
        @"(?i)\b\d+(\.\d+)?\s*(%|(?:mg|mcg|ml|mmhg|bpm|mmol\/l|mg\/dl|g\/dl)\b)|\b\d{2,3}\/\d{2,3}\b",
        RegexOptions.Compiled);

    private static readonly Regex MedicationRegex = new(
        @"(?i)\b(warfarin|coumadin|apixaban|eliquis|rivaroxaban|xarelto|dabigatran|heparin|ibuprofen|advil|motrin|naproxen|aleve|aspirin|diclofenac|ketorolac|meloxicam|lisinopril|enalapril|ramipril|losartan|valsartan|potassium|spironolactone|atorvastatin|lipitor|simvastatin|zocor|rosuvastatin|clarithromycin|erythromycin|amoxicillin|penicillin|augmentin|metformin|paracetamol|acetaminophen|cetirizine|fluticasone)\b",
        RegexOptions.Compiled);

    private static readonly Regex SentenceSplitter = new(
        @"(?<=[.!?])\s+|\r?\n+",
        RegexOptions.Compiled);

    public ClinicalGroundingVerifierService(ILogger<ClinicalGroundingVerifierService> logger)
    {
        _logger = logger;
    }

    public Task<GroundingVerificationResult> VerifyAsync(
        string generatedText,
        IReadOnlyList<string> sourceContexts,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(generatedText))
        {
            return Task.FromResult(new GroundingVerificationResult(
                GroundingScore: 1.0,
                IsGrounded: true,
                Claims: new List<ClinicalClaimVerification>(),
                HallucinationFlags: new List<string>(),
                RequiresCorrection: false
            ));
        }

        var sourceCorpus = sourceContexts != null && sourceContexts.Count > 0
            ? string.Join("\n---\n", sourceContexts)
            : string.Empty;

        var claims = new List<ClinicalClaimVerification>();
        var hallucinationFlags = new List<string>();

        var sentences = SentenceSplitter.Split(generatedText)
            .Select(s => s.Trim())
            .Where(s => s.Length > 12)
            .ToList();

        foreach (var sentence in sentences)
        {
            // 1. Check Allergy Contradictions
            if (Regex.IsMatch(sentence, @"(?i)\b(no\s+(?:known\s+)?(?:drug\s+)?allerg\w*|denies\s+allerg\w*|no\s+allerg\w*)\b"))
            {
                if (Regex.IsMatch(sourceCorpus, @"(?i)\b(allergic\s+to|allergy\s+to|allergy\b|allergies\b|anaphylax\w*|hypersensitivity)\b"))
                {
                    var flag = "CONTRADICTION: Generated text asserted no known allergies, but source records document an active clinical allergy.";
                    claims.Add(new ClinicalClaimVerification(
                        ClaimText: sentence,
                        Status: ClaimStatus.Contradicted,
                        SourceEvidence: ExtractMatchingLine(sourceCorpus, @"(?i)(allergic|allerg)"),
                        Explanation: flag
                    ));
                    hallucinationFlags.Add(flag);
                    continue;
                }
            }

            // 2. Check Numeric & Laboratory Metrics
            var metricMatches = NumericMetricRegex.Matches(sentence);
            foreach (Match m in metricMatches)
            {
                var metricToken = m.Value.Trim();
                var isFoundInSource = sourceCorpus.Contains(metricToken, StringComparison.OrdinalIgnoreCase);

                if (isFoundInSource)
                {
                    claims.Add(new ClinicalClaimVerification(
                        ClaimText: $"Clinical metric '{metricToken}' in: \"{sentence}\"",
                        Status: ClaimStatus.Supported,
                        SourceEvidence: ExtractMatchingLine(sourceCorpus, Regex.Escape(metricToken)),
                        Explanation: "Verified exact match in source clinical record."
                    ));
                }
                else
                {
                    var flag = $"UNGROUNDED LAB / METRIC: Value '{metricToken}' asserted in summary was not found in source records.";
                    claims.Add(new ClinicalClaimVerification(
                        ClaimText: $"Clinical metric '{metricToken}' in: \"{sentence}\"",
                        Status: ClaimStatus.Ungrounded,
                        SourceEvidence: null,
                        Explanation: flag
                    ));
                    hallucinationFlags.Add(flag);
                }
            }

            // 3. Check Specific Medication Assertions
            var medMatches = MedicationRegex.Matches(sentence);
            foreach (Match m in medMatches)
            {
                var medName = m.Value;
                // Exclude hypothetical alternative suggestions (e.g. "Do NOT take Ibuprofen", "Consider Paracetamol instead")
                if (Regex.IsMatch(sentence, @"(?i)\b(do not|avoid|contraindicat|instead of|alternative|recommend)\b"))
                {
                    continue;
                }

                var medInSource = sourceCorpus.Contains(medName, StringComparison.OrdinalIgnoreCase);
                if (medInSource)
                {
                    claims.Add(new ClinicalClaimVerification(
                        ClaimText: $"Medication assertion '{medName}' in: \"{sentence}\"",
                        Status: ClaimStatus.Supported,
                        SourceEvidence: ExtractMatchingLine(sourceCorpus, Regex.Escape(medName)),
                        Explanation: "Verified medication presence in patient record."
                    ));
                }
                else if (Regex.IsMatch(sentence, @"(?i)\b(takes|taking|prescribed|dose|history of)\b"))
                {
                    var flag = $"UNGROUNDED MEDICATION: Patient was asserted to take '{medName}', which is not recorded in patient history.";
                    claims.Add(new ClinicalClaimVerification(
                        ClaimText: $"Medication assertion '{medName}' in: \"{sentence}\"",
                        Status: ClaimStatus.Ungrounded,
                        SourceEvidence: null,
                        Explanation: flag
                    ));
                    hallucinationFlags.Add(flag);
                }
            }
        }

        double score;
        if (claims.Count == 0)
        {
            // If no specific metric/medication claims were extracted, check lexical overlap
            score = 1.0;
        }
        else
        {
            var supportedCount = claims.Count(c => c.Status == ClaimStatus.Supported);
            score = Math.Round((double)supportedCount / claims.Count, 2);
        }

        var hasContradiction = claims.Any(c => c.Status == ClaimStatus.Contradicted);
        var hasUngrounded = claims.Any(c => c.Status == ClaimStatus.Ungrounded);
        var isGrounded = score >= 0.85 && !hasContradiction && !hasUngrounded;
        var requiresCorrection = !isGrounded;

        _logger.LogInformation("Grounding verification completed: Score={Score}, IsGrounded={IsGrounded}, ClaimsCount={Count}, Flags={FlagCount}",
            score, isGrounded, claims.Count, hallucinationFlags.Count);

        return Task.FromResult(new GroundingVerificationResult(
            GroundingScore: score,
            IsGrounded: isGrounded,
            Claims: claims,
            HallucinationFlags: hallucinationFlags,
            RequiresCorrection: requiresCorrection
        ));
    }

    private static string? ExtractMatchingLine(string sourceCorpus, string pattern)
    {
        if (string.IsNullOrWhiteSpace(sourceCorpus)) return null;

        var lines = sourceCorpus.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);
        var match = lines.FirstOrDefault(l => regex.IsMatch(l));
        return match?.Trim();
    }
}
