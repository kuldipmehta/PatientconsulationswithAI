from fastapi import APIRouter, Depends
from app.api.v1.health import router as health_router
from app.api.v1.extraction import router as extraction_router
from app.api.v1.chat import router as chat_router
from app.api.v1.documents import router as documents_router
from app.core.security import validate_api_key

api_v1_router = APIRouter()

# Public Probes (No API Key Required for Container Health Checks)
api_v1_router.include_router(health_router)

# Protected AI Microservice Endpoints (Require Valid Service API Key)
api_v1_router.include_router(
    extraction_router,
    dependencies=[Depends(validate_api_key)],
)
api_v1_router.include_router(
    chat_router,
    dependencies=[Depends(validate_api_key)],
)
api_v1_router.include_router(
    documents_router,
    dependencies=[Depends(validate_api_key)],
)
