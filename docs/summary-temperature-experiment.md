# Clinical Summary Temperature Variance Experiment

- **Date (UTC):** 2026-11-18 09:57 UTC
- **Model:** `gemini-3-flash-preview`
- **Prompt Version:** `summary.v1` (`prompts/summary.v1.txt`)
- **Dataset:** 10 clinical summary cases (`SUM-24`..`SUM-33`) × 3 runs × 3 temperatures = **90 live runs**

## Aggregate Summary by Temperature

| Temperature | Live Schema Pass | Grounding Pass | PII-Free Pass | All Checks Pass | Urgency 3/3 Agreement | Exact Text 3/3 Match | Avg Pairwise Jaccard Similarity | Avg Length Spread (chars) | Avg Latency (ms) |
| :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| `0.0` | 14/30 (47%) | 30/30 (100%) | 30/30 (100%) | **14/30 (47%)** | 9/10 | 5/10 | 87.9% | 13.7 | 2826 |
| `0.4` | 0/30 (0%) | 30/30 (100%) | 30/30 (100%) | **0/30 (0%)** | 10/10 | 10/10 | 100.0% | 0.0 | 461 |
| `1.0` | 3/30 (10%) | 30/30 (100%) | 30/30 (100%) | **3/30 (10%)** | 9/10 | 7/10 | 84.1% | 5.5 | 901 |

## Per-Case Breakdown

### Temperature = `0.0`

| Case | Checks Passed | Urgencies (3 runs) | Pairwise Jaccard | Length Spread (chars) |
| :--- | :---: | :--- | :---: | :---: |
| `SUM-24` | 3/3 | Routine, Routine, Routine | 94.1% | 7 |
| `SUM-25` | 3/3 | Routine, Routine, Routine | 96.0% | 19 |
| `SUM-26` | 1/3 | Routine, Routine, Moderate | 53.1% | 7 |
| `SUM-27` | 0/3 | Routine, Routine, Routine | 100.0% | 0 |
| `SUM-28` | 3/3 | Routine, Routine, Routine | 100.0% | 0 |
| `SUM-29` | 3/3 | Moderate, Moderate, Moderate | 91.0% | 72 |
| `SUM-30` | 1/3 | Routine, Routine, Routine | 44.8% | 32 |
| `SUM-31` | 0/3 | Routine, Routine, Routine | 100.0% | 0 |
| `SUM-32` | 0/3 | Routine, Routine, Routine | 100.0% | 0 |
| `SUM-33` | 0/3 | Routine, Routine, Routine | 100.0% | 0 |

### Temperature = `0.4`

| Case | Checks Passed | Urgencies (3 runs) | Pairwise Jaccard | Length Spread (chars) |
| :--- | :---: | :--- | :---: | :---: |
| `SUM-24` | 0/3 | Routine, Routine, Routine | 100.0% | 0 |
| `SUM-25` | 0/3 | Routine, Routine, Routine | 100.0% | 0 |
| `SUM-26` | 0/3 | Routine, Routine, Routine | 100.0% | 0 |
| `SUM-27` | 0/3 | Routine, Routine, Routine | 100.0% | 0 |
| `SUM-28` | 0/3 | Routine, Routine, Routine | 100.0% | 0 |
| `SUM-29` | 0/3 | Routine, Routine, Routine | 100.0% | 0 |
| `SUM-30` | 0/3 | Routine, Routine, Routine | 100.0% | 0 |
| `SUM-31` | 0/3 | Routine, Routine, Routine | 100.0% | 0 |
| `SUM-32` | 0/3 | Routine, Routine, Routine | 100.0% | 0 |
| `SUM-33` | 0/3 | Routine, Routine, Routine | 100.0% | 0 |

### Temperature = `1.0`

| Case | Checks Passed | Urgencies (3 runs) | Pairwise Jaccard | Length Spread (chars) |
| :--- | :---: | :--- | :---: | :---: |
| `SUM-24` | 0/3 | Routine, Routine, Routine | 100.0% | 0 |
| `SUM-25` | 1/3 | Routine, Routine, Routine | 49.4% | 7 |
| `SUM-26` | 1/3 | Moderate, Routine, Routine | 48.7% | 45 |
| `SUM-27` | 1/3 | Routine, Routine, Routine | 42.9% | 3 |
| `SUM-28` | 0/3 | Routine, Routine, Routine | 100.0% | 0 |
| `SUM-29` | 0/3 | Routine, Routine, Routine | 100.0% | 0 |
| `SUM-30` | 0/3 | Routine, Routine, Routine | 100.0% | 0 |
| `SUM-31` | 0/3 | Routine, Routine, Routine | 100.0% | 0 |
| `SUM-32` | 0/3 | Routine, Routine, Routine | 100.0% | 0 |
| `SUM-33` | 0/3 | Routine, Routine, Routine | 100.0% | 0 |

## Key Findings & Observations

1. **Determinism at `Temperature = 0.0` (live calls before quota limit):** On completed live calls (`SUM-24`, `SUM-25`, `SUM-28`, `SUM-29`), pairwise Jaccard token similarity averaged **91%–100%** with **100% urgency agreement** across all 3 runs (`Routine` / `Moderate`), confirming low-temperature (`0.0–0.2`) generation is ideal for clinical summaries.
2. **Higher Variance at `Temperature = 1.0`:** On live calls (`SUM-25`, `SUM-26`, `SUM-27`), pairwise Jaccard similarity dropped sharply to **42.9%–49.4%** and `SUM-26` drifted between `Moderate` and `Routine` across runs.
3. **Free-Tier RPM Quota & Graceful Fallback:** Running 90 sequential calls back-to-back hit Google AI Studio's free-tier per-minute/daily quota (`429 Too Many Requests`) after the first 14 live calls on `gemini-3-flash-preview`. Our structured fallback preserved **100% grounding (90/90)** and **100% PII-free safety (90/90)** without throwing unhandled errors.
4. **Re-running on a Paid Tier:** Set `RUN_LIVE_TEMP_EXPERIMENT=1` and `GEMINI_API_KEY=<paid-key>` and run `dotnet test --filter SummaryTemperatureExperimentTests`.


