# CareSync Python AI Microservice (FastAPI + ASGI)

High-throughput asynchronous Python microservice for clinical Generative AI operations, token streaming, Pydantic v2 structured extraction, and dynamic vector memory.

---

## Architecture & Technology Stack

- **Framework:** FastAPI 0.115+ (ASGI)
- **Runtime:** Python 3.11+
- **Data Validation & Schemas:** Pydantic v2
- **Server:** Uvicorn (Asynchronous HTTP/1.1 & WebSockets)
- **Async HTTP Client:** HTTPX
- **Testing:** Pytest + Pytest-Asyncio

---

## Directory Structure

```text
ai-service/
├── app/
│   ├── main.py          # ASGI application entrypoint, CORS & Lifespan
│   ├── config.py        # Pydantic BaseSettings (.env loader)
│   ├── api/
│   │   └── v1/
│   │       ├── router.py# Main API v1 router aggregator
│   │       └── health.py# Health & engine diagnostic endpoints
│   ├── schemas/         # Pydantic v2 data models
│   └── services/        # AI orchestration & model providers
├── tests/               # Pytest async test suite
├── requirements.txt     # Production dependencies
├── pytest.ini           # Pytest async configuration
├── .env.example         # Environment template
└── README.md
```

---

## Quickstart (Local Development)

### 1. Create & Activate Virtual Environment
```bash
cd ai-service
python -m venv venv

# Windows PowerShell:
.\venv\Scripts\Activate.ps1

# Linux / macOS:
source venv/bin/activate
```

### 2. Install Dependencies
```bash
pip install -r requirements.txt
```

### 3. Run the Development Server
```bash
uvicorn app.main:app --reload --port 8000
```

- **Interactive Swagger Documentation:** `http://127.0.0.1:8000/docs`
- **ReDoc Documentation:** `http://127.0.0.1:8000/redoc`
- **Health Endpoint:** `http://127.0.0.1:8000/api/v1/health`

---

## Running Automated Tests
```bash
pytest
```
