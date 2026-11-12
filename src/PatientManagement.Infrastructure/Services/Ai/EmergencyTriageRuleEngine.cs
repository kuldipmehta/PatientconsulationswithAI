using System.Text.RegularExpressions;
using PatientManagement.Core.DTOs.Ai;

namespace PatientManagement.Infrastructure.Services.Ai;

public sealed record EmergencyRuleResult(
    bool Triggered,
    string? RuleName,
    AiTriageResponseDto? Response);

public static class EmergencyTriageRuleEngine
{
    private sealed record EmergencyRule(
        string Name,
        string Specialty,
        Regex Pattern,
        string ImmediateGuidance,
        string[] RedFlags);

    private static readonly EmergencyRule[] Rules =
    [
        new(
            "Acute Cardiac / Chest Pain Red Flag",
            "Emergency Cardiology",
            new Regex(@"\b(chest pain|chest tightness|crushing chest|pain radiating to (left )?(arm|jaw|shoulder|back)|shortness of breath.{0,40}chest|chest.{0,40}sweating)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "EMERGENCY ALERT: Call emergency services immediately (911 / 112 / 108) or go to the nearest Emergency Department. Do NOT drive yourself. Suspected acute coronary syndrome requires immediate ECG and cardiac troponin evaluation.",
            [
                "Is the chest pain crushing, squeezing, or radiating to the left arm, jaw, or back?",
                "Are you experiencing diaphoresis (cold sweats), dizziness, or shortness of breath?",
                "Do you have a prior history of coronary artery disease or hypertension?"
            ]),
        new(
            "Acute Stroke / Neurological Red Flag (FAST)",
            "Emergency Neurology",
            new Regex(@"\b(stroke|facial droop|face drooping|arm weakness|one[- ]sided weakness|slurred speech|sudden confusion|sudden vision loss|worst headache of life|thunderclap headache)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "EMERGENCY ALERT: Call emergency services immediately (911 / 112 / 108). Possible acute stroke or intracranial emergency. Note the exact time symptoms started ('last known well') for thrombolytic eligibility.",
            [
                "What exact time were symptoms last normal?",
                "Is there sudden facial asymmetry, unilateral arm/leg weakness, or speech difficulty?",
                "Is there sudden severe headache with neck stiffness or visual loss?"
            ]),
        new(
            "Severe Respiratory Distress / Anaphylaxis",
            "Emergency Medicine",
            new Regex(@"\b(can'?t breathe|cannot breathe|severe shortness of breath|gasping for air|blue lips|cyanosis|throat swelling|tongue swelling|anaphylaxis|airway swelling)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "EMERGENCY ALERT: Call emergency services immediately (911 / 112 / 108). Suspected airway compromise, anaphylaxis, or acute respiratory failure. Use prescribed epinephrine auto-injector immediately if anaphylaxis is suspected.",
            [
                "Is the patient struggling to speak in full sentences or showing lip/tongue swelling?",
                "Was there a recent allergen exposure (food, medication, insect sting)?",
                "Is a rescue inhaler or epinephrine auto-injector available?"
            ]),
        new(
            "Self-Harm / Acute Psychiatric Crisis",
            "Emergency Psychiatry",
            new Regex(@"\b(suicid(e|al)|kill myself|end my life|self[- ]harm|overdose)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "EMERGENCY ALERT: Call emergency services immediately (911 / 112 / 988 Suicide & Crisis Lifeline) or proceed to the nearest Emergency Department. Do not leave the patient unattended.",
            [
                "Does the patient have an immediate plan or access to lethal means?",
                "Has any medication or substance already been ingested?",
                "Is a trusted family member or clinician physically with the patient right now?"
            ]),
        new(
            "Uncontrolled Hemorrhage / Trauma / Syncope",
            "Emergency Medicine",
            new Regex(@"\b(uncontrolled bleeding|severe bleeding|coughing up blood|vomiting blood|unconscious|unresponsive|seizure)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "EMERGENCY ALERT: Call emergency services immediately (911 / 112 / 108). Apply direct firm pressure to external bleeding if safe and keep airway clear.",
            [
                "Is the patient currently responsive and breathing normally?",
                "How long has active bleeding or seizure activity persisted?",
                "Is the patient taking anticoagulants (blood thinners)?"
            ])
    ];

    public static EmergencyRuleResult Evaluate(string symptoms, string? existingConditions = null, bool isSimulated = false)
    {
        var combined = $"{symptoms} {existingConditions}".Trim();
        if (string.IsNullOrWhiteSpace(combined))
        {
            return new EmergencyRuleResult(false, null, null);
        }

        foreach (var rule in Rules)
        {
            if (rule.Pattern.IsMatch(combined))
            {
                return new EmergencyRuleResult(
                    true,
                    rule.Name,
                    new AiTriageResponseDto
                    {
                        UrgencyLevel = "Emergency",
                        RecommendedSpecialty = rule.Specialty,
                        TriageNotes = rule.ImmediateGuidance,
                        RedFlagQuestions = rule.RedFlags.ToList(),
                        SuggestedNextSteps =
                        [
                            "Call emergency services (911 / 112 / 108) immediately.",
                            "Do not delay emergency transport waiting for outpatient booking.",
                            "Keep current medications list ready for emergency responders."
                        ],
                        RuleBasedEmergencyTriggered = true,
                        MatchedEmergencyRule = rule.Name,
                        IsSimulatedMode = isSimulated,
                        AnalyzedAtUtc = DateTime.UtcNow
                    });
            }
        }

        return new EmergencyRuleResult(false, null, null);
    }
}
