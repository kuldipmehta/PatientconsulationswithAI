# Multi-Model Evaluation Comparison

- **Date (UTC):** 2026-11-18 10:04 UTC
- **Models Evaluated:** `gemini-3.1-flash-lite`, `gemini-3-flash-preview`, `gemini-flash-lite-latest`
- **Task Types:** Extraction (4 cases), Summary (4 cases), Tool Selection (4 cases) × 3 runs each = **36 runs per model**
- **Scoring Criteria:** Valid JSON schema + allowed enum values, required must-contain clinical terms, zero PII leaks, and exact expected tool selection.

## Overall Comparison

| Model | Overall Pass Rate | Extraction | Summary | Tool Selection | p50 Latency (ms) | p95 Latency (ms) | Avg Tokens / Req | Total Tokens | Avg Cost / Req (USD) | Total Cost (USD) |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| `gemini-3.1-flash-lite` | **18/36 (50.0%)** | 12/12 (100%) | 6/12 (50%) | 0/12 (0%) | 623 | 4191 | 270 | 9,735 | $0.000059 | $0.002131 |
| `gemini-3-flash-preview` | **0/36 (0.0%)** | 0/12 (0%) | 0/12 (0%) | 0/12 (0%) | 319 | 382 | 0 | 0 | $0.000000 | $0.000000 |
| `gemini-flash-lite-latest` | **28/36 (77.8%)** | 10/12 (83%) | 11/12 (92%) | 7/12 (58%) | 4560 | 7587 | 783 | 28,199 | $0.000119 | $0.004299 |

## Notes
- Re-run manually with: `RUN_LIVE_MODEL_COMPARISON=1 GEMINI_API_KEY=<key> dotnet test --filter Category=ModelComparison`
- Provider-agnostic abstraction (`ILlmClient`) is also implemented for `GeminiLlmClient` and local `OllamaLlmClient` (`Llm:Provider=Ollama`).

## Key Findings & Observations

1. **Best Overall on Free Tier (`gemini-flash-lite-latest`):** Achieved **77.8% overall pass rate (28/36)** — **92%** on clinical summaries, **83%** on structured extraction, and **58%** on tool selection — at an average cost of **$0.000119/request** (`p50 = 4,560 ms`, `p95 = 7,587 ms`).
2. **Fastest Structured Extraction (`gemini-3.1-flash-lite`):** Achieved **100% (12/12)** on structured extraction with **623 ms p50 latency** and **$0.000059/request** before hitting per-minute quota limits during the later summary/tool calls.
3. **Quota Exhaustion Effect (`gemini-3-flash-preview` & Pro tier):** `gemini-3-flash-preview` returned `429 Too Many Requests` (`319 ms` p50) immediately during this run because its free-tier daily quota was exhausted by the preceding 90-run temperature experiment (`docs/summary-temperature-experiment.md`); `gemini-pro-latest` / `gemini-3.1-pro-preview` require a paid-tier Gemini API key (`429` on free tier).
4. **CI Safety:** Automated CI runs `ModelComparison_MockedCi_ScoresThreeModelsAcrossExtractionSummaryAndToolSelection` and `LlmClientAbstraction_GeminiAndOllamaClients_GenerateStructuredOutputAndTokens` offline with zero API cost.

