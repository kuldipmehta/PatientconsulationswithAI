using PatientManagement.Core.DTOs.Ai;

namespace PatientManagement.Core.Interfaces;

public interface IGroundingVerifierService
{
    /// <summary>
    /// Decomposes generated clinical text into atomic claims and cross-verifies each
    /// against source clinical consultations and document chunks.
    /// Flags ungrounded speculations and contradicted assertions.
    /// </summary>
    Task<GroundingVerificationResult> VerifyAsync(
        string generatedText,
        IReadOnlyList<string> sourceContexts,
        CancellationToken cancellationToken = default);
}
