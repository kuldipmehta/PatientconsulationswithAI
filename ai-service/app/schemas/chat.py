from typing import List, Optional
from pydantic import BaseModel, Field


class ChatStreamRequest(BaseModel):
    message: str = Field(
        ...,
        min_length=1,
        max_length=5000,
        description="Clinician query, medical inquiry, or patient dialogue",
    )
    session_id: Optional[str] = Field(
        default=None,
        description="Unique session ID to maintain conversation history across turns",
    )
    patient_context_id: Optional[int] = Field(
        default=None,
        description="Patient ID to scope clinical citations and knowledge retrieval",
    )
    model: Optional[str] = Field(
        default=None,
        description="Explicit model override (e.g. gemini-flash-latest)",
    )


class StreamTokenData(BaseModel):
    text: str = Field(..., description="Chunk of generated token text")


class StreamMetaData(BaseModel):
    session_id: str
    model_used: str
    is_simulated_mode: bool
    emergency_rule_triggered: bool = False
    disclaimer: str = (
        "AI Clinical Decision Support Only: Output must be verified by a licensed clinician "
        "before clinical action or scheduling."
    )
