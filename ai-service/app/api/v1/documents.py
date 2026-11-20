from typing import List, Optional
from fastapi import APIRouter, BackgroundTasks, HTTPException, Query, status

from app.schemas.document import (
    DocumentIngestRequest,
    DocumentIngestResponse,
    JobStatusResponse,
)
from app.services.document_service import document_processor

router = APIRouter(prefix="/documents", tags=["Clinical Document Ingestion"])


@router.post(
    "/ingest",
    response_model=DocumentIngestResponse,
    status_code=status.HTTP_202_ACCEPTED,
    summary="Queue a clinical document for background chunking and vector indexing",
    description=(
        "Accepts a clinical note, lab report, or discharge summary and immediately responds "
        "with HTTP 202 Accepted. Processing, sliding-window chunking, and embedding generation "
        "execute asynchronously in the background."
    ),
)
async def ingest_document(
    request: DocumentIngestRequest,
    background_tasks: BackgroundTasks,
) -> DocumentIngestResponse:
    """Queues a document for background asynchronous ingestion."""
    job = document_processor.create_job(request)

    # Schedule non-blocking background task
    background_tasks.add_task(
        document_processor.process_document_background,
        job.job_id,
        request,
    )

    return DocumentIngestResponse(
        job_id=job.job_id,
        status=job.status,
        message=f"Document '{request.document_id}' queued for asynchronous chunking and indexing.",
        document_id=request.document_id,
        patient_id=request.patient_id,
    )


@router.get(
    "/jobs/{job_id}",
    response_model=JobStatusResponse,
    summary="Get status and extracted chunks for an ingestion job",
    description="Retrieves the current execution lifecycle state, chunk count, and semantic chunks for a given job ID.",
)
async def get_job_status(job_id: str) -> JobStatusResponse:
    """Returns lifecycle state and output chunks for an ingestion job."""
    job = document_processor.get_job(job_id)
    if job is None:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Ingestion job '{job_id}' not found.",
        )
    return job


@router.get(
    "/jobs",
    response_model=List[JobStatusResponse],
    summary="List document ingestion jobs",
    description="Lists all recorded background ingestion jobs, with optional filtering by CareSync Patient ID.",
)
async def list_jobs(
    patient_id: Optional[int] = Query(
        default=None,
        description="Filter jobs by specific CareSync Patient ID",
    ),
) -> List[JobStatusResponse]:
    """Lists ingestion jobs across all documents or filtered by patient ID."""
    return document_processor.list_jobs(patient_id=patient_id)
