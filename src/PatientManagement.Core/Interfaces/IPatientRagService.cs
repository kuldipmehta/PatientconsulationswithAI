using Microsoft.AspNetCore.Http;
using PatientManagement.Core.DTOs.Rag;

namespace PatientManagement.Core.Interfaces;

public interface IPatientRagService
{
    Task<PatientDocumentResponseDto> UploadAndIndexDocumentAsync(
        int patientId,
        IFormFile file,
        UploadPatientDocumentDto dto,
        CancellationToken cancellationToken = default);

    Task<List<PatientDocumentResponseDto>> GetPatientDocumentsAsync(
        int patientId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteDocumentAsync(
        int patientId,
        int documentId,
        CancellationToken cancellationToken = default);

    Task<List<RagSearchResultDto>> SearchPatientKnowledgeAsync(
        int patientId,
        string query,
        int topK = 5,
        double minScore = 0.35,
        CancellationToken cancellationToken = default);

    Task IndexConsultationAsync(
        int consultationId,
        CancellationToken cancellationToken = default);

    Task<int> ReindexPatientConsultationsAsync(
        int patientId,
        CancellationToken cancellationToken = default);
}
