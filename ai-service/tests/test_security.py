import pytest
import httpx


@pytest.mark.asyncio
async def test_protected_extraction_endpoint_without_key_returns_401(unauthenticated_client: httpx.AsyncClient):
    payload = {"raw_clinical_text": "Patient has elevated blood pressure 140/90 mmHg."}
    response = await unauthenticated_client.post("/api/v1/extract/clinical-entity", json=payload)
    assert response.status_code == 401
    data = response.json()
    assert "Missing X-API-Key header" in data["detail"]


@pytest.mark.asyncio
async def test_protected_streaming_endpoint_without_key_returns_401(unauthenticated_client: httpx.AsyncClient):
    payload = {"message": "Patient is asking about safe analgesics."}
    response = await unauthenticated_client.post("/api/v1/chat/stream", json=payload)
    assert response.status_code == 401
    data = response.json()
    assert "Missing X-API-Key header" in data["detail"]


@pytest.mark.asyncio
async def test_protected_endpoint_with_invalid_key_returns_401(unauthenticated_client: httpx.AsyncClient):
    payload = {"raw_clinical_text": "Patient has elevated blood pressure 140/90 mmHg."}
    headers = {"X-API-Key": "unauthorized-malicious-key-999"}
    response = await unauthenticated_client.post("/api/v1/extract/clinical-entity", json=payload, headers=headers)
    assert response.status_code == 401
    data = response.json()
    assert "Invalid X-API-Key credential" in data["detail"]


@pytest.mark.asyncio
async def test_protected_endpoint_with_valid_key_returns_200(async_client: httpx.AsyncClient):
    payload = {"raw_clinical_text": "Patient has elevated blood pressure 140/90 mmHg."}
    response = await async_client.post("/api/v1/extract/clinical-entity", json=payload)
    assert response.status_code == 200


@pytest.mark.asyncio
async def test_health_endpoints_remain_public_without_key(unauthenticated_client: httpx.AsyncClient):
    # Both root /health and versioned /api/v1/health must be accessible to container probes
    root_health = await unauthenticated_client.get("/health")
    assert root_health.status_code == 200
    assert root_health.json()["status"] == "healthy"

    v1_health = await unauthenticated_client.get("/api/v1/health")
    assert v1_health.status_code == 200
    assert v1_health.json()["status"] == "healthy"
