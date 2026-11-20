from datetime import datetime, timezone
from enum import Enum
from typing import List, Optional
from pydantic import BaseModel, Field, field_validator, model_validator


class UrgencyLevel(str, Enum):
    ROUTINE = "Routine"
    MODERATE = "Moderate"
    URGENT = "Urgent"
    EMERGENCY = "Emergency"


class VitalSigns(BaseModel):
    systolic_bp: Optional[int] = Field(default=None, ge=40, le=300, description="Systolic blood pressure (mmHg)")
    diastolic_bp: Optional[int] = Field(default=None, ge=20, le=200, description="Diastolic blood pressure (mmHg)")
    heart_rate_bpm: Optional[int] = Field(default=None, ge=20, le=260, description="Heart rate / pulse in beats per minute")
    blood_glucose_mg_dl: Optional[float] = Field(default=None, ge=10.0, le=1000.0, description="Blood glucose level (mg/dL)")
    hba1c_percent: Optional[float] = Field(default=None, ge=2.0, le=20.0, description="Glycated hemoglobin percentage (%)")

    @model_validator(mode="after")
    def validate_bp_relationship(self) -> "VitalSigns":
        if self.systolic_bp is not None and self.diastolic_bp is not None:
            if self.systolic_bp <= self.diastolic_bp:
                raise ValueError("Systolic blood pressure must be strictly greater than diastolic blood pressure.")
        return self


class MedicationEntry(BaseModel):
    name: str = Field(..., description="Medication generic or trade name")
    dosage: Optional[str] = Field(default=None, description="Dosage (e.g., 20mg, 500mg)")
    frequency: Optional[str] = Field(default=None, description="Frequency (e.g., once daily, BID, PRN)")
    is_active: bool = Field(default=True, description="Whether medication is currently active")
    contraindication_warning: Optional[str] = Field(default=None, description="Safety alert if contraindicated")


class ClinicalExtractionRequest(BaseModel):
    raw_clinical_text: str = Field(
        ...,
        min_length=10,
        max_length=10000,
        description="Raw unstructured clinical narrative, consultation note, or doctor-patient dialogue",
    )
    patient_age: Optional[int] = Field(default=None, ge=0, le=130, description="Patient age in years")
    gender: Optional[str] = Field(default=None, description="Patient gender (Male, Female, Other)")
    known_allergies: List[str] = Field(default_factory=list, description="Known drug allergies")
    active_medications: List[str] = Field(default_factory=list, description="Current baseline medications")


class StructuredClinicalRecord(BaseModel):
    patient_pseudonym: str = Field(..., description="De-identified patient label (e.g. Patient #1)")
    vitals: VitalSigns = Field(default_factory=VitalSigns, description="Extracted numerical vital signs")
    diagnosed_conditions: List[str] = Field(default_factory=list, description="Documented diagnoses or clinical impressions")
    medications: List[MedicationEntry] = Field(default_factory=list, description="Extracted medications")
    allergies: List[str] = Field(default_factory=list, description="Documented allergies")
    urgency_level: UrgencyLevel = Field(default=UrgencyLevel.ROUTINE, description="Triage urgency classification")
    requires_immediate_escalation: bool = Field(default=False, description="Whether immediate emergency escalation is needed")
    clinical_summary: str = Field(..., description="Objective synthesis of extracted clinical facts")
    extracted_at_utc: datetime = Field(default_factory=lambda: datetime.now(timezone.utc))

    @model_validator(mode="after")
    def validate_emergency_escalation(self) -> "StructuredClinicalRecord":
        if self.urgency_level == UrgencyLevel.EMERGENCY and not self.requires_immediate_escalation:
            self.requires_immediate_escalation = True
        return self
