using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Infrastructure.Services.Ai.Agents;

public class PharmacovigilanceAgent : ISpecialistAgent
{
    private readonly ILogger<PharmacovigilanceAgent> _logger;

    public string AgentName => "Pharmacovigilance Specialist Agent";
    public AgentSpecialty Specialty => AgentSpecialty.Pharmacovigilance;

    public PharmacovigilanceAgent(ILogger<PharmacovigilanceAgent> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(ClinicalAgentContext context)
    {
        var combinedText = $"{context.UserMessage} {string.Join(" ", context.ActiveMedications)} {string.Join(" ", context.KnownAllergies)}";
        return Regex.IsMatch(combinedText, @"(?i)\b(prescrib|medicat|drug|dose|warfarin|ibuprofen|aspirin|naproxen|metformin|lisinopril|statin|atorvastatin|amoxicillin|penicillin|antibiotic|paracetamol|acetaminophen|nsaid|interaction|contraindicat|allerg)\b");
    }

    public Task<SpecialistAnalysisResult> AnalyzeAsync(ClinicalAgentContext context, CancellationToken cancellationToken = default)
    {
        var text = context.UserMessage;
        var warnings = new List<string>();
        var recommendations = new List<string>();

        var allMeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in context.ActiveMedications) allMeds.Add(m);

        // Detect mentions of medications in user text
        var mentionedMeds = ExtractMedicationMentions(text);
        foreach (var m in mentionedMeds) allMeds.Add(m);

        var allergies = context.KnownAllergies.ToList();
        if (text.Contains("allergic", StringComparison.OrdinalIgnoreCase) || text.Contains("allergy", StringComparison.OrdinalIgnoreCase))
        {
            if (Regex.IsMatch(text, @"(?i)penicillin")) allergies.Add("Penicillin");
            if (Regex.IsMatch(text, @"(?i)nsaid|ibuprofen|aspirin")) allergies.Add("NSAIDs");
        }

        // 1. Anticoagulant + NSAID Check
        var hasAnticoagulant = allMeds.Any(m => Regex.IsMatch(m, @"(?i)\b(warfarin|coumadin|apixaban|eliquis|rivaroxaban|xarelto|dabigatran|heparin)\b")) ||
                               Regex.IsMatch(text, @"(?i)\b(warfarin|coumadin|apixaban|eliquis|rivaroxaban|xarelto|dabigatran|heparin)\b");
        var hasNsaid = allMeds.Any(m => Regex.IsMatch(m, @"(?i)\b(ibuprofen|advil|motrin|naproxen|aleve|aspirin|diclofenac|ketorolac|meloxicam|nsaid)\b")) ||
                       Regex.IsMatch(text, @"(?i)\b(ibuprofen|advil|motrin|naproxen|aleve|aspirin|diclofenac|ketorolac|meloxicam|nsaid)\b");

        if (hasAnticoagulant && hasNsaid)
        {
            warnings.Add("MAJOR CONTRAINDICATION (Bleeding Risk): Co-administration of anticoagulants (e.g. Warfarin) with NSAIDs (e.g. Ibuprofen/Naproxen) severely elevates gastrointestinal hemorrhage and internal bleeding risk due to synergistic platelet inhibition and gastric mucosal injury.");
            recommendations.Add("Do NOT prescribe or recommend NSAIDs while patient is on anticoagulant therapy.");
            recommendations.Add("Recommended safe alternative analgesic: Acetaminophen (Paracetamol) at standard therapeutic dose (maximum 2g/24h in elderly or liver-monitored patients) or topical agents.");
        }

        // 2. ACE-Inhibitor + Potassium Check
        var hasAceInhibitor = allMeds.Any(m => Regex.IsMatch(m, @"(?i)\b(lisinopril|enalapril|ramipril|benazepril|captopril|losartan|valsartan)\b")) ||
                              Regex.IsMatch(text, @"(?i)\b(lisinopril|enalapril|ramipril|benazepril|captopril|losartan|valsartan)\b");
        var hasPotassium = allMeds.Any(m => Regex.IsMatch(m, @"(?i)\b(potassium|spironolactone|aldactone|k-dur|klor-con)\b")) ||
                           Regex.IsMatch(text, @"(?i)\b(potassium|spironolactone|aldactone|k-dur|klor-con)\b");

        if (hasAceInhibitor && hasPotassium)
        {
            warnings.Add("MODERATE-TO-SEVERE INTERACTION (Hyperkalemia Risk): ACE inhibitors/ARBs combined with potassium supplements or potassium-sparing diuretics significantly reduce renal potassium excretion, risking life-threatening cardiac arrhythmias.");
            recommendations.Add("Obtain baseline serum potassium and creatinine before initiation and monitor within 1–2 weeks.");
        }

        // 3. Statin + Macrolide Antibiotic Check
        var hasStatin = allMeds.Any(m => Regex.IsMatch(m, @"(?i)\b(atorvastatin|lipitor|simvastatin|zocor|lovastatin|rosuvastatin|crestor)\b")) ||
                        Regex.IsMatch(text, @"(?i)\b(atorvastatin|lipitor|simvastatin|zocor|lovastatin|rosuvastatin|crestor)\b");
        var hasMacrolide = allMeds.Any(m => Regex.IsMatch(m, @"(?i)\b(clarithromycin|biaxin|erythromycin)\b")) ||
                           Regex.IsMatch(text, @"(?i)\b(clarithromycin|biaxin|erythromycin)\b");

        if (hasStatin && hasMacrolide)
        {
            warnings.Add("SIGNIFICANT INTERACTION (Rhabdomyolysis Risk): Macrolide antibiotics strongly inhibit CYP3A4 metabolism of statins, elevating serum statin concentrations up to 10-fold and drastically increasing risk of severe myopathy or rhabdomyolysis.");
            recommendations.Add("Temporarily suspend statin therapy during antibiotic course, or prescribe non-interacting antibiotic (e.g. Azithromycin or Amoxicillin).");
        }

        // 4. Penicillin / Beta-Lactam Allergy Check
        var hasPenicillinAllergy = allergies.Any(a => Regex.IsMatch(a, @"(?i)penicillin|amoxicillin|ampicillin"));
        var wantsBetaLactam = Regex.IsMatch(text, @"(?i)\b(amoxicillin|penicillin|ampicillin|augmentin|piperacillin|cephalexin|keflex)\b");

        if (hasPenicillinAllergy && wantsBetaLactam)
        {
            warnings.Add("CRITICAL ALLERGY ALERT: Patient has documented Penicillin hypersensitivity. Beta-lactam antibiotics (e.g. Amoxicillin, Augmentin) carry high cross-reactivity risk and could provoke severe anaphylaxis.");
            recommendations.Add("Avoid all penicillin-derivative and beta-lactam antimicrobials.");
            recommendations.Add("Recommended safe alternative antimicrobials: Macrolides (Azithromycin), Doxycycline, or Fluoroquinolones based on culture sensitivities.");
        }

        // 5. Metformin Warnings
        if (Regex.IsMatch(text, @"(?i)metformin") && Regex.IsMatch(text, @"(?i)contrast|ct scan|angiography|renal"))
        {
            warnings.Add("CLINICAL SAFETY ALERT: Metformin must be temporarily discontinued 48 hours prior to procedures with iodinated contrast media to prevent contrast-induced lactic acidosis.");
            recommendations.Add("Hold Metformin before procedure and verify normal renal function (eGFR > 45 mL/min) prior to resumption.");
        }

        string summary;
        if (warnings.Count > 0)
        {
            summary = $"Identified {warnings.Count} pharmacological safety contraindication(s) requiring immediate clinician review.";
        }
        else
        {
            summary = "Medication profile reviewed. No severe drug-drug interactions or known allergy contraindications identified.";
            if (mentionedMeds.Count > 0)
            {
                recommendations.Add($"Continue monitoring patient clinical response and renal/hepatic tolerance for prescribed therapy.");
            }
        }

        var result = new SpecialistAnalysisResult(
            AgentName,
            Specialty,
            summary,
            recommendations,
            warnings,
            RequiresUrgentEscalation: warnings.Count > 0
        );

        _logger.LogInformation("PharmacovigilanceAgent completed analysis: {WarningCount} warnings, {RecCount} recommendations.",
            warnings.Count, recommendations.Count);

        return Task.FromResult(result);
    }

    private static List<string> ExtractMedicationMentions(string text)
    {
        var meds = new List<string>();
        var pattern = @"(?i)\b(warfarin|coumadin|apixaban|eliquis|rivaroxaban|xarelto|dabigatran|heparin|ibuprofen|advil|motrin|naproxen|aleve|aspirin|diclofenac|ketorolac|meloxicam|lisinopril|enalapril|ramipril|losartan|valsartan|potassium|spironolactone|atorvastatin|lipitor|simvastatin|zocor|rosuvastatin|clarithromycin|erythromycin|amoxicillin|penicillin|augmentin|metformin|paracetamol|acetaminophen|cetirizine|fluticasone)\b";
        var matches = Regex.Matches(text, pattern);
        foreach (Match m in matches)
        {
            if (!meds.Contains(m.Value, StringComparer.OrdinalIgnoreCase))
            {
                meds.Add(m.Value);
            }
        }
        return meds;
    }
}
