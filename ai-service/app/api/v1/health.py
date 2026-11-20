from fastapi import APIRouter, Depends
from app.config import Settings, get_settings
from app.schemas.health import HealthResponse, EngineStatusResponse

router = APIRouter(prefix="/health", tags=["Health & Diagnostics"])


@router.get("", response_model=HealthResponse, summary="Service Health Check")
async def get_health(settings: Settings = Depends(get_settings)) -> HealthResponse:
    """Readiness and liveness probe for orchestrators and load balancers."""
    return HealthResponse(
        status="healthy",
        service=settings.PROJECT_NAME,
        version=settings.VERSION,
        environment=settings.ENVIRONMENT,
    )


@router.get("/status", response_model=EngineStatusResponse, summary="AI Engine Runtime Status")
async def get_engine_status(settings: Settings = Depends(get_settings)) -> EngineStatusResponse:
    """Inspects active AI model, live API connectivity, and service capabilities."""
    has_key = bool(settings.GEMINI_API_KEY and "YOUR_GEMINI" not in settings.GEMINI_API_KEY)
    return EngineStatusResponse(
        model=settings.GEMINI_MODEL,
        live_api_connected=has_key,
        is_simulated_mode=not has_key,
    )
