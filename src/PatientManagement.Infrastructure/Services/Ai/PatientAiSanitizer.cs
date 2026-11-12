using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.DTOs.Patients;

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

    public static object BuildMinimizedClinicalPayload(PatientResponseDto patient)
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
            }).ToList()
        };
    }

    public static List<AiSourceCitationDto> ExtractCitations(PatientResponseDto patient)
    {
        return patient.Consultations
            .OrderByDescending(c => c.ConsultationDate)
            .Select(c => new AiSourceCitationDto
            {
                ConsultationId = c.Id,
                ConsultationDate = c.ConsultationDate,
                DoctorName = c.DoctorName,
                ClinicalSnippet = $"{c.Diagnosis}" +
                    (string.IsNullOrWhiteSpace(c.Prescription) ? string.Empty : $" (Rx: {c.Prescription})")
            })
            .ToList();
    }
}
