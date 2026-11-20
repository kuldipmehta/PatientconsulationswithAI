import asyncio
import hashlib
import math
import uuid
from datetime import datetime, timezone
from typing import Dict, List, Optional

from app.schemas.document import (
    DocumentChunk,
    DocumentIngestRequest,
    JobStatusResponse,
)


def _compute_mock_embedding(text: str, dimensions: int = 8) -> List[float]:
    """
    Computes a deterministic, unit-normalized mock embedding vector for testing/simulation.
    Uses SHA-256 digests over text segments to produce consistent floats between -1.0 and 1.0.
    """
    raw_vals = []
    for i in range(dimensions):
        seed = f"{text}:{i}".encode("utf-8")
        h = hashlib.sha256(seed).hexdigest()
        # Convert hex prefix to signed float between -1.0 and 1.0
        val = (int(h[:6], 16) / 0xFFFFFF) * 2.0 - 1.0
        raw_vals.append(val)

    # Unit normalize
    norm = math.sqrt(sum(v * v for v in raw_vals)) or 1.0
    return [round(v / norm, 4) for v in raw_vals]


def chunk_document_text(
    text: str, chunk_size: int = 350, chunk_overlap: int = 50
) -> List[DocumentChunk]:
    """
    Splits clinical text into sliding-window chunks with configurable character overlap.
    Preserves word boundaries where feasible.
    """
    cleaned_text = text.strip()
    if not cleaned_text:
        return []

    # Ensure chunk_overlap is strictly smaller than chunk_size
    effective_overlap = min(chunk_overlap, chunk_size - 1)
    step = chunk_size - effective_overlap

    chunks: List[DocumentChunk] = []
    chunk_index = 0
    start = 0
    total_length = len(cleaned_text)

    while start < total_length:
        end = min(start + chunk_size, total_length)

        # If not at the very end of text, attempt to break on the last whitespace
        if end < total_length:
            last_space = cleaned_text.rfind(" ", start, end)
            if last_space > start + (chunk_size // 2):
                end = last_space

        chunk_str = cleaned_text[start:end].strip()
        if chunk_str:
            chunks.append(
                DocumentChunk(
                    chunk_index=chunk_index,
                    content=chunk_str,
                    char_start=start,
                    char_end=end,
                    embedding_preview=_compute_mock_embedding(chunk_str),
                )
            )
            chunk_index += 1

        if end >= total_length:
            break

        start += step
        if start >= end:
            start = end

    return chunks


class DocumentProcessorService:
    """
    Thread-safe asynchronous document ingestion and indexing service.
    Coordinates background job registration, chunking, and simulated semantic vectorization.
    """

    def __init__(self) -> None:
        self._jobs: Dict[str, JobStatusResponse] = {}
        self._lock = asyncio.Lock()

    def create_job(self, request: DocumentIngestRequest) -> JobStatusResponse:
        """Synchronously registers a new job in 'queued' state with a unique job ID."""
        job_id = f"job-{uuid.uuid4().hex[:12]}"
        now = datetime.now(timezone.utc)

        job = JobStatusResponse(
            job_id=job_id,
            document_id=request.document_id,
            patient_id=request.patient_id,
            document_type=request.document_type,
            status="queued",
            chunks_count=0,
            chunks=[],
            created_at=now,
            completed_at=None,
            error=None,
        )
        self._jobs[job_id] = job
        return job

    async def process_document_background(
        self, job_id: str, request: DocumentIngestRequest
    ) -> None:
        """
        Asynchronous background task invoked by FastAPI's BackgroundTasks queue.
        Transitions job through processing -> completed (or failed).
        """
        if job_id not in self._jobs:
            return

        try:
            # Transition to processing state
            job = self._jobs[job_id]
            job.status = "processing"

            # Cooperative yield for async runtime
            await asyncio.sleep(0.01)

            # Perform sliding-window chunking and embedding simulation
            chunks = chunk_document_text(
                text=request.content,
                chunk_size=request.chunk_size,
                chunk_overlap=request.chunk_overlap,
            )

            # Update job state to completed
            job.chunks = chunks
            job.chunks_count = len(chunks)
            job.status = "completed"
            job.completed_at = datetime.now(timezone.utc)

        except Exception as exc:  # pylint: disable=broad-except
            if job_id in self._jobs:
                job = self._jobs[job_id]
                job.status = "failed"
                job.error = str(exc)
                job.completed_at = datetime.now(timezone.utc)

    def get_job(self, job_id: str) -> Optional[JobStatusResponse]:
        """Retrieves a job by its tracking ID, or None if not found."""
        return self._jobs.get(job_id)

    def list_jobs(self, patient_id: Optional[int] = None) -> List[JobStatusResponse]:
        """Lists all registered jobs, optionally filtered by patient ID."""
        all_jobs = list(self._jobs.values())
        if patient_id is not None:
            return [j for j in all_jobs if j.patient_id == patient_id]
        return all_jobs

    def clear(self) -> None:
        """Clears all in-memory jobs (primarily used by test suites)."""
        self._jobs.clear()


# Global singleton instance for document processing
document_processor = DocumentProcessorService()
