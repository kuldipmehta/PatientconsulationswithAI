from datetime import datetime
from typing import Optional, List
from pydantic import BaseModel, Field


class DocumentChunk(BaseModel):
    """Represents an extracted semantic chunk from a clinical document."""
    chunk_index: int = Field(..., description="Zero-based index of this chunk within the document")
    content: str = Field(..., description="Text content of the chunk")
    char_start: int = Field(..., description="Character offset start in original document")
    char_end: int = Field(..., description="Character offset end in original document")
    embedding_preview: List[float] = Field(
        default_factory=list,
        description="Preview vector of the generated semantic embedding (e.g. 8-dim preview)",
    )


class DocumentIngestRequest(BaseModel):
    """Payload for submitting a clinical document for asynchronous chunking and indexing."""
    document_id: str = Field(..., min_length=1, description="Unique reference ID for this document")
    patient_id: int = Field(..., gt=0, description="Associated CareSync Patient ID")
    document_type: str = Field(
        ...,
        min_length=1,
        description="Type of clinical document (e.g. 'DischargeSummary', 'LabReport', 'EHRNote')",
    )
    content: str = Field(
        ...,
        min_length=10,
        description="Raw text content of the clinical document to be chunked and indexed",
    )
    chunk_size: int = Field(
        default=350,
        ge=50,
        le=4000,
        description="Target maximum character length per chunk",
    )
    chunk_overlap: int = Field(
        default=50,
        ge=0,
        le=500,
        description="Character overlap between consecutive chunks to preserve contextual continuity",
    )


class DocumentIngestResponse(BaseModel):
    """Immediate HTTP 202 response acknowledging that document ingestion has been queued."""
    job_id: str = Field(..., description="Asynchronous tracking UUID for this background ingestion job")
    status: str = Field(default="queued", description="Initial lifecycle state ('queued')")
    message: str = Field(..., description="Human-readable queue confirmation message")
    document_id: str = Field(..., description="Reference document ID")
    patient_id: int = Field(..., description="Associated Patient ID")


class JobStatusResponse(BaseModel):
    """Detailed status and chunking output of an asynchronous ingestion job."""
    job_id: str = Field(..., description="Job identifier")
    document_id: str = Field(..., description="Reference document ID")
    patient_id: int = Field(..., description="Associated Patient ID")
    document_type: str = Field(..., description="Document type")
    status: str = Field(
        ...,
        description="Current lifecycle state: 'queued' | 'processing' | 'completed' | 'failed'",
    )
    chunks_count: int = Field(default=0, description="Number of semantic chunks generated")
    chunks: List[DocumentChunk] = Field(default_factory=list, description="Extracted chunks list")
    created_at: datetime = Field(..., description="Timestamp when job was created")
    completed_at: Optional[datetime] = Field(default=None, description="Timestamp when job finished")
    error: Optional[str] = Field(default=None, description="Error message if status is 'failed'")
