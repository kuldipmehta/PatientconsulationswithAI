from fastapi import APIRouter, Depends, Request
from fastapi.responses import StreamingResponse

from app.config import Settings, get_settings
from app.schemas.chat import ChatStreamRequest
from app.services.streaming_service import StreamingChatService

router = APIRouter(prefix="/chat", tags=["Real-Time Token Streaming"])


def get_streaming_service(
    request: Request,
    settings: Settings = Depends(get_settings),
) -> StreamingChatService:
    http_client = getattr(request.app.state, "http_client", None)
    return StreamingChatService(settings=settings, http_client=http_client)


@router.post(
    "/stream",
    response_class=StreamingResponse,
    summary="Real-Time Server-Sent Events (SSE) Token Stream",
    description=(
        "Streams LLM generated tokens in real time via Server-Sent Events (SSE). "
        "Emits 'meta' (session ID, model info), sequential 'token' events, and 'done' when complete. "
        "Automatically cancels generation if the client disconnects."
    ),
)
async def stream_chat(
    payload: ChatStreamRequest,
    request: Request,
    service: StreamingChatService = Depends(get_streaming_service),
) -> StreamingResponse:
    generator = service.stream_chat(payload, request)
    return StreamingResponse(
        generator,
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
            "X-Accel-Buffering": "no",
        },
    )
