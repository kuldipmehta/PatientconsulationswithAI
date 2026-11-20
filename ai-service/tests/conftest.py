import pytest
import pytest_asyncio
import httpx
from app.main import app


@pytest_asyncio.fixture
async def async_client():
    """Async HTTP client fixture configured for in-process ASGI testing."""
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://testserver") as client:
        yield client
