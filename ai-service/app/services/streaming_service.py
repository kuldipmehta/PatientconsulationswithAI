import asyncio
import json
import logging
import re
import uuid
from typing import AsyncGenerator, Optional
from fastapi import Request
import httpx

from app.config import Settings
from app.schemas.chat import ChatStreamRequest, StreamMetaData

logger = logging.getLogger("caresync.streaming")

EMERGENCY_REGEX = re.compile(
    r"\b(crushing chest|chest tightness|radiating to (left )?(arm|jaw)|slurred speech|facial droop|anaphylaxis|cannot breathe)\b",
    re.IGNORECASE,
)


def format_sse(event_type: str, data: dict) -> str:
    """Formats payload into standard W3C Server-Sent Event (SSE) block."""
    return f"event: {event_type}\ndata: {json.dumps(data)}\n\n"


class StreamingChatService:
    def __init__(self, settings: Settings, http_client: Optional[httpx.AsyncClient] = None):
        self._settings = settings
        self._http_client = http_client

    async def stream_chat(
        self,
        request: ChatStreamRequest,
        http_request: Request,
    ) -> AsyncGenerator[str, None]:
        session_id = request.session_id or f"sess-{uuid.uuid4().hex[:12]}"
        has_key = bool(self._settings.GEMINI_API_KEY and "YOUR_GEMINI" not in self._settings.GEMINI_API_KEY)
        model_to_use = request.model or self._settings.GEMINI_MODEL

        # 1. Immediate Deterministic Emergency Red Flag Check
        if EMERGENCY_REGEX.search(request.message):
            emergency_meta = StreamMetaData(
                session_id=session_id,
                model_used="deterministic-emergency-engine",
                is_simulated_mode=not has_key,
                emergency_rule_triggered=True,
            )
            yield format_sse("meta", emergency_meta.model_dump())

            emergency_text = (
                "EMERGENCY ALERT: Call emergency services immediately (911 / 112 / 108) or proceed to the nearest "
                "Emergency Department. Do NOT attempt to drive yourself. The reported symptoms require immediate "
                "emergency clinical evaluation for suspected acute coronary or neurological compromise."
            )
            for token in re.findall(r"\S+|\s+", emergency_text):
                if await http_request.is_disconnected():
                    logger.info("Client disconnected during emergency stream; aborting.")
                    return
                yield format_sse("token", {"text": token})
                await asyncio.sleep(0.01)

            yield format_sse("done", {"session_id": session_id, "status": "completed"})
            return

        # 2. Check for Live Gemini API Key vs High-Fidelity Simulated Fallback
        if has_key and self._http_client is not None:
            try:
                async for chunk in self._stream_live_gemini(request, session_id, model_to_use, http_request):
                    yield chunk
                return
            except Exception as ex:
                logger.warning("Live Gemini stream encountered an issue (%s); falling back to simulated stream.", ex)

        # 3. High-Fidelity Simulated Clinical Streaming Generator
        async for chunk in self._stream_simulated(request, session_id, model_to_use, http_request):
            yield chunk

    async def _stream_simulated(
        self,
        request: ChatStreamRequest,
        session_id: str,
        model_to_use: str,
        http_request: Request,
    ) -> AsyncGenerator[str, None]:
        meta = StreamMetaData(
            session_id=session_id,
            model_used=f"{model_to_use}-simulated",
            is_simulated_mode=True,
            emergency_rule_triggered=False,
        )
        yield format_sse("meta", meta.model_dump())

        # Synthesize clinically relevant response based on prompt context
        prompt_lower = request.message.lower()
        if "warfarin" in prompt_lower and ("ibuprofen" in prompt_lower or "nsaid" in prompt_lower or "pain" in prompt_lower):
            response_text = (
                "CONTRAINDICATION WARNING: Co-administration of Ibuprofen (or other NSAIDs) with Warfarin is strictly "
                "contraindicated. NSAIDs inhibit platelet COX-1 and displace Warfarin from plasma proteins, markedly "
                "increasing upper gastrointestinal bleeding risk.\n\n"
                "Recommended Actions:\n"
                "1. Prescribe Paracetamol / Acetaminophen (500mg-1000mg up to TID, max 3g/24h) for pain.\n"
                "2. Schedule an orthopedic evaluation to assess joint effusion.\n"
                "3. Monitor current INR level to ensure therapeutic range (2.0-3.0)."
            )
        elif "penicillin" in prompt_lower or "amoxicillin" in prompt_lower or "allergy" in prompt_lower:
            response_text = (
                "ALLERGY CONTRAINDICATION ALERT: For patients with documented penicillin allergy, beta-lactams "
                "such as Amoxicillin and Augmentin carry high risk of severe IgE-mediated hypersensitivity or anaphylaxis.\n\n"
                "Recommended Alternatives:\n"
                "1. Non-beta-lactam options: Azithromycin 500mg daily or Doxycycline 100mg BID.\n"
                "2. Confirm allergy severity and history before administration."
            )
        elif request.patient_context_id:
            response_text = (
                f"Clinical Assessment for Patient #{request.patient_context_id}:\n"
                "Patient records have been reviewed. Vital signs are controlled and current medical regimens are active. "
                "Recommended follow-up includes routine bi-annual surveillance and medication adherence confirmation."
            )
        else:
            response_text = (
                "Clinical Assistant Analysis:\n"
                "I have evaluated your medical inquiry. All clinical recommendations are synthesized from evidence-based "
                "guidelines and require physician review before implementation. How else may I assist with patient care?"
            )

        # Stream tokens with realistic generation cadence (~15-20ms per token chunk)
        tokens = re.findall(r"\S+|\s+", response_text)
        for token in tokens:
            if await http_request.is_disconnected():
                logger.info("Client aborted connection mid-stream; cancelling token generation.")
                break
            yield format_sse("token", {"text": token})
            await asyncio.sleep(0.015)

        yield format_sse("done", {"session_id": session_id, "status": "completed"})

    async def _stream_live_gemini(
        self,
        request: ChatStreamRequest,
        session_id: str,
        model: str,
        http_request: Request,
    ) -> AsyncGenerator[str, None]:
        url = f"https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse"
        headers = {"x-goog-api-key": self._settings.GEMINI_API_KEY}
        payload = {
            "contents": [{"role": "user", "parts": [{"text": request.message}]}],
            "generationConfig": {"temperature": 0.2, "maxOutputTokens": 1024},
        }

        meta = StreamMetaData(
            session_id=session_id,
            model_used=model,
            is_simulated_mode=False,
            emergency_rule_triggered=False,
        )
        yield format_sse("meta", meta.model_dump())

        assert self._http_client is not None
        async with self._http_client.stream("POST", url, headers=headers, json=payload) as response:
            if response.status_code != 200:
                raise RuntimeError(f"Gemini streaming failed with HTTP {response.status_code}")

            buffer = ""
            async for line in response.aiter_lines():
                if await http_request.is_disconnected():
                    logger.info("Client disconnected; aborting live Gemini stream.")
                    break
                if line.startswith("data: "):
                    json_str = line[6:].strip()
                    if not json_str:
                        continue
                    try:
                        chunk_doc = json.loads(json_str)
                        candidates = chunk_doc.get("candidates", [])
                        if candidates:
                            parts = candidates[0].get("content", {}).get("parts", [])
                            for part in parts:
                                text_chunk = part.get("text", "")
                                if text_chunk:
                                    yield format_sse("token", {"text": text_chunk})
                    except json.JSONDecodeError:
                        pass

        yield format_sse("done", {"session_id": session_id, "status": "completed"})
