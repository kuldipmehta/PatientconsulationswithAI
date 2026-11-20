import pytest
from app.services.document_service import chunk_document_text, document_processor


@pytest.fixture(autouse=True)
def clean_job_store():
    """Ensure in-memory job registry is clean before each test."""
    document_processor.clear()
    yield
    document_processor.clear()


@pytest.mark.asyncio
async def test_unauthenticated_ingest_fails(unauthenticated_client):
    """Endpoints under /documents must reject requests missing X-API-Key."""
    payload = {
        "document_id": "DOC-999",
        "patient_id": 101,
        "document_type": "DischargeSummary",
        "content": "Patient was admitted for acute hypertensive emergency and stabilized.",
    }
    response = await unauthenticated_client.post("/api/v1/documents/ingest", json=payload)
    assert response.status_code == 401
    assert "Missing X-API-Key header" in response.json()["detail"]


@pytest.mark.asyncio
async def test_invalid_api_key_fails(unauthenticated_client):
    """Endpoints under /documents must reject requests with wrong X-API-Key."""
    headers = {"X-API-Key": "wrong-secret-token"}
    payload = {
        "document_id": "DOC-999",
        "patient_id": 101,
        "document_type": "DischargeSummary",
        "content": "Patient was admitted for acute hypertensive emergency and stabilized.",
    }
    response = await unauthenticated_client.post(
        "/api/v1/documents/ingest", json=payload, headers=headers
    )
    assert response.status_code == 401


@pytest.mark.asyncio
async def test_ingest_document_202_accepted(async_client):
    """Submitting a document responds with HTTP 202 Accepted and job tracking metadata."""
    payload = {
        "document_id": "DOC-2026-001",
        "patient_id": 42,
        "document_type": "LabReport",
        "content": (
            "Comprehensive Metabolic Panel: Glucose 108 mg/dL, BUN 18 mg/dL, "
            "Creatinine 0.9 mg/dL, Sodium 140 mEq/L, Potassium 4.1 mEq/L. "
            "Lipid Panel: Total Cholesterol 215 mg/dL, Triglycerides 160 mg/dL, "
            "HDL 45 mg/dL, LDL 138 mg/dL."
        ),
        "chunk_size": 100,
        "chunk_overlap": 20,
    }

    response = await async_client.post("/api/v1/documents/ingest", json=payload)
    assert response.status_code == 202
    data = response.json()

    assert data["job_id"].startswith("job-")
    assert data["status"] in ("queued", "processing", "completed")
    assert data["document_id"] == "DOC-2026-001"
    assert data["patient_id"] == 42
    assert "queued for asynchronous chunking" in data["message"]


@pytest.mark.asyncio
async def test_job_completion_and_chunk_inspection(async_client):
    """Verifies that background task executes chunking and produces valid embedding previews."""
    clinical_note = (
        "Patient presents with persistent cough and low-grade fever for 4 days. "
        "Auscultation reveals mild bilateral wheezing. Oxygen saturation is 96% on room air. "
        "Prescribed Albuterol inhaler 90mcg two puffs every 4 to 6 hours as needed. "
        "Advised to follow up in 5 days if symptoms do not improve or if fever exceeds 101F."
    )
    payload = {
        "document_id": "CLINICAL-NOTE-104",
        "patient_id": 88,
        "document_type": "ProgressNote",
        "content": clinical_note,
        "chunk_size": 120,
        "chunk_overlap": 25,
    }

    post_resp = await async_client.post("/api/v1/documents/ingest", json=payload)
    assert post_resp.status_code == 202
    job_id = post_resp.json()["job_id"]

    # In ASGI Transport, background tasks complete before or immediately upon request completion
    status_resp = await async_client.get(f"/api/v1/documents/jobs/{job_id}")
    assert status_resp.status_code == 200
    job_data = status_resp.json()

    assert job_data["job_id"] == job_id
    assert job_data["document_id"] == "CLINICAL-NOTE-104"
    assert job_data["patient_id"] == 88
    assert job_data["status"] == "completed"
    assert job_data["chunks_count"] > 1
    assert len(job_data["chunks"]) == job_data["chunks_count"]

    # Verify first chunk structure
    first_chunk = job_data["chunks"][0]
    assert first_chunk["chunk_index"] == 0
    assert len(first_chunk["content"]) > 0
    assert len(first_chunk["embedding_preview"]) == 8
    assert all(isinstance(val, float) for val in first_chunk["embedding_preview"])


@pytest.mark.asyncio
async def test_nonexistent_job_returns_404(async_client):
    """Querying an unknown job ID yields a 404 Not Found."""
    response = await async_client.get("/api/v1/documents/jobs/job-unknown-99999")
    assert response.status_code == 404
    assert "not found" in response.json()["detail"].lower()


@pytest.mark.asyncio
async def test_list_jobs_and_filter_by_patient(async_client):
    """Listing jobs supports all jobs and filtering by specific patient ID."""
    doc_a = {
        "document_id": "DOC-A",
        "patient_id": 501,
        "document_type": "EHRSummary",
        "content": "Patient 501 annual checkup records and immunization history.",
    }
    doc_b = {
        "document_id": "DOC-B",
        "patient_id": 501,
        "document_type": "LabReport",
        "content": "Patient 501 routine blood panel results within normal limits.",
    }
    doc_c = {
        "document_id": "DOC-C",
        "patient_id": 602,
        "document_type": "DischargeSummary",
        "content": "Patient 602 discharged following successful knee arthroscopy.",
    }

    await async_client.post("/api/v1/documents/ingest", json=doc_a)
    await async_client.post("/api/v1/documents/ingest", json=doc_b)
    await async_client.post("/api/v1/documents/ingest", json=doc_c)

    # Filter for patient 501
    resp_501 = await async_client.get("/api/v1/documents/jobs?patient_id=501")
    assert resp_501.status_code == 200
    jobs_501 = resp_501.json()
    assert len(jobs_501) == 2
    assert all(j["patient_id"] == 501 for j in jobs_501)

    # Filter for patient 602
    resp_602 = await async_client.get("/api/v1/documents/jobs?patient_id=602")
    assert resp_602.status_code == 200
    jobs_602 = resp_602.json()
    assert len(jobs_602) == 1
    assert jobs_602[0]["document_id"] == "DOC-C"

    # All jobs
    resp_all = await async_client.get("/api/v1/documents/jobs")
    assert resp_all.status_code == 200
    assert len(resp_all.json()) == 3


def test_chunk_document_text_sliding_window_unit():
    """Unit test for sliding-window chunker logic and boundary handling."""
    text = (
        "Alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu "
        "xi omicron pi rho sigma tau upsilon phi chi psi omega."
    )
    # Chunk size 40, overlap 10
    chunks = chunk_document_text(text, chunk_size=40, chunk_overlap=10)
    assert len(chunks) >= 3

    # Consecutive chunks should maintain indexing order
    for i, chunk in enumerate(chunks):
        assert chunk.chunk_index == i
        assert len(chunk.content) > 0
        assert len(chunk.embedding_preview) == 8

    # Empty string returns empty chunks list
    assert chunk_document_text("   ") == []


@pytest.mark.asyncio
async def test_validation_error_on_short_content(async_client):
    """Pydantic must reject content shorter than 10 characters with 422 Unprocessable Entity."""
    payload = {
        "document_id": "SHORT-1",
        "patient_id": 10,
        "document_type": "Note",
        "content": "Short",  # Only 5 characters, min_length is 10
    }
    response = await async_client.post("/api/v1/documents/ingest", json=payload)
    assert response.status_code == 422
