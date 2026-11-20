import re
import logging
from typing import List, Optional
import httpx

from app.config import Settings
from app.schemas.clinical import (
    ClinicalExtractionRequest,
    MedicationEntry,
    StructuredClinicalRecord,
    UrgencyLevel,
    VitalSigns,
)

logger = logging.getLogger("caresync.extractor")

BP_REGEX = re.compile(r"\b(?P<systolic>\d{2,3})\/(?P<diastolic>\d{2,3})\s*(?:mmhg)?\b", re.IGNORECASE)
PULSE_REGEX = re.compile(r"\b(?:pulse|hr|heart rate)[:\s]+(?P<rate>\d{2,3})\s*(?:bpm)?\b|\b(?P<rate2>\d{2,3})\s*bpm\b", re.IGNORECASE)
GLUCOSE_REGEX = re.compile(r"\b(?:glucose|blood sugar|fbs)[:\s]+(?P<glucose>\d{2,3}(?:\.\d+)?)\s*(?:mg\/dl)?\b|\b(?P<glucose2>\d{2,3}(?:\.\d+)?)\s*mg\/dl\b", re.IGNORECASE)
HBA1C_REGEX = re.compile(r"\b(?:hba1c|a1c)[:\s]+(?P<a1c>\d{1,2}(?:\.\d+)?)\s*%?\b|\b(?P<a1c2>\d{1,2}(?:\.\d+)?)\s*%\s*(?:hba1c|a1c)\b", re.IGNORECASE)

EMERGENCY_CARDIAC_REGEX = re.compile(
    r"\b(crushing chest|chest tightness|chest pain radiating|pain radiating to (left )?(arm|jaw)|shortness of breath.{0,25}chest)\b",
    re.IGNORECASE,
)
EMERGENCY_STROKE_REGEX = re.compile(
    r"\b(facial droop|face drooping|arm weakness|one[- ]sided weakness|slurred speech|thunderclap headache)\b",
    re.IGNORECASE,
)
EMERGENCY_AIRWAY_REGEX = re.compile(
    r"\b(anaphylaxis|throat swelling|tongue swelling|gasping for air|cannot breathe|cyanosis)\b",
    re.IGNORECASE,
)

KNOWN_CONDITIONS = [
    ("hypertension", "Essential Hypertension"),
    ("htn", "Essential Hypertension"),
    ("high blood pressure", "Essential Hypertension"),
    ("diabetes", "Type 2 Diabetes Mellitus"),
    ("t2d", "Type 2 Diabetes Mellitus"),
    ("asthma", "Bronchial Asthma"),
    ("copd", "Chronic Obstructive Pulmonary Disease"),
    ("atrial fibrillation", "Atrial Fibrillation"),
    ("afib", "Atrial Fibrillation"),
    ("coronary artery disease", "Coronary Artery Disease"),
    ("cad", "Coronary Artery Disease"),
    ("osteoarthritis", "Osteoarthritis"),
    ("bronchitis", "Acute Bronchitis"),
    ("sinusitis", "Acute Sinusitis"),
    ("abscess", "Dental Abscess"),
    ("pneumonia", "Community-Acquired Pneumonia"),
    ("covid", "COVID-19 Infection"),
]

KNOWN_MEDS = [
    ("warfarin", "Warfarin", True, False, False),
    ("coumadin", "Warfarin", True, False, False),
    ("apixaban", "Apixaban", True, False, False),
    ("eliquis", "Apixaban", True, False, False),
    ("ibuprofen", "Ibuprofen", False, True, False),
    ("advil", "Ibuprofen", False, True, False),
    ("motrin", "Ibuprofen", False, True, False),
    ("naproxen", "Naproxen", False, True, False),
    ("aleve", "Naproxen", False, True, False),
    ("aspirin", "Aspirin", False, True, False),
    ("diclofenac", "Diclofenac", False, True, False),
    ("meloxicam", "Meloxicam", False, True, False),
    ("penicillin", "Penicillin", False, False, True),
    ("amoxicillin", "Amoxicillin", False, False, True),
    ("augmentin", "Amoxicillin/Clavulanate", False, False, True),
    ("lisinopril", "Lisinopril", False, False, False),
    ("amlodipine", "Amlodipine", False, False, False),
    ("losartan", "Losartan", False, False, False),
    ("metformin", "Metformin", False, False, False),
    ("atorvastatin", "Atorvastatin", False, False, False),
    ("paracetamol", "Paracetamol / Acetaminophen", False, False, False),
    ("acetaminophen", "Acetaminophen", False, False, False),
]


