import pytest
import httpx
from pydantic import ValidationError
from app.schemas.clinical import VitalSigns, UrgencyLevel, StructuredClinicalRecord


@pytest.mark.asyncio
async def test_extract_vitals_and_medications(async_client: httpx.AsyncClient):
    payload = {
        "raw_clinical_text": (
            "Patient is a 68-year-old male with hypertension and diabetes. "
            "Vital signs today: BP 120/80 mmHg, pulse 72 bpm, HbA1c 6.5%. "
            "Currently prescribed Lisinopril 20mg daily and Metformin 500mg BID."
        ),
        "patient_age": 68,
        "gender": "Male",
    }

    response = await async_client.post("/api/v1/extract/clinical-entity", json=payload)
    assert response.status_code == 200
    data = response.json()

    assert data["vitals"]["systolic_bp"] == 120
    assert data["vitals"]["diastolic_bp"] == 80
    assert data["vitals"]["heart_rate_bpm"] == 72
    assert data["vitals"]["hba1c_percent"] == 6.5

    assert "Essential Hypertension" in data["diagnosed_conditions"]
    assert "Type 2 Diabetes Mellitus" in data["diagnosed_conditions"]

    med_names = [m["name"] for m in data["medications"]]
    assert "Lisinopril" in med_names
    assert "Metformin" in med_names

    assert data["urgency_level"] == "Routine"
    assert data["requires_immediate_escalation"] is False


@pytest.mark.asyncio
async def test_extract_emergency_red_flag(async_client: httpx.AsyncClient):
    payload = {
        "raw_clinical_text": (
            "Patient rushed in with acute crushing chest pressure radiating to left arm "
            "and profuse diaphoresis with severe shortness of breath."
        ),
        "patient_age": 55,
        "gender": "Male",
    }

    response = await async_client.post("/api/v1/extract/clinical-entity", json=payload)
    assert response.status_code == 200
    data = response.json()

    assert data["urgency_level"] == "Emergency"
    assert data["requires_immediate_escalation"] is True


@pytest.mark.asyncio
async def test_contraindication_warning_warfarin_nsaid(async_client: httpx.AsyncClient):
    payload = {
        "raw_clinical_text": (
            "Patient has chronic atrial fibrillation and takes Warfarin daily. "
            "Patient complains of severe knee pain and requests Ibuprofen 400mg."
        ),
        "patient_age": 72,
        "gender": "Female",
        "active_medications": ["Warfarin"],
    }

    response = await async_client.post("/api/v1/extract/clinical-entity", json=payload)
    assert response.status_code == 200
    data = response.json()

    ibuprofen = next((m for m in data["medications"] if m["name"] == "Ibuprofen"), None)
    assert ibuprofen is not None
    assert ibuprofen["contraindication_warning"] is not None
    assert "anticoagulant" in ibuprofen["contraindication_warning"].lower()


@pytest.mark.asyncio
async def test_contraindication_warning_penicillin_allergy(async_client: httpx.AsyncClient):
    payload = {
        "raw_clinical_text": (
            "Patient has documented allergy to penicillin with rash and hives. "
            "For acute bacterial sinusitis, prescribe Amoxicillin 500mg."
        ),
        "patient_age": 34,
        "gender": "Female",
        "known_allergies": ["Penicillin"],
    }

    response = await async_client.post("/api/v1/extract/clinical-entity", json=payload)
    assert response.status_code == 200
    data = response.json()

    amoxicillin = next((m for m in data["medications"] if m["name"] == "Amoxicillin"), None)
    assert amoxicillin is not None
    assert amoxicillin["contraindication_warning"] is not None
    assert "penicillin" in amoxicillin["contraindication_warning"].lower()


@pytest.mark.asyncio
async def test_pydantic_validation_rejects_invalid_bp_relationship():
    # Systolic must be strictly greater than diastolic
    with pytest.raises(ValidationError) as exc_info:
        VitalSigns(systolic_bp=75, diastolic_bp=120)

    assert "Systolic blood pressure must be strictly greater" in str(exc_info.value)


@pytest.mark.asyncio
async def test_short_clinical_text_triggers_http_422(async_client: httpx.AsyncClient):
    # Field has min_length=10
    payload = {"raw_clinical_text": "too short"}
    response = await async_client.post("/api/v1/extract/clinical-entity", json=payload)
    assert response.status_code == 422
