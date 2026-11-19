using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Infrastructure.Services.Ai.Agents;

public class SchedulingSpecialistAgent : ISpecialistAgent
{
    private readonly ILogger<SchedulingSpecialistAgent> _logger;

    public string AgentName => "Clinical Scheduling & Operations Agent";
    public AgentSpecialty Specialty => AgentSpecialty.Scheduling;

    public SchedulingSpecialistAgent(ILogger<SchedulingSpecialistAgent> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(ClinicalAgentContext context)
    {
        var text = context.UserMessage;
        return Regex.IsMatch(text, @"(?i)\b(schedul|book|appoint|visit|consult|slot|calendar|see\s*a\s*doctor|orthopedic|cardiolog|follow-up|urgent)\b");
    }

    public Task<SpecialistAnalysisResult> AnalyzeAsync(ClinicalAgentContext context, CancellationToken cancellationToken = default)
    {
        var text = context.UserMessage;
        var patientId = context.PatientId ?? (context.Patient?.Id ?? 1);

        int doctorId;
        string doctorName;
        string specialtyNeeded;

        if (Regex.IsMatch(text, @"(?i)\b(orthopedic|bone|joint|sprain|ankle|knee|wrist|fracture|muscle|arthr)\b"))
        {
            doctorId = 3;
            doctorName = "Dr. James Wilson, Orthopedic Specialist";
            specialtyNeeded = "Orthopedic Evaluation & Joint Assessment";
        }
        else if (Regex.IsMatch(text, @"(?i)\b(heart|cardio|chest|blood pressure|hypertension|ecg|palpitat)\b"))
        {
            doctorId = 2;
            doctorName = "Dr. Robert Sterling, Cardiologist";
            specialtyNeeded = "Cardiology Consultation & Vitals Review";
        }
        else
        {
            doctorId = 1;
            doctorName = "Dr. Sarah Jenkins, MD";
            specialtyNeeded = "General Medical Consultation & Follow-up";
        }

        var proposedDate = DateTime.UtcNow.Date.AddDays(1).AddHours(10);
        var actionId = Guid.NewGuid().ToString("N");

        var proposal = new PendingActionProposalDto
        {
            ActionId = actionId,
            ActionType = "ScheduleConsultation",
            PatientId = patientId,
            DoctorId = doctorId,
            DoctorName = doctorName,
            ProposedDateUtc = proposedDate,
            DurationMinutes = 30,
            DiagnosisOrReason = specialtyNeeded,
            Status = "Pending",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(2),
            ConfirmationPrompt = $"Book {specialtyNeeded} with {doctorName} on {proposedDate:dddd, MMM dd} at {proposedDate:HH:mm} UTC?"
        };

        var summary = $"Matched clinical requirement with {doctorName}. Prepared slot proposal on {proposedDate:yyyy-MM-dd HH:mm} UTC.";
        var recommendations = new List<string>
        {
            $"Propose appointment with {doctorName} for {specialtyNeeded}.",
            "Requires human-in-the-loop clinician or receptionist confirmation."
        };

        var result = new SpecialistAnalysisResult(
            AgentName,
            Specialty,
            summary,
            recommendations,
            Warnings: new List<string>(),
            RequiresUrgentEscalation: false,
            ActionProposal: proposal
        );

        _logger.LogInformation("SchedulingSpecialistAgent prepared appointment proposal for DoctorId={DocId}, PatientId={PatientId}",
            doctorId, patientId);

        return Task.FromResult(result);
    }
}
