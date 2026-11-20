from fastapi import APIRouter
from app.api.v1.health import router as health_router
from app.api.v1.extraction import router as extraction_router
from app.api.v1.chat import router as chat_router

api_v1_router = APIRouter()
api_v1_router.include_router(health_router)
api_v1_router.include_router(extraction_router)
api_v1_router.include_router(chat_router)
