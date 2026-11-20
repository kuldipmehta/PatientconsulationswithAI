import pytest
import httpx


@pytest.mark.asyncio
async def test_root_redirect(async_client: httpx.AsyncClient):
    response = await async_client.get("/", follow_redirects=False)
    assert response.status_code == 307
    assert response.headers["location"] == "/docs"


@pytest.mark.asyncio
async def test_root_health(async_client: httpx.AsyncClient):
    response = await async_client.get("/health")
    assert response.status_code == 200
    data = response.json()
    assert data["status"] == "healthy"
    assert "CareSync" in data["service"]


@pytest.mark.asyncio
async def test_v1_health(async_client: httpx.AsyncClient):
    response = await async_client.get("/api/v1/health")
    assert response.status_code == 200
    data = response.json()
    assert data["status"] == "healthy"
    assert "timestamp_utc" in data
    assert data["environment"] == "development"


@pytest.mark.asyncio
async def test_v1_engine_status(async_client: httpx.AsyncClient):
    response = await async_client.get("/api/v1/health/status")
    assert response.status_code == 200
    data = response.json()
    assert "FastAPI (ASGI)" in data["runtime"]
    assert "capabilities" in data
    assert len(data["capabilities"]) > 0
    assert "asynchronous_token_streaming_sse" in data["capabilities"]
