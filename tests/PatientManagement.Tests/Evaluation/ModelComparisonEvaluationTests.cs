using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.DTOs.Consultations;
using PatientManagement.Core.DTOs.Doctors;
using PatientManagement.Core.DTOs.Patients;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Data;
using PatientManagement.Infrastructure.Services;
using PatientManagement.Infrastructure.Services.Ai;
using Xunit;

namespace PatientManagement.Tests.Evaluation;

public class ModelComparisonEvaluationTests
{
    private static readonly string[] ComparisonModels =
    [
        "gemini-2.5-flash-lite",
        "gemini-2.5-flash",
        "gemini-2.5-pro"
    ];

    private static readonly string[] LiveComparisonModels =
    [
        "gemini-3.1-flash-lite",
        "gemini-3-flash-preview",
        "gemini-flash-lite-latest"
    ];

    private const int RunsPerCase = 3;

    [Fact]
    public async Task ModelComparison_MockedCi_ScoresThreeModelsAcrossExtractionSummaryAndToolSelection()
    {
        var dataset = await LoadDatasetAsync();
        var results = new List<ModelEvalAggregate>();

        foreach (var model in ComparisonModels)
        {
            var handler = CreateDeterministicMockHandler();
            var aggregate = await EvaluateModelAsync(
                model,
                apiKey: "mock-ci-key",
                httpClient: new HttpClient(handler),
                dataset,
                maxSummaryCases: 4,
                delayMsBetweenCalls: 0);

            results.Add(aggregate);
        }

        // Assert: All 3 models evaluated across all 3 task types (4 extraction + 4 summary + 4 tool = 12 cases x 3 runs = 36 runs each)
        results.Should().HaveCount(3);
        foreach (var agg in results)
        {
            agg.TotalRuns.Should().Be(36);
            agg.PassRate.Should().Be(1.0);
            agg.ExtractionPassRate.Should().Be(1.0);
            agg.SummaryPassRate.Should().Be(1.0);
            agg.ToolSelectionPassRate.Should().Be(1.0);
            agg.TotalTokens.Should().BeGreaterThan(0);
            agg.AvgCostPerRequestUsd.Should().BeGreaterThan(0m);
            agg.P95LatencyMs.Should().BeGreaterThanOrEqualTo(agg.P50LatencyMs);
        }

        // Pro cost per request should exceed Flash which should exceed Flash-Lite under Google's pricing
        var lite = results.Single(r => r.Model == "gemini-2.5-flash-lite");
        var flash = results.Single(r => r.Model == "gemini-2.5-flash");
        var pro = results.Single(r => r.Model == "gemini-2.5-pro");
        pro.AvgCostPerRequestUsd.Should().BeGreaterThan(flash.AvgCostPerRequestUsd);
        flash.AvgCostPerRequestUsd.Should().BeGreaterThan(lite.AvgCostPerRequestUsd);
    }

