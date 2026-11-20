import pytest
import pytest_asyncio
import httpx
from app.config import get_settings
from app.main import app

settings = get_settings()


@pytest_asyncio.fixture
async def async_client():
    """Async HTTP client pre-configured with valid service credentials."""
    transport = httpx.ASGITransport(app=app)
    headers = {"X-API-Key": settings.AI_SERVICE_API_KEY}
    async with httpx.AsyncClient(transport=transport, base_url="http://testserver", headers=headers) as client:
        yield client


@pytest_asyncio.fixture
async def unauthenticated_client():
    """Async HTTP client without any authentication credentials."""
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://testserver") as client:
        yield client
