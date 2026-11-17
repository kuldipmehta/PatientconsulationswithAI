using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.DTOs.Patients;
using PatientManagement.Core.Entities;

namespace PatientManagement.Infrastructure.Services.Ai;

public static class PatientAiSanitizer
{
    public static string WrapUntrustedText(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return "<untrusted_clinical_text>None provided</untrusted_clinical_text>";
        }

        // Prevent closing tag escape inside user-supplied medical notes or symptoms
        var neutralized = rawText
            .Replace("<untrusted_clinical_text>", "[tag-removed]", StringComparison.OrdinalIgnoreCase)
            .Replace("</untrusted_clinical_text>", "[tag-removed]", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return $"<untrusted_clinical_text>\n{neutralized}\n</untrusted_clinical_text>";
    }

    public static object BuildMinimizedClinicalPayload(
        PatientResponseDto patient,
        IEnumerable<PatientDocumentChunk>? documentChunks = null)
    {
        // Strictly omit PII: FirstName, LastName, Email, PhoneNumber, Address, DateOfBirth
        return new
        {
            ClinicalPseudonym = $"Patient #{patient.Id}",
            Age = patient.Age,
            Gender = patient.Gender,
            SanitizedMedicalNotes = WrapUntrustedText(patient.MedicalNotes),
            Consultations = patient.Consultations.Select(c => new
            {
                ConsultationId = c.Id,
                DateUtc = c.ConsultationDate.ToString("yyyy-MM-dd"),
                DoctorSpecialtyOrName = c.DoctorName,
                Status = c.Status.ToString(),
                Diagnosis = WrapUntrustedText(c.Diagnosis),
                Symptoms = WrapUntrustedText(c.Symptoms),
                Notes = WrapUntrustedText(c.Notes),
                TreatmentPlan = WrapUntrustedText(c.TreatmentPlan),
                Prescription = WrapUntrustedText(c.Prescription)
            }).ToList(),
            UploadedLabAndClinicalDocuments = documentChunks != null
                ? documentChunks.Select(dc => new
                {
                    DocumentOrChunkId = dc.Id,
                    DocumentType = dc.SourceType,
                    Label = dc.SourceLabel,
                    Content = WrapUntrustedText(dc.TextContent),
                    DateUtc = dc.CreatedAtUtc.ToString("yyyy-MM-dd")
                }).ToList()
                : (object)patient.Documents.Select(d => new
                {
                    DocumentId = d.Id,
                    DocumentName = d.FileName,
                    d.DocumentType,
                    TotalChunks = d.TotalChunks,
                    Summary = d.Summary,
                    DateUtc = d.CreatedAtUtc.ToString("yyyy-MM-dd")
                }).ToList()
        };
    }

    public static List<AiSourceCitationDto> ExtractCitations(
        PatientResponseDto patient,
        IEnumerable<PatientDocumentChunk>? documentChunks = null)
    {
        var list = patient.Consultations
            .OrderByDescending(c => c.ConsultationDate)
            .Select(c => new AiSourceCitationDto
            {
                ConsultationId = c.Id,
                ConsultationDate = c.ConsultationDate,
                DoctorName = c.DoctorName,
                ClinicalSnippet = $"{c.Diagnosis}" +
                    (string.IsNullOrWhiteSpace(c.Prescription) ? string.Empty : $" (Rx: {c.Prescription})"),
                SourceType = "Consultation"
            })
            .ToList();

        if (documentChunks != null)
        {
            foreach (var chunk in documentChunks)
            {
                list.Add(new AiSourceCitationDto
                {
                    ConsultationId = chunk.ConsultationId ?? 0,
                    ConsultationDate = chunk.CreatedAtUtc,
                    DoctorName = chunk.SourceLabel,
                    ClinicalSnippet = chunk.TextContent.Length > 160 ? chunk.TextContent[..160] + "..." : chunk.TextContent,
                    SourceType = chunk.SourceType,
                    DocumentId = chunk.PatientDocumentId,
                    DocumentName = chunk.SourceLabel
                });
            }
        }
        else if (patient.Documents.Count > 0)
        {
            foreach (var doc in patient.Documents)
            {
                list.Add(new AiSourceCitationDto
                {
                    ConsultationId = doc.ConsultationId ?? 0,
                    ConsultationDate = doc.CreatedAtUtc,
                    DoctorName = "Uploaded Clinical Document",
                    ClinicalSnippet = $"{doc.FileName} ({doc.DocumentType}, {doc.TotalChunks} chunks)",
                    SourceType = "PatientDocument",
                    DocumentId = doc.Id,
                    DocumentName = doc.FileName
                });
            }
        }

        return list;
    }
}
