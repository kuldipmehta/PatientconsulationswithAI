from datetime import datetime, timezone
from typing import List
from pydantic import BaseModel, Field


class HealthResponse(BaseModel):
    status: str = Field(default="healthy", description="Current health status (healthy, degraded, unhealthy)")
    service: str = Field(default="CareSync AI Engine", description="Microservice identifier")
    version: str = Field(..., description="Service semantic version")
    environment: str = Field(..., description="Active runtime environment")
    timestamp: str = Field(default_factory=lambda: datetime.now(timezone.utc).isoformat())
    timestamp_utc: datetime = Field(default_factory=lambda: datetime.now(timezone.utc))


class EngineStatusResponse(BaseModel):
    status: str = Field(default="healthy", description="Engine status")
    gemini_configured: bool = Field(default=False, description="Whether Gemini API is configured")
    model: str = Field(default="gemini-flash-latest", description="Primary AI model name")
    fastapi_lifespan_ready: bool = Field(default=True, description="Whether ASGI lifespan hooks completed")
    uptime_seconds: float = Field(default=0.0, description="Uptime in seconds since server start")
    engine_name: str = "CareSync Python AI Microservice"
    runtime: str = "FastAPI (ASGI) + Python 3.11"
    live_api_connected: bool = False
    is_simulated_mode: bool = True
    capabilities: List[str] = [
        "asynchronous_token_streaming_sse",
        "pydantic_v2_structured_extraction",
        "vector_few_shot_exemplar_memory",
        "background_document_embedding",
        "clinical_safety_guardrails",
    ]
    timestamp_utc: datetime = Field(default_factory=lambda: datetime.now(timezone.utc))
