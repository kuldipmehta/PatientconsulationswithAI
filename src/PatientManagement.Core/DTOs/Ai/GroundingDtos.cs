using PatientManagement.Core.Enums;

namespace PatientManagement.Core.DTOs.Ai;

public record ClinicalClaimVerification(
    string ClaimText,
    ClaimStatus Status,
    string? SourceEvidence = null,
    string? Explanation = null
);

public record GroundingVerificationResult(
    double GroundingScore,
    bool IsGrounded,
    List<ClinicalClaimVerification> Claims,
    List<string> HallucinationFlags,
    bool RequiresCorrection
);
