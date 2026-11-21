from contextlib import asynccontextmanager
import logging
import time
from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import RedirectResponse
import httpx

from app.api.v1.router import api_v1_router
from app.config import get_settings

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
)
logger = logging.getLogger("caresync.ai_service")


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Application lifespan context manager for startup and shutdown hooks."""
    settings = get_settings()
    logger.info("Initializing CareSync AI Microservice [%s]...", settings.ENVIRONMENT)

    # Initialize server start time for engine metrics
    app.state.start_time = time.time()

    # Initialize shared HTTP client pool for outbound AI calls
    app.state.http_client = httpx.AsyncClient(timeout=httpx.Timeout(60.0))
    logger.info("Outbound HTTP client connection pool established.")

    yield

    # Clean up resources on shutdown
    logger.info("Shutting down CareSync AI Microservice...")
    await app.state.http_client.aclose()
    logger.info("Shared HTTP client pool cleanly closed.")


def create_application() -> FastAPI:
    settings = get_settings()

    app = FastAPI(
        title=settings.PROJECT_NAME,
        version=settings.VERSION,
        description=(
            "CareSync High-Throughput Python AI Microservice.\n\n"
            "Provides asynchronous LLM token streaming (SSE), Pydantic v2 structured extraction, "
            "clinical guardrails verification, and dynamic vector exemplar memory."
        ),
        lifespan=lifespan,
        docs_url="/docs",
        redoc_url="/redoc",
        openapi_url="/openapi.json",
    )

    # Configure CORS
    app.add_middleware(
        CORSMiddleware,
        allow_origins=settings.CORS_ORIGINS,
        allow_credentials=True,
        allow_methods=["*"],
        allow_headers=["*"],
    )

    # Mount API v1 router
    app.include_router(api_v1_router, prefix=settings.API_V1_STR)

    @app.get("/", include_in_schema=False)
    async def root_redirect():
        """Redirect root requests to interactive Swagger documentation."""
        return RedirectResponse(url="/docs")

    @app.get("/health", include_in_schema=False)
    async def root_health():
        """Fast root-level health probe for container orchestrators."""
        return {"status": "healthy", "service": settings.PROJECT_NAME, "version": settings.VERSION}

    return app


app = create_application()