class ClinicalExtractorService:
    def __init__(self, settings: Settings, http_client: Optional[httpx.AsyncClient] = None):
        self._settings = settings
        self._http_client = http_client

    async def extract_clinical_record(self, request: ClinicalExtractionRequest) -> StructuredClinicalRecord:
        """Extracts and validates structured clinical records using Pydantic v2 contracts."""
        text = request.raw_clinical_text

        # 1. Extract Numerical Vital Signs
        vitals = self._extract_vitals(text)

        # 2. Extract Conditions
        conditions = self._extract_conditions(text)

        # 3. Extract Documented Allergies
        allergies = list(request.known_allergies)
        allergy_match = re.search(r"(?i)\ballerg(?:y|ic|ies)\s*(?:to)?[:\s]+([a-zA-Z0-9,\s]+?)(?:\.|\n|$)", text)
        if allergy_match:
            for item in re.split(r"[,;]", allergy_match.group(1)):
                cleaned = item.strip()
                if cleaned and len(cleaned) > 2 and cleaned not in allergies:
                    allergies.append(cleaned.title())

        # 4. Extract Medications and Check Safety Contraindications
        medications = self._extract_medications(text, request, allergies)

        # 5. Evaluate Clinical Urgency and Emergency Escalation
        urgency, requires_escalation = self._evaluate_urgency(text, vitals)

        # 6. Synthesize Objective Clinical Summary
        age_gender = f"{request.patient_age or 'Adult'} {request.gender or 'Patient'}"
        summary_parts = [f"Clinical record for {age_gender}."]
        if vitals.systolic_bp and vitals.diastolic_bp:
            summary_parts.append(f"Recorded blood pressure: {vitals.systolic_bp}/{vitals.diastolic_bp} mmHg.")
        if vitals.hba1c_percent:
            summary_parts.append(f"Glycemic control HbA1c: {vitals.hba1c_percent}%.")
        if conditions:
            summary_parts.append(f"Documented conditions: {', '.join(conditions)}.")
        if medications:
            med_names = [f"{m.name} ({m.dosage})" if m.dosage else m.name for m in medications]
            summary_parts.append(f"Active medications: {', '.join(med_names)}.")
        if allergies:
            summary_parts.append(f"Documented allergies: {', '.join(allergies)}.")
        if requires_escalation:
            summary_parts.append("CRITICAL: Patient presents with acute red flags requiring immediate emergency medical evaluation.")

        record = StructuredClinicalRecord(
            patient_pseudonym=f"Patient #{abs(hash(text)) % 900 + 100}",
            vitals=vitals,
            diagnosed_conditions=conditions,
            medications=medications,
            allergies=allergies,
            urgency_level=urgency,
            requires_immediate_escalation=requires_escalation,
            clinical_summary=" ".join(summary_parts),
        )

        return record

    def _extract_vitals(self, text: str) -> VitalSigns:
        systolic, diastolic = None, None
        bp_match = BP_REGEX.search(text)
        if bp_match:
            sys_val = int(bp_match.group("systolic"))
            dia_val = int(bp_match.group("diastolic"))
            if 40 <= sys_val <= 300 and 20 <= dia_val <= 200 and sys_val > dia_val:
                systolic, diastolic = sys_val, dia_val

        heart_rate = None
        pulse_match = PULSE_REGEX.search(text)
        if pulse_match:
            val = pulse_match.group("rate") or pulse_match.group("rate2")
            if val and 20 <= int(val) <= 260:
                heart_rate = int(val)

        glucose = None
        gluc_match = GLUCOSE_REGEX.search(text)
        if gluc_match:
            val = gluc_match.group("glucose") or gluc_match.group("glucose2")
            if val and 10.0 <= float(val) <= 1000.0:
                glucose = float(val)

        hba1c = None
        a1c_match = HBA1C_REGEX.search(text)
        if a1c_match:
            val = a1c_match.group("a1c") or a1c_match.group("a1c2")
            if val and 2.0 <= float(val) <= 20.0:
                hba1c = float(val)

        return VitalSigns(
            systolic_bp=systolic,
            diastolic_bp=diastolic,
            heart_rate_bpm=heart_rate,
            blood_glucose_mg_dl=glucose,
            hba1c_percent=hba1c,
        )

    def _extract_conditions(self, text: str) -> List[str]:
        text_lower = text.lower()
        extracted = []
        for keyword, standard_name in KNOWN_CONDITIONS:
            if re.search(rf"\b{re.escape(keyword)}\b", text_lower):
                if standard_name not in extracted:
                    extracted.append(standard_name)
        return extracted

    def _extract_medications(
        self,
        text: str,
        request: ClinicalExtractionRequest,
        allergies: List[str],
    ) -> List[MedicationEntry]:
        text_lower = text.lower()
        all_meds = list(request.active_medications)

        # Detect patient baseline anticoagulant presence
        has_anticoagulant = any(
            re.search(rf"\b{re.escape(kw)}\b", text_lower) or any(kw in m.lower() for m in all_meds)
            for kw in ["warfarin", "coumadin", "apixaban", "eliquis", "rivaroxaban", "dabigatran", "heparin"]
        )

        has_penicillin_allergy = any(
            "penicillin" in a.lower() or "amox" in a.lower()
            for a in allergies
        )

        entries = []
        seen = set()

        for token, standard_name, is_anticoag, is_nsaid, is_betalactam in KNOWN_MEDS:
            if re.search(rf"\b{re.escape(token)}\b", text_lower):
                if standard_name in seen:
                    continue
                seen.add(standard_name)

                # Extract dosage if present immediately following name (e.g. 500mg, 20mg)
                dose_match = re.search(rf"\b{re.escape(token)}\s+(\d+(?:\.\d+)?\s*(?:mg|g|mcg|ml))\b", text_lower)
                dosage = dose_match.group(1).strip() if dose_match else None

                warning = None
                if is_nsaid and has_anticoagulant:
                    warning = "CRITICAL CONTRAINDICATION: NSAID prescribed to patient on anticoagulant therapy (high risk of GI hemorrhage)."
                elif is_betalactam and has_penicillin_allergy:
                    warning = "CRITICAL ALLERGY ALERT: Beta-lactam antibiotic prescribed to patient with documented penicillin hypersensitivity."

                entries.append(
                    MedicationEntry(
                        name=standard_name,
                        dosage=dosage,
                        is_active=True,
                        contraindication_warning=warning,
                    )
                )

        return entries

    def _evaluate_urgency(self, text: str, vitals: VitalSigns) -> tuple[UrgencyLevel, bool]:
        if (
            EMERGENCY_CARDIAC_REGEX.search(text)
            or EMERGENCY_STROKE_REGEX.search(text)
            or EMERGENCY_AIRWAY_REGEX.search(text)
        ):
            return UrgencyLevel.EMERGENCY, True

        # Check hypertensive crisis or severe vital signs
        if (vitals.systolic_bp and vitals.systolic_bp >= 180) or (vitals.diastolic_bp and vitals.diastolic_bp >= 120):
            return UrgencyLevel.URGENT, False

        if vitals.blood_glucose_mg_dl and (vitals.blood_glucose_mg_dl >= 350 or vitals.blood_glucose_mg_dl <= 50):
            return UrgencyLevel.URGENT, False

        if re.search(r"\b(acute|severe pain|fever.{0,20}103|fracture|infection)\b", text, re.IGNORECASE):
            return UrgencyLevel.MODERATE, False

        return UrgencyLevel.ROUTINE, False
