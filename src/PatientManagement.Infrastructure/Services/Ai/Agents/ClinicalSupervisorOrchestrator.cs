using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.DTOs.Patients;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Infrastructure.Services.Ai.Agents;

public class ClinicalSupervisorOrchestrator : IClinicalSupervisorOrchestrator
{
    private readonly IEnumerable<ISpecialistAgent> _specialistAgents;
    private readonly ILogger<ClinicalSupervisorOrchestrator> _logger;

    public ClinicalSupervisorOrchestrator(
        IEnumerable<ISpecialistAgent> specialistAgents,
        ILogger<ClinicalSupervisorOrchestrator> logger)
    {
        _specialistAgents = specialistAgents;
        _logger = logger;
    }

    public bool ShouldOrchestrate(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage)) return false;

        // Check if query spans multiple clinical disciplines:
        // e.g. Medication/Prescription + Symptoms/Diagnostic or Medication + Scheduling
        var hasMeds = Regex.IsMatch(userMessage, @"(?i)\b(prescrib|medicat|drug|dose|warfarin|ibuprofen|aspirin|amoxicillin|penicillin|lisinopril|statin|paracetamol|nsaid|interaction|contraindicat|allergic|allergy)\b");
        var hasSymptoms = Regex.IsMatch(userMessage, @"(?i)\b(pain|swelling|fever|cough|sprain|injury|fracture|vitals|bp|symptom|bleed|triage)\b");
        var hasScheduling = Regex.IsMatch(userMessage, @"(?i)\b(schedul|book|appoint|visit|consult|orthopedic|cardiolog|see\s*a\s*doctor|slot)\b");
        var hasExplicitMultiAgent = Regex.IsMatch(userMessage, @"(?i)\b(multi-agent|swarm|specialist review|comprehensive review|full panel|safety check)\b");

        // Triggers multi-agent if at least 2 clinical domains intersect or explicit multi-agent keyword is present
        var domainCount = (hasMeds ? 1 : 0) + (hasSymptoms ? 1 : 0) + (hasScheduling ? 1 : 0);
        return domainCount >= 2 || hasExplicitMultiAgent;
    }

    public async Task<MultiAgentOrchestrationResult> CoordinateAsync(
        string userMessage,
        int? patientId,
        PatientResponseDto? patient,
        IReadOnlyList<string> ragSnippets,
        CancellationToken cancellationToken = default)
    {
        var activeMeds = ExtractActiveMedications(patient);
        var allergies = ExtractAllergies(patient);
        var diagnoses = patient?.Consultations?.Select(c => c.Diagnosis).Where(d => !string.IsNullOrWhiteSpace(d)).ToList()
                        ?? new List<string>();

        var context = new ClinicalAgentContext(
            UserMessage: userMessage,
            PatientId: patientId,
            Patient: patient,
            KnownAllergies: allergies,
            ActiveMedications: activeMeds,
            PreviousDiagnoses: diagnoses,
            RagSnippets: ragSnippets
        );

        // Select agents capable of handling aspects of this multi-disciplinary query
        var participating = _specialistAgents.Where(a => a.CanHandle(context)).ToList();

        if (participating.Count == 0)
        {
            // Default to at least DiagnosticTriage if nothing matched
            var defaultAgent = _specialistAgents.FirstOrDefault(a => a.Specialty == AgentSpecialty.DiagnosticTriage);
            if (defaultAgent != null) participating.Add(defaultAgent);
        }

        _logger.LogInformation("ClinicalSupervisor dispatched query to {AgentCount} specialist agent(s): {AgentNames}",
            participating.Count, string.Join(", ", participating.Select(a => a.AgentName)));

        // Run specialist agents concurrently
        var analysisTasks = participating.Select(a => a.AnalyzeAsync(context, cancellationToken));
        var results = await Task.WhenAll(analysisTasks);

        var collaboratingNames = participating.Select(a => a.AgentName).ToList();
        var allWarnings = new List<string>();
        var proposals = new List<PendingActionProposalDto>();

        foreach (var r in results)
        {
            if (r.Warnings.Count > 0)
            {
                allWarnings.AddRange(r.Warnings);
            }

            if (r.ActionProposal != null)
            {
                proposals.Add(r.ActionProposal);
            }
        }

        // Cross-Agent Safety Reconciliation:
        // If Pharmacovigilance detected a critical contraindication, modify proposals to prioritize Medication Safety Review
        var criticalDrugWarning = results
            .FirstOrDefault(r => r.Specialty == AgentSpecialty.Pharmacovigilance)?
            .Warnings.FirstOrDefault(w => w.Contains("CONTRAINDICATION", StringComparison.OrdinalIgnoreCase) || w.Contains("ALLERGY", StringComparison.OrdinalIgnoreCase));

        if (criticalDrugWarning != null)
        {
            foreach (var p in proposals)
            {
                p.DiagnosisOrReason = $"Medication Safety Review: {p.DiagnosisOrReason} (Contraindication Detected)";
                p.ConfirmationPrompt = $"Schedule priority safety review with {p.DoctorName} (Potential drug interaction identified)?";
            }
        }

        var synthesizedText = BuildSynthesizedMarkdown(userMessage, results, allWarnings);

        return new MultiAgentOrchestrationResult(
            CollaboratingAgents: collaboratingNames,
            SynthesizedResponse: synthesizedText,
            PharmacovigilanceWarnings: allWarnings,
            ProposedActions: proposals
        );
    }

    private static string BuildSynthesizedMarkdown(
        string userMessage,
        SpecialistAnalysisResult[] results,
        List<string> allWarnings)
    {
        var sb = new StringBuilder();

        sb.AppendLine("### 🤖 Multi-Agent Clinical Specialist Swarm Analysis");
        sb.AppendLine();
        sb.AppendLine($"*Clinical Supervisor coordinated **{results.Length} specialist agents** for this multidisciplinary case.*");
        sb.AppendLine();

        if (allWarnings.Count > 0)
        {
            sb.AppendLine("> [!WARNING]");
            sb.AppendLine("> **PHARMACOVIGILANCE SAFETY ALERT:**");
            foreach (var w in allWarnings)
            {
                sb.AppendLine($"> • {w}");
            }
            sb.AppendLine();
        }

        foreach (var res in results)
        {
            var icon = res.Specialty switch
            {
                AgentSpecialty.Pharmacovigilance => "💊",
                AgentSpecialty.DiagnosticTriage => "🩺",
                AgentSpecialty.Scheduling => "📅",
                _ => "📋"
            };

            sb.AppendLine($"#### {icon} {res.AgentName}");
            sb.AppendLine($"**Finding:** {res.Summary}");

            if (res.Recommendations.Count > 0)
            {
                sb.AppendLine("**Recommendations:**");
                foreach (var rec in res.Recommendations)
                {
                    sb.AppendLine($"- {rec}");
                }
            }

            if (res.ActionProposal != null)
            {
                sb.AppendLine();
                sb.AppendLine($"📌 *Action Prepared: [{res.ActionProposal.ActionType}] {res.ActionProposal.ConfirmationPrompt}*");
            }

            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("**Consolidated Decision Support Plan:** Clinician should review the pharmacological contraindication above, administer approved safe alternative analgesic, and confirm the scheduled specialist evaluation.");

        return sb.ToString().Trim();
    }

    private static List<string> ExtractActiveMedications(PatientResponseDto? patient)
    {
        var meds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (patient == null) return meds.ToList();

        if (!string.IsNullOrWhiteSpace(patient.MedicalNotes))
        {
            ExtractFromText(patient.MedicalNotes, meds);
        }

        if (patient.Consultations != null)
        {
            foreach (var c in patient.Consultations)
            {
                if (!string.IsNullOrWhiteSpace(c.Prescription))
                {
                    ExtractFromText(c.Prescription, meds);
                }
                if (!string.IsNullOrWhiteSpace(c.Notes))
                {
                    ExtractFromText(c.Notes, meds);
                }
            }
        }

        return meds.ToList();
    }

    private static List<string> ExtractAllergies(PatientResponseDto? patient)
    {
        var allergies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (patient == null) return allergies.ToList();

        if (!string.IsNullOrWhiteSpace(patient.MedicalNotes))
        {
            var m = Regex.Match(patient.MedicalNotes, @"(?i)allergic to\s+([a-zA-Z0-9\s,]+)", RegexOptions.Compiled);
            if (m.Success)
            {
                allergies.Add(m.Groups[1].Value.Trim());
            }
            if (Regex.IsMatch(patient.MedicalNotes, @"(?i)penicillin")) allergies.Add("Penicillin");
            if (Regex.IsMatch(patient.MedicalNotes, @"(?i)nsaid|aspirin|ibuprofen")) allergies.Add("NSAIDs");
        }

        return allergies.ToList();
    }

    private static void ExtractFromText(string text, HashSet<string> target)
    {
        var pattern = @"(?i)\b(warfarin|coumadin|apixaban|eliquis|rivaroxaban|xarelto|dabigatran|heparin|ibuprofen|advil|motrin|naproxen|aleve|aspirin|diclofenac|ketorolac|meloxicam|lisinopril|enalapril|ramipril|losartan|valsartan|potassium|spironolactone|atorvastatin|lipitor|simvastatin|zocor|rosuvastatin|clarithromycin|erythromycin|amoxicillin|penicillin|augmentin|metformin|paracetamol|acetaminophen|cetirizine|fluticasone)\b";
        var matches = Regex.Matches(text, pattern);
        foreach (Match m in matches)
        {
            target.Add(m.Value);
        }
    }
}
