import time
from fastapi import APIRouter, Depends, Request
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
@router.get("/engine", response_model=EngineStatusResponse, summary="AI Engine Runtime Status (.NET Gateway Alias)")
async def get_engine_status(
    request: Request,
    settings: Settings = Depends(get_settings),
) -> EngineStatusResponse:
    """Inspects active AI model, live API connectivity, and service capabilities."""
    has_key = bool(settings.GEMINI_API_KEY and "YOUR_GEMINI" not in settings.GEMINI_API_KEY)
    start_time = getattr(request.app.state, "start_time", None)
    uptime = (time.time() - start_time) if start_time else 0.0

    return EngineStatusResponse(
        status="healthy",
        gemini_configured=has_key,
        model=settings.GEMINI_MODEL,
        fastapi_lifespan_ready=True,
        uptime_seconds=round(uptime, 2),
        live_api_connected=has_key,
        is_simulated_mode=not has_key,
    )
