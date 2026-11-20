from fastapi import APIRouter, Depends, Request
from app.config import Settings, get_settings
from app.schemas.clinical import ClinicalExtractionRequest, StructuredClinicalRecord
from app.services.extractor_service import ClinicalExtractorService

router = APIRouter(prefix="/extract", tags=["Structured Clinical Extraction"])


def get_extractor_service(
    request: Request,
    settings: Settings = Depends(get_settings),
) -> ClinicalExtractorService:
    http_client = getattr(request.app.state, "http_client", None)
    return ClinicalExtractorService(settings=settings, http_client=http_client)


@router.post(
    "/clinical-entity",
    response_model=StructuredClinicalRecord,
    summary="Extract Structured Clinical Record",
    description=(
        "Parses raw, unstructured clinical narratives into strictly typed, validated "
        "Pydantic v2 records (numerical vitals, diagnosed conditions, medications with contraindication "
        "safety alerts, and triage urgency)."
    ),
)
async def extract_clinical_entity(
    payload: ClinicalExtractionRequest,
    extractor: ClinicalExtractorService = Depends(get_extractor_service),
) -> StructuredClinicalRecord:
    return await extractor.extract_clinical_record(payload)
