using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Infrastructure.Services.Ai.Agents;

public class DiagnosticTriageAgent : ISpecialistAgent
{
    private readonly ILogger<DiagnosticTriageAgent> _logger;

    public string AgentName => "Clinical Diagnostic & Triage Agent";
    public AgentSpecialty Specialty => AgentSpecialty.DiagnosticTriage;

    public DiagnosticTriageAgent(ILogger<DiagnosticTriageAgent> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(ClinicalAgentContext context)
    {
        var text = context.UserMessage;
        return Regex.IsMatch(text, @"(?i)\b(pain|swelling|fever|cough|shortness of breath|headache|ache|sprain|injury|fracture|rash|symptom|bleed|dizzy|nausea|vomit|vitals|bp|pulse|examination|triage)\b");
    }

    public Task<SpecialistAnalysisResult> AnalyzeAsync(ClinicalAgentContext context, CancellationToken cancellationToken = default)
    {
        var text = context.UserMessage;
        var recommendations = new List<string>();
        var warnings = new List<string>();
        var requiresEscalation = false;

        // Emergency red flag checks
        if (Regex.IsMatch(text, @"(?i)\b(chest pain|crushing|radiating to (arm|jaw)|slurred speech|facial droop|cannot breathe|unresponsive)\b"))
        {
            requiresEscalation = true;
            warnings.Add("RED FLAG: Potential acute cardiovascular or neurological emergency detected. Requires immediate emergency services transfer.");
            recommendations.Add("Call emergency services (911 / 112 / 108) immediately. Do not wait for outpatient appointment.");
        }
        else if (Regex.IsMatch(text, @"(?i)\b(joint|swelling|sprain|ankle|knee|wrist|fracture|acute pain|twisted)\b"))
        {
            recommendations.Add("Initiate conservative RICE therapy (Rest, Ice application 15–20m every 2–3 hours, Compression wrap, Elevation above heart level).");
            recommendations.Add("Perform focused physical examination: assess joint stability, neurovascular status, and passive/active range of motion.");
            recommendations.Add("Consider plain radiographs (X-ray 3 views) to rule out occult fracture if Ottawa rules are positive or weight-bearing is impossible.");
            recommendations.Add("Recommend prompt orthopedic or sports medicine clinical consultation.");
        }
        else if (Regex.IsMatch(text, @"(?i)\b(headache|migraine)\b"))
        {
            recommendations.Add("Assess for neurological focal deficits, meningismus, visual aura, and blood pressure elevation.");
            recommendations.Add("Recommend dark, quiet environment hydration, and neurological follow-up if headaches are recurring or refractory.");
        }
        else if (Regex.IsMatch(text, @"(?i)\b(fever|cough|respiratory|sore throat)\b"))
        {
            recommendations.Add("Measure vital signs: core body temperature, resting pulse oximetry (SpO2), respiratory rate, and blood pressure.");
            recommendations.Add("Perform pulmonary auscultation and evaluate for signs of lower respiratory tract involvement.");
        }
        else
        {
            recommendations.Add("Conduct comprehensive clinical review of symptoms and correlate with historical vital trends.");
        }

        var severity = requiresEscalation ? "Critical" : warnings.Count > 0 ? "Urgent" : "Moderate";
        var summary = $"Diagnostic triage assessment completed: Symptom profile categorized as {severity} clinical priority.";

        var result = new SpecialistAnalysisResult(
            AgentName,
            Specialty,
            summary,
            recommendations,
            warnings,
            RequiresUrgentEscalation: requiresEscalation
        );

        _logger.LogInformation("DiagnosticTriageAgent completed assessment: Priority={Priority}, RecCount={Count}",
            severity, recommendations.Count);

        return Task.FromResult(result);
    }
}
