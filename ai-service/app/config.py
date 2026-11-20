from functools import lru_cache
from typing import List, Union
from pydantic import AnyHttpUrl, field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    PROJECT_NAME: str = "CareSync FastAPI AI Microservice"
    VERSION: str = "1.0.0"
    ENVIRONMENT: str = "development"
    API_V1_STR: str = "/api/v1"
    HOST: str = "127.0.0.1"
    PORT: int = 8000
    DEBUG: bool = True

    # CORS Allowlist
    CORS_ORIGINS: List[str] = [
        "http://localhost:4200",
        "http://localhost:5056",
        "http://127.0.0.1:4200",
        "http://127.0.0.1:5056",
    ]

    # Service Security
    AI_SERVICE_API_KEY: str = "dev-caresync-ai-secret-key-123"

    # LLM Settings
    GEMINI_API_KEY: str = ""
    GEMINI_MODEL: str = "gemini-flash-latest"

    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        case_sensitive=True,
        extra="ignore",
    )


@lru_cache()
def get_settings() -> Settings:
    """Returns cached application settings loaded from environment or .env file."""
    return Settings()
