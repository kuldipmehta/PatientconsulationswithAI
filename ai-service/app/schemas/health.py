from datetime import datetime, timezone
from typing import List
from pydantic import BaseModel, Field


class HealthResponse(BaseModel):
    status: str = Field(default="healthy", description="Current health status (healthy, degraded, unhealthy)")
    service: str = Field(default="CareSync AI Engine", description="Microservice identifier")
    version: str = Field(..., description="Service semantic version")
    environment: str = Field(..., description="Active runtime environment")
    timestamp_utc: datetime = Field(default_factory=lambda: datetime.now(timezone.utc))


class EngineStatusResponse(BaseModel):
    engine_name: str = "CareSync Python AI Microservice"
    runtime: str = "FastAPI (ASGI) + Python 3.11"
    model: str
    live_api_connected: bool
    is_simulated_mode: bool
    capabilities: List[str] = [
        "asynchronous_token_streaming_sse",
        "pydantic_v2_structured_extraction",
        "vector_few_shot_exemplar_memory",
        "background_document_embedding",
        "clinical_safety_guardrails",
    ]
    timestamp_utc: datetime = Field(default_factory=lambda: datetime.now(timezone.utc))