    [Fact]
    public async Task LlmClientAbstraction_GeminiAndOllamaClients_GenerateStructuredOutputAndTokens()
    {
        // 1. GeminiLlmClient via ILlmClient
        HttpRequestMessage? capturedGeminiReq = null;
        var geminiHandler = new StubHandler(req =>
        {
            capturedGeminiReq = req;
            var json = JsonSerializer.Serialize(new
            {
                candidates = new[] { new { content = new { parts = new[] { new { text = "{\"status\":\"ok\"}" } } } } },
                usageMetadata = new { promptTokenCount = 120, candidatesTokenCount = 30, totalTokenCount = 150 }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var geminiConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"] = "unit-test-key",
                ["Gemini:Model"] = "gemini-2.5-flash"
            })
            .Build();

        ILlmClient geminiClient = new GeminiLlmClient(new HttpClient(geminiHandler), geminiConfig);
        var geminiRes = await geminiClient.GenerateAsync(new LlmGenerationRequest
        {
            Model = "gemini-2.5-flash",
            SystemPrompt = "Return JSON.",
            UserPrompt = "Health check",
            ExpectJson = true
        });

        geminiRes.Provider.Should().Be("GoogleGemini");
        geminiRes.Text.Should().Be("{\"status\":\"ok\"}");
        geminiRes.PromptTokens.Should().Be(120);
        geminiRes.CompletionTokens.Should().Be(30);
        geminiRes.TotalTokens.Should().Be(150);
        capturedGeminiReq!.RequestUri!.ToString().Should().NotContain("key=");
        capturedGeminiReq.Headers.GetValues("x-goog-api-key").Should().ContainSingle().Which.Should().Be("unit-test-key");

        // 2. OllamaLlmClient via ILlmClient (non-Gemini local provider)
        string? capturedOllamaBody = null;
        var ollamaHandler = new StubHandler(req =>
        {
            capturedOllamaBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var json = JsonSerializer.Serialize(new
            {
                model = "llama3.2",
                message = new { role = "assistant", content = "{\"status\":\"ok\"}" },
                prompt_eval_count = 95,
                eval_count = 25
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var ollamaConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ollama:BaseUrl"] = "http://localhost:11434",
                ["Ollama:Model"] = "llama3.2"
            })
            .Build();

        ILlmClient ollamaClient = new OllamaLlmClient(new HttpClient(ollamaHandler), ollamaConfig);
        var ollamaRes = await ollamaClient.GenerateAsync(new LlmGenerationRequest
        {
            Model = "llama3.2",
            SystemPrompt = "Return JSON.",
            UserPrompt = "Health check",
            ExpectJson = true
        });

        ollamaRes.Provider.Should().Be("Ollama");
        ollamaRes.ModelUsed.Should().Be("llama3.2");
        ollamaRes.Text.Should().Be("{\"status\":\"ok\"}");
        ollamaRes.PromptTokens.Should().Be(95);
        ollamaRes.CompletionTokens.Should().Be(25);
        ollamaRes.TotalTokens.Should().Be(120);
        capturedOllamaBody.Should().Contain("\"format\":\"json\"");
    }

    [Fact]
    [Trait("Category", "ModelComparison")]
    public async Task RunLiveThreeModelComparison_WhenEnabled_WritesMarkdownReport()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_LIVE_MODEL_COMPARISON"), "1", StringComparison.Ordinal))
        {
            return; // Manual live benchmark only; skipped in CI
        }

        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        apiKey.Should().NotBeNullOrWhiteSpace("GEMINI_API_KEY must be set when RUN_LIVE_MODEL_COMPARISON=1");

        var dataset = await LoadDatasetAsync();
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        var aggregates = new List<ModelEvalAggregate>();
        foreach (var model in LiveComparisonModels)
        {
            var agg = await EvaluateModelAsync(
                model,
                apiKey!,
                httpClient,
                dataset,
                maxSummaryCases: 4,
                delayMsBetweenCalls: 150);
            aggregates.Add(agg);
        }

        var md = BuildMarkdownReport(aggregates);
        var repoRoot = FindRepoRoot();
        var docsDir = Path.Combine(repoRoot, "docs");
        Directory.CreateDirectory(docsDir);
        await File.WriteAllTextAsync(Path.Combine(docsDir, "model-comparison.md"), md);
    }

    private static async Task<ModelEvalAggregate> EvaluateModelAsync(
        string model,
        string apiKey,
        HttpClient httpClient,
        ComparisonDataset dataset,
        int maxSummaryCases,
        int delayMsBetweenCalls)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"ModelCmp_{model}_{Guid.NewGuid():N}")
            .Options;
        await using var db = new ApplicationDbContext(options);

        var patientMock = new Mock<IPatientService>();
        var consultMock = new Mock<IConsultationService>();
        var doctorMock = new Mock<IDoctorService>();
        var loggerMock = new Mock<ILogger<GeminiPatientAgentService>>();

        // Seed mocks for tool execution
        patientMock.Setup(s => s.GetPatientsAsync(It.IsAny<PatientFilterParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<PatientSummaryDto>(
                [new PatientSummaryDto { Id = 101, FirstName = "Alice", LastName = "HiddenName", Age = 42, Gender = "Female" }],
                1, 1, 10));

        doctorMock.Setup(d => d.GetDoctorsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DoctorDto { Id = 1, FullName = "Dr. Sarah Jenkins, MD", Specialty = "Internal Medicine" }]);

        doctorMock.Setup(d => d.GetAvailabilityAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DoctorAvailabilityResponseDto
            {
                DoctorId = 1,
                DoctorName = "Dr. Sarah Jenkins, MD",
                Specialty = "Internal Medicine",
                DateUtc = new DateTime(2026, 11, 25, 0, 0, 0, DateTimeKind.Utc),
                Slots =
                [
                    new TimeSlotDto
                    {
                        StartUtc = new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc),
                        EndUtc = new DateTime(2026, 11, 25, 10, 30, 0, DateTimeKind.Utc),
                        IsAvailable = true
                    }
                ]
            });

        // Pin AllowedModels strictly to this single model so fallback never masks a model's real behavior
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"] = apiKey,
                ["Gemini:Model"] = model,
                ["Gemini:AllowedModels:0"] = model,
                ["Gemini:Features:Summary:Temperature"] = "0.2",
                ["Gemini:Features:Summary:TopP"] = "0.9",
                ["Gemini:Features:Summary:MaxOutputTokens"] = "1024",
                ["Gemini:Features:Summary:PromptVersion"] = "summary.v1",
                ["Gemini:Features:Triage:Temperature"] = "0.1",
                ["Gemini:Features:Triage:TopP"] = "0.85",
                ["Gemini:Features:Triage:MaxOutputTokens"] = "1024",
                ["Gemini:Features:Triage:PromptVersion"] = "triage.v1",
                ["Gemini:Features:Chat:Temperature"] = "0.2",
                ["Gemini:Features:Chat:TopP"] = "0.9",
                ["Gemini:Features:Chat:MaxOutputTokens"] = "1024",
                ["Gemini:Features:Chat:PromptVersion"] = "chat.v1",
                ["Pricing:gemini-2.5-flash-lite:InputPerMillion"] = "0.10",
                ["Pricing:gemini-2.5-flash-lite:OutputPerMillion"] = "0.40",
                ["Pricing:gemini-flash-lite-latest:InputPerMillion"] = "0.10",
                ["Pricing:gemini-flash-lite-latest:OutputPerMillion"] = "0.40",
                ["Pricing:gemini-3.1-flash-lite:InputPerMillion"] = "0.10",
                ["Pricing:gemini-3.1-flash-lite:OutputPerMillion"] = "0.40",
                ["Pricing:gemini-2.5-flash:InputPerMillion"] = "0.30",
                ["Pricing:gemini-2.5-flash:OutputPerMillion"] = "2.50",
                ["Pricing:gemini-3-flash-preview:InputPerMillion"] = "0.50",
                ["Pricing:gemini-3-flash-preview:OutputPerMillion"] = "3.00",
                ["Pricing:gemini-2.5-pro:InputPerMillion"] = "1.25",
                ["Pricing:gemini-2.5-pro:OutputPerMillion"] = "10.00",
                ["Pricing:gemini-pro-latest:InputPerMillion"] = "1.25",
                ["Pricing:gemini-pro-latest:OutputPerMillion"] = "10.00"
            })
            .Build();

        var agent = new GeminiPatientAgentService(
            httpClient, db, config,
            patientMock.Object, consultMock.Object, doctorMock.Object, loggerMock.Object);

        int extPassed = 0, extTotal = 0;
        int sumPassed = 0, sumTotal = 0;
        int toolPassed = 0, toolTotal = 0;

        // 1. Extraction cases (via structured triage extraction)
        foreach (var ext in dataset.ExtractionCases)
        {
            for (int r = 0; r < RunsPerCase; r++)
            {
                extTotal++;
                var res = await agent.TriageSymptomsAsync(new AiTriageRequestDto
                {
                    Symptoms = ext.Symptoms,
                    PatientAge = ext.Age,
                    Gender = ext.Gender,
                    ExistingConditions = ext.ExistingConditions
                }, explicitModel: model);

                var combined = $"{res.UrgencyLevel} {res.RecommendedSpecialty} {res.TriageNotes} {string.Join(' ', res.SuggestedNextSteps)}";
                bool schemaValid = !res.IsSimulatedMode && Enum.TryParse<TriageUrgencyLevel>(res.UrgencyLevel, true, out _);
                bool mustContain = combined.Contains(ext.ExpectedMustContain, StringComparison.OrdinalIgnoreCase);

                if (schemaValid && mustContain) extPassed++;
                if (delayMsBetweenCalls > 0) await Task.Delay(delayMsBetweenCalls);
            }
        }

        // 2. Summary cases
        var selectedSummaries = dataset.SummaryCases.Take(maxSummaryCases).ToList();
        foreach (var s in selectedSummaries)
        {
            var patient = new PatientResponseDto
            {
                Id = s.PatientId,
                FirstName = s.FirstName,
                LastName = s.LastName,
                Email = $"{s.FirstName.ToLowerInvariant()}@private.example.com",
                Age = s.Age,
                Gender = s.Gender,
                MedicalNotes = s.MedicalNotes,
                Consultations =
                [
                    new ConsultationResponseDto
                    {
                        Id = s.ConsultationId,
                        PatientId = s.PatientId,
                        DoctorName = "Dr. Sarah Jenkins, MD",
                        ConsultationDate = DateTime.UtcNow.AddDays(-5),
                        Diagnosis = s.Diagnosis,
                        Status = ConsultationStatus.Completed
                    }
                ]
            };
            patientMock.Setup(p => p.GetPatientByIdAsync(s.PatientId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(patient);

            for (int r = 0; r < RunsPerCase; r++)
            {
                sumTotal++;
                var res = await agent.SummarizePatientLiveAsync(s.PatientId, explicitModel: model);
                var combined = $"{res.AiGeneratedSummary} {string.Join(' ', res.AiIdentifiedRisks)} {string.Join(' ', res.AiRecommendedActions)}";

                bool schemaValid = !res.IsSimulatedMode && Enum.TryParse<ClinicalUrgencyLevel>(res.UrgencyAssessment, true, out _);
                bool mustContain = combined.Contains(s.ExpectedMustContain, StringComparison.OrdinalIgnoreCase);
                bool noPii = !combined.Contains(s.ExpectedMustNotContain, StringComparison.OrdinalIgnoreCase);

                if (schemaValid && mustContain && noPii) sumPassed++;
                if (delayMsBetweenCalls > 0) await Task.Delay(delayMsBetweenCalls);
            }
        }

        // 3. Tool selection cases
        foreach (var tc in dataset.ToolSelectionCases)
        {
            for (int r = 0; r < RunsPerCase; r++)
            {
                toolTotal++;
                var res = await agent.ChatAsync(new AiChatRequestDto
                {
                    Message = tc.Prompt,
                    PatientContextId = tc.PatientContextId,
                    Model = model
                });

                bool toolCorrect = !res.IsSimulatedMode &&
                    res.ToolsExecuted.Contains(tc.ExpectedToolName, StringComparer.OrdinalIgnoreCase);

                if (toolCorrect) toolPassed++;
                if (delayMsBetweenCalls > 0) await Task.Delay(delayMsBetweenCalls);
            }
        }

        var logs = await db.AiInteractionLogs.AsNoTracking().ToListAsync();
        var latencies = logs.Select(l => l.LatencyMs).OrderBy(x => x).ToList();
        var totalTokens = logs.Sum(l => (long)(l.TotalTokens ?? 0));
        var totalCost = logs.Sum(l => l.EstimatedCostUsd ?? 0m);
        int totalRuns = extTotal + sumTotal + toolTotal;
        int totalPassed = extPassed + sumPassed + toolPassed;

        return new ModelEvalAggregate
        {
            Model = model,
            TotalRuns = totalRuns,
            TotalPassed = totalPassed,
            ExtractionPassed = extPassed,
            ExtractionTotal = extTotal,
            SummaryPassed = sumPassed,
            SummaryTotal = sumTotal,
            ToolPassed = toolPassed,
            ToolTotal = toolTotal,
            P50LatencyMs = Percentile(latencies, 0.50),
            P95LatencyMs = Percentile(latencies, 0.95),
            TotalTokens = totalTokens,
            AvgTokensPerRequest = totalRuns > 0 ? (double)totalTokens / totalRuns : 0,
            TotalCostUsd = Math.Round(totalCost, 8),
            AvgCostPerRequestUsd = totalRuns > 0 ? Math.Round(totalCost / totalRuns, 8) : 0m
        };
    }

    private static string BuildMarkdownReport(List<ModelEvalAggregate> aggregates)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Multi-Model Evaluation Comparison");
        sb.AppendLine();
        sb.AppendLine($"- **Date (UTC):** {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"- **Models Evaluated:** {string.Join(", ", aggregates.Select(a => $"`{a.Model}`"))}");
        sb.AppendLine($"- **Task Types:** Extraction (4 cases), Summary (4 cases), Tool Selection (4 cases) × {RunsPerCase} runs each = **{aggregates.First().TotalRuns} runs per model**");
        sb.AppendLine("- **Scoring Criteria:** Valid JSON schema + allowed enum values, required must-contain clinical terms, zero PII leaks, and exact expected tool selection.");
        sb.AppendLine();
        sb.AppendLine("## Overall Comparison");
        sb.AppendLine();
        sb.AppendLine("| Model | Overall Pass Rate | Extraction | Summary | Tool Selection | p50 Latency (ms) | p95 Latency (ms) | Avg Tokens / Req | Total Tokens | Avg Cost / Req (USD) | Total Cost (USD) |");
        sb.AppendLine("| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |");

        foreach (var a in aggregates)
        {
            sb.AppendLine(
                $"| `{a.Model}` | **{a.TotalPassed}/{a.TotalRuns} ({a.PassRate:P1})** | " +
                $"{a.ExtractionPassed}/{a.ExtractionTotal} ({a.ExtractionPassRate:P0}) | " +
                $"{a.SummaryPassed}/{a.SummaryTotal} ({a.SummaryPassRate:P0}) | " +
                $"{a.ToolPassed}/{a.ToolTotal} ({a.ToolSelectionPassRate:P0}) | " +
                $"{a.P50LatencyMs} | {a.P95LatencyMs} | {a.AvgTokensPerRequest:F0} | {a.TotalTokens:N0} | " +
                $"${a.AvgCostPerRequestUsd:F6} | ${a.TotalCostUsd:F6} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Notes");
        sb.AppendLine("- Re-run manually with: `RUN_LIVE_MODEL_COMPARISON=1 GEMINI_API_KEY=<key> dotnet test --filter Category=ModelComparison`");
        sb.AppendLine("- Provider-agnostic abstraction (`ILlmClient`) is also implemented for `GeminiLlmClient` and local `OllamaLlmClient` (`Llm:Provider=Ollama`).");
        return sb.ToString();
    }

    private static StubHandler CreateDeterministicMockHandler() => new(req =>
    {
        var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

        // Tool response turn (turn 2 of chat)
        if (body.Contains("\"functionResponse\""))
        {
            return OkGeminiJson(new
            {
                candidates = new[] { new { content = new { parts = new[] { new { text = "Completed clinical tool execution cleanly." } } } } },
                usageMetadata = new { promptTokenCount = 420, candidatesTokenCount = 80, totalTokenCount = 500 }
            });
        }

        // Tool selection turn (turn 1 of chat)
        if (body.Contains("\"functionDeclarations\""))
        {
            var toolName =
                body.Contains("Search our patient directory", StringComparison.OrdinalIgnoreCase) ? "search_patients" :
                body.Contains("Fetch the clinical record", StringComparison.OrdinalIgnoreCase) ? "get_patient_details" :
                body.Contains("Check Dr. Sarah Jenkins", StringComparison.OrdinalIgnoreCase) ? "get_doctor_availability" :
                "propose_consultation";

            object args = toolName switch
            {
                "search_patients" => new { searchTerm = "Eleanor" },
                "get_patient_details" => new { patientId = 101 },
                "get_doctor_availability" => new { doctorId = 1, dateUtc = "2026-11-25" },
                _ => new { patientId = 101, doctorName = "Dr. Sarah Jenkins, MD", diagnosis = "Hypertension review", consultationDateUtc = "2026-11-25T10:00:00Z" }
            };

            return OkGeminiJson(new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            parts = new[] { new { functionCall = new { id = "mock_fc_1", name = toolName, args } } }
                        }
                    }
                },
                usageMetadata = new { promptTokenCount = 350, candidatesTokenCount = 45, totalTokenCount = 395 }
            });
        }

        // Extraction / Triage schema
        if (body.Contains("\"urgencyLevel\""))
        {
            var note =
                body.Contains("fever", StringComparison.OrdinalIgnoreCase) ? "High fever with sinus symptoms requiring prompt clinical evaluation." :
                body.Contains("ankle", StringComparison.OrdinalIgnoreCase) ? "Acute right ankle sprain/injury requiring orthopedic assessment." :
                body.Contains("migraine", StringComparison.OrdinalIgnoreCase) ? "Recurring migraine with photophobia and nausea." :
                "Lower urinary tract burning symptoms consistent with uncomplicated UTI.";

            var text = JsonSerializer.Serialize(new
            {
                urgencyLevel = "Medium",
                recommendedSpecialty = "Internal Medicine",
                triageNotes = note,
                redFlagQuestions = new[] { "Are symptoms rapidly worsening?" },
                suggestedNextSteps = new[] { "Schedule clinician evaluation." }
            });

            return OkGeminiJson(new
            {
                candidates = new[] { new { content = new { parts = new[] { new { text } } } } },
                usageMetadata = new { promptTokenCount = 260, candidatesTokenCount = 90, totalTokenCount = 350 }
            });
        }

        // Clinical Summary schema
        var summaryText =
            body.Contains("1001") ? "Patient #101 reviewed in Consultation #1001 for hypertension with penicillin allergy." :
            body.Contains("apixaban", StringComparison.OrdinalIgnoreCase) ? "Patient #102 on apixaban for atrial fibrillation (Consultation #1002)." :
            body.Contains("Asthma", StringComparison.OrdinalIgnoreCase) ? "Patient #103 has seasonal Asthma flare managed with albuterol (Consultation #1003)." :
            "Patient #104 evaluated in Consultation #1004 for diabetes HbA1c screening.";

        var summaryJson = JsonSerializer.Serialize(new
        {
            summary = summaryText,
            identifiedRisks = new[] { "Documented baseline clinical risk" },
            recommendedActions = new[] { "Follow up with primary clinician" },
            urgencyAssessment = "Routine"
        });

        return OkGeminiJson(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = summaryJson } } } } },
            usageMetadata = new { promptTokenCount = 310, candidatesTokenCount = 110, totalTokenCount = 420 }
        });
    });

    private static HttpResponseMessage OkGeminiJson(object obj) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(obj), Encoding.UTF8, "application/json")
        };

    private static long Percentile(List<long> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        var idx = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Clamp(idx, 0, sorted.Count - 1)];
    }

    private static async Task<ComparisonDataset> LoadDatasetAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Evaluation", "ai-evaluation-dataset.json");
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<ComparisonDataset>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PatientManagement.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class ModelEvalAggregate
    {
        public string Model { get; set; } = string.Empty;
        public int TotalRuns { get; set; }
        public int TotalPassed { get; set; }
        public int ExtractionPassed { get; set; }
        public int ExtractionTotal { get; set; }
        public int SummaryPassed { get; set; }
        public int SummaryTotal { get; set; }
        public int ToolPassed { get; set; }
        public int ToolTotal { get; set; }
        public long P50LatencyMs { get; set; }
        public long P95LatencyMs { get; set; }
        public long TotalTokens { get; set; }
        public double AvgTokensPerRequest { get; set; }
        public decimal TotalCostUsd { get; set; }
        public decimal AvgCostPerRequestUsd { get; set; }

        public double PassRate => TotalRuns > 0 ? (double)TotalPassed / TotalRuns : 0;
        public double ExtractionPassRate => ExtractionTotal > 0 ? (double)ExtractionPassed / ExtractionTotal : 0;
        public double SummaryPassRate => SummaryTotal > 0 ? (double)SummaryPassed / SummaryTotal : 0;
        public double ToolSelectionPassRate => ToolTotal > 0 ? (double)ToolPassed / ToolTotal : 0;
    }

    private sealed class ComparisonDataset
    {
        public List<SummaryEvalCase> SummaryCases { get; set; } = new();
        public List<ExtractionEvalCase> ExtractionCases { get; set; } = new();
        public List<ToolSelectionEvalCase> ToolSelectionCases { get; set; } = new();
    }

    private sealed class SummaryEvalCase
    {
        public string Id { get; set; } = string.Empty;
        public int PatientId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public int Age { get; set; }
        public string Gender { get; set; } = string.Empty;
        public string MedicalNotes { get; set; } = string.Empty;
        public int ConsultationId { get; set; }
        public string Diagnosis { get; set; } = string.Empty;
        public string ExpectedMustContain { get; set; } = string.Empty;
        public string ExpectedMustNotContain { get; set; } = string.Empty;
    }

    private sealed class ExtractionEvalCase
    {
        public string Id { get; set; } = string.Empty;
        public string Symptoms { get; set; } = string.Empty;
        public int Age { get; set; }
        public string Gender { get; set; } = string.Empty;
        public string ExistingConditions { get; set; } = string.Empty;
        public string ExpectedMustContain { get; set; } = string.Empty;
    }

    private sealed class ToolSelectionEvalCase
    {
        public string Id { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public int? PatientContextId { get; set; }
        public string ExpectedToolName { get; set; } = string.Empty;
    }
}
