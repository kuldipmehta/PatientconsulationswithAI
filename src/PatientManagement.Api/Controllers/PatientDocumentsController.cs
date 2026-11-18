using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.DTOs.Rag;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Api.Controllers;

[ApiController]
[Route("api/patients/{patientId:int}/documents")]
[Authorize]
[Produces("application/json")]
public class PatientDocumentsController : ControllerBase
{
    private readonly IPatientRagService _ragService;

    public PatientDocumentsController(IPatientRagService ragService)
    {
        _ragService = ragService;
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Doctor,Receptionist")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<PatientDocumentResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadDocument(
        [FromRoute] int patientId,
        [FromForm] UploadPatientDocumentDto dto,
        CancellationToken cancellationToken)
    {
        if (dto.File == null || dto.File.Length == 0)
        {
            return BadRequest(ApiResponse<object>.Fail("A valid non-empty file must be provided."));
        }

        var doc = await _ragService.UploadAndIndexDocumentAsync(patientId, dto.File, dto, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<PatientDocumentResponseDto>.Ok(doc, "Document uploaded and indexed successfully into RAG vector store."));
    }

    [HttpGet]
    [Authorize(Roles = "Admin,Doctor,Receptionist")]
    [ProducesResponseType(typeof(ApiResponse<List<PatientDocumentResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDocuments(
        [FromRoute] int patientId,
        CancellationToken cancellationToken)
    {
        var docs = await _ragService.GetPatientDocumentsAsync(patientId, cancellationToken);
        return Ok(ApiResponse<List<PatientDocumentResponseDto>>.Ok(docs, "Patient documents retrieved successfully."));
    }

    [HttpDelete("{documentId:int}")]
    [Authorize(Roles = "Admin,Doctor")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDocument(
        [FromRoute] int patientId,
        [FromRoute] int documentId,
        CancellationToken cancellationToken)
    {
        var deleted = await _ragService.DeleteDocumentAsync(patientId, documentId, cancellationToken);
        if (!deleted)
        {
            return NotFound(ApiResponse<object>.Fail($"Document #{documentId} not found for Patient #{patientId}."));
        }

        return Ok(ApiResponse<bool>.Ok(true, "Document deleted and removed from RAG index successfully."));
    }

    [HttpPost("/api/patients/{patientId:int}/rag/search")]
    [Authorize(Roles = "Admin,Doctor")]
    [ProducesResponseType(typeof(ApiResponse<List<RagSearchResultDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchKnowledge(
        [FromRoute] int patientId,
        [FromBody] SemanticSearchRequestDto request,
        CancellationToken cancellationToken)
    {
        var results = await _ragService.SearchPatientKnowledgeAsync(
            patientId,
            request.Query,
            request.TopK > 0 ? request.TopK : 5,
            request.MinScore > 0 ? request.MinScore : 0.15,
            cancellationToken);

        return Ok(ApiResponse<List<RagSearchResultDto>>.Ok(results, $"Retrieved {results.Count} semantically relevant record chunks."));
    }

    [HttpPost("/api/patients/{patientId:int}/rag/reindex")]
    [Authorize(Roles = "Admin,Doctor")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReindexConsultations(
        [FromRoute] int patientId,
        CancellationToken cancellationToken)
    {
        var count = await _ragService.ReindexPatientConsultationsAsync(patientId, cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { patientId, consultationsIndexed = count }, $"Reindexed {count} consultations into RAG vector store."));
    }
}
