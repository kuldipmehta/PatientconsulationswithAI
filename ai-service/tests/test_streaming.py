import json
import pytest
import httpx


def parse_sse_events(raw_body: str) -> list[tuple[str, dict]]:
    events = []
    blocks = raw_body.strip().split("\n\n")
    for block in blocks:
        lines = [line.strip() for line in block.split("\n") if line.strip()]
        event_type = "message"
        data_json = {}
        for line in lines:
            if line.startswith("event:"):
                event_type = line[6:].strip()
            elif line.startswith("data:"):
                data_str = line[5:].strip()
                try:
                    data_json = json.loads(data_str)
                except Exception:
                    data_json = {"raw": data_str}
        if lines:
            events.append((event_type, data_json))
    return events


@pytest.mark.asyncio
async def test_stream_chat_returns_sse_protocol(async_client: httpx.AsyncClient):
    payload = {"message": "Summarize standard clinical management for essential hypertension."}
    response = await async_client.post("/api/v1/chat/stream", json=payload)

    assert response.status_code == 200
    assert "text/event-stream" in response.headers["content-type"]
    assert response.headers["cache-control"] == "no-cache"

    events = parse_sse_events(response.text)
    assert len(events) >= 3

    # First event should be 'meta'
    meta_event = events[0]
    assert meta_event[0] == "meta"
    assert "session_id" in meta_event[1]
    assert meta_event[1]["emergency_rule_triggered"] is False

    # Intermediate events are 'token'
    tokens = [ev[1].get("text", "") for ev in events if ev[0] == "token"]
    assert len(tokens) > 0
    full_text = "".join(tokens)
    assert len(full_text) > 20

    # Last event should be 'done'
    done_event = events[-1]
    assert done_event[0] == "done"
    assert done_event[1]["status"] == "completed"


@pytest.mark.asyncio
async def test_stream_emergency_red_flag_triggers_emergency_meta(async_client: httpx.AsyncClient):
    payload = {"message": "Patient experiencing crushing chest tightness radiating to the jaw with cold sweats."}
    response = await async_client.post("/api/v1/chat/stream", json=payload)

    assert response.status_code == 200
    events = parse_sse_events(response.text)

    meta_event = next((ev for ev in events if ev[0] == "meta"), None)
    assert meta_event is not None
    assert meta_event[1]["emergency_rule_triggered"] is True

    tokens = [ev[1].get("text", "") for ev in events if ev[0] == "token"]
    full_text = "".join(tokens)
    assert "EMERGENCY ALERT" in full_text
    assert "911" in full_text


@pytest.mark.asyncio
async def test_stream_warfarin_nsaid_contraindication_alert(async_client: httpx.AsyncClient):
    payload = {"message": "Patient is taking Warfarin daily. Can we prescribe Ibuprofen for knee pain?"}
    response = await async_client.post("/api/v1/chat/stream", json=payload)

    assert response.status_code == 200
    events = parse_sse_events(response.text)

    tokens = [ev[1].get("text", "") for ev in events if ev[0] == "token"]
    full_text = "".join(tokens)
    assert "CONTRAINDICATION" in full_text
    assert "bleeding" in full_text.lower()
