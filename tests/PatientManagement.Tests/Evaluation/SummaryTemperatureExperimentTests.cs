using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PatientManagement.Core.DTOs.Consultations;
using PatientManagement.Core.DTOs.Patients;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Data;
using PatientManagement.Infrastructure.Services;
using Xunit;

namespace PatientManagement.Tests.Evaluation;

public class SummaryTemperatureExperimentTests
{
    private static readonly double[] Temperatures = [0.0, 0.4, 1.0];
    private const int RunsPerCase = 3;

    [Fact]
    [Trait("Category", "LiveExperiment")]
    public async Task RunSummaryTemperatureVarianceExperiment_WhenEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_LIVE_TEMP_EXPERIMENT"), "1", StringComparison.Ordinal))
        {
            return; // Opt-in live experiment; skipped during standard offline CI runs
        }

        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        apiKey.Should().NotBeNullOrWhiteSpace("GEMINI_API_KEY must be set when RUN_LIVE_TEMP_EXPERIMENT=1");

        var datasetPath = Path.Combine(AppContext.BaseDirectory, "Evaluation", "ai-evaluation-dataset.json");
        var json = await File.ReadAllTextAsync(datasetPath);
        var dataset = JsonSerializer.Deserialize<EvalDataset>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var cases = dataset.SummaryCases;
        cases.Should().HaveCount(10);

        var report = new StringBuilder();
        report.AppendLine("# Clinical Summary Temperature Variance Experiment");
        report.AppendLine();
        report.AppendLine($"- **Date (UTC):** {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        report.AppendLine($"- **Model:** `gemini-3-flash-preview`");
        report.AppendLine($"- **Prompt Version:** `summary.v1` (`prompts/summary.v1.txt`)");
        report.AppendLine($"- **Dataset:** {cases.Count} clinical summary cases (`SUM-24`..`SUM-33`) × {RunsPerCase} runs × {Temperatures.Length} temperatures = **{cases.Count * RunsPerCase * Temperatures.Length} live runs**");
        report.AppendLine();
        report.AppendLine("## Aggregate Summary by Temperature");
        report.AppendLine();
        report.AppendLine("| Temperature | Live Schema Pass | Grounding Pass | PII-Free Pass | All Checks Pass | Urgency 3/3 Agreement | Exact Text 3/3 Match | Avg Pairwise Jaccard Similarity | Avg Length Spread (chars) | Avg Latency (ms) |");
        report.AppendLine("| :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |");

        var perCaseDetails = new StringBuilder();
        perCaseDetails.AppendLine();
        perCaseDetails.AppendLine("## Per-Case Breakdown");
        perCaseDetails.AppendLine();

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        foreach (var temp in Temperatures)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"TempExp_{temp}_{Guid.NewGuid():N}")
                .Options;
            await using var db = new ApplicationDbContext(options);

            var patientMock = new Mock<IPatientService>();
            var consultMock = new Mock<IConsultationService>();
            var doctorMock = new Mock<IDoctorService>();
            var loggerMock = new Mock<ILogger<GeminiPatientAgentService>>();

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Gemini:ApiKey"] = apiKey,
                    ["Gemini:Model"] = "gemini-3-flash-preview",
                    ["Gemini:AllowedModels:0"] = "gemini-3-flash-preview",
                    ["Gemini:Features:Summary:Temperature"] = temp.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Gemini:Features:Summary:TopP"] = "0.9",
                    ["Gemini:Features:Summary:MaxOutputTokens"] = "1024",
                    ["Gemini:Features:Summary:PromptVersion"] = "summary.v1"
                })
                .Build();

            var agent = new GeminiPatientAgentService(
                httpClient, db, config,
                patientMock.Object, consultMock.Object, doctorMock.Object, loggerMock.Object);

            int totalRuns = 0;
            int schemaPass = 0;
            int groundingPass = 0;
            int piiPass = 0;
            int allPass = 0;
            int urgencyAgreeCases = 0;
            int exactTextCases = 0;
            var jaccards = new List<double>();
            var lengthSpreads = new List<int>();

            perCaseDetails.AppendLine($"### Temperature = `{temp:0.0}`");
            perCaseDetails.AppendLine();
            perCaseDetails.AppendLine("| Case | Checks Passed | Urgencies (3 runs) | Pairwise Jaccard | Length Spread (chars) |");
            perCaseDetails.AppendLine("| :--- | :---: | :--- | :---: | :---: |");

            foreach (var c in cases)
            {
                var patient = new PatientResponseDto
                {
                    Id = c.PatientId,
                    FirstName = c.FirstName,
                    LastName = c.LastName,
                    Email = $"{c.FirstName.ToLowerInvariant()}.{c.LastName.ToLowerInvariant()}@private.example.com",
                    Age = c.Age,
                    Gender = c.Gender,
                    MedicalNotes = c.MedicalNotes,
                    Consultations =
                    [
                        new ConsultationResponseDto
                        {
                            Id = c.ConsultationId,
                            PatientId = c.PatientId,
                            DoctorName = "Dr. Sarah Jenkins, MD",
                            ConsultationDate = DateTime.UtcNow.AddDays(-5),
                            Diagnosis = c.Diagnosis,
                            Status = ConsultationStatus.Completed
                        }
                    ]
                };

                patientMock.Setup(s => s.GetPatientByIdAsync(c.PatientId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(patient);

                var summaries = new List<string>();
                var urgencies = new List<string>();
                int caseAllPass = 0;

                for (int run = 0; run < RunsPerCase; run++)
                {
                    totalRuns++;
                    var res = await agent.SummarizePatientLiveAsync(c.PatientId);
                    var combined = $"{res.AiGeneratedSummary} {string.Join(' ', res.AiIdentifiedRisks)} {string.Join(' ', res.AiRecommendedActions)}";

                    bool okSchema = !res.IsSimulatedMode && !string.IsNullOrWhiteSpace(res.AiGeneratedSummary);
                    bool okGrounding = combined.Contains(c.ExpectedMustContain, StringComparison.OrdinalIgnoreCase);
                    bool okPii = !combined.Contains(c.ExpectedMustNotContain, StringComparison.OrdinalIgnoreCase) &&
                                 !combined.Contains(patient.Email, StringComparison.OrdinalIgnoreCase);

                    if (okSchema) schemaPass++;
                    if (okGrounding) groundingPass++;
                    if (okPii) piiPass++;
                    if (okSchema && okGrounding && okPii)
                    {
                        allPass++;
                        caseAllPass++;
                    }

                    summaries.Add(res.AiGeneratedSummary.Trim());
                    urgencies.Add(res.UrgencyAssessment);

                    // Small pause to stay well under per-minute API rate limits
                    await Task.Delay(150);
                }

                if (urgencies.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
                {
                    urgencyAgreeCases++;
                }

                if (summaries.Distinct(StringComparer.Ordinal).Count() == 1)
                {
                    exactTextCases++;
                }

                double caseJaccard = AveragePairwiseJaccard(summaries);
                int spread = summaries.Max(s => s.Length) - summaries.Min(s => s.Length);
                jaccards.Add(caseJaccard);
                lengthSpreads.Add(spread);

                perCaseDetails.AppendLine($"| `{c.Id}` | {caseAllPass}/{RunsPerCase} | {string.Join(", ", urgencies)} | {caseJaccard:P1} | {spread} |");
            }

            var avgLatency = await db.AiInteractionLogs.AnyAsync()
                ? (long)await db.AiInteractionLogs.AverageAsync(l => l.LatencyMs)
                : 0;

            report.AppendLine(
                $"| `{temp:0.0}` | {schemaPass}/{totalRuns} ({(double)schemaPass / totalRuns:P0}) | " +
                $"{groundingPass}/{totalRuns} ({(double)groundingPass / totalRuns:P0}) | " +
                $"{piiPass}/{totalRuns} ({(double)piiPass / totalRuns:P0}) | " +
                $"**{allPass}/{totalRuns} ({(double)allPass / totalRuns:P0})** | " +
                $"{urgencyAgreeCases}/{cases.Count} | {exactTextCases}/{cases.Count} | " +
                $"{jaccards.Average():P1} | {lengthSpreads.Average():F1} | {avgLatency} |");

            perCaseDetails.AppendLine();
        }

        report.Append(perCaseDetails);

        // Save report to repo root docs/summary-temperature-experiment.md
        var repoRoot = FindRepoRoot();
        var reportDir = Path.Combine(repoRoot, "docs");
        Directory.CreateDirectory(reportDir);
        var reportPath = Path.Combine(reportDir, "summary-temperature-experiment.md");
        await File.WriteAllTextAsync(reportPath, report.ToString());
    }

    private static double AveragePairwiseJaccard(List<string> texts)
    {
        var sets = texts.Select(Tokenize).ToList();
        double sum = 0;
        int pairs = 0;
        for (int i = 0; i < sets.Count; i++)
        {
            for (int j = i + 1; j < sets.Count; j++)
            {
                var union = sets[i].Union(sets[j]).Count();
                var inter = sets[i].Intersect(sets[j]).Count();
                sum += union == 0 ? 1.0 : (double)inter / union;
                pairs++;
            }
        }
        return pairs == 0 ? 1.0 : sum / pairs;
    }

    private static HashSet<string> Tokenize(string text) =>
        text.ToLowerInvariant()
            .Split([' ', '\r', '\n', '\t', '.', ',', ';', ':', '(', ')', '[', ']', '-', '/', '"', '\''],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PatientManagement.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    private sealed class EvalDataset
    {
        public List<SummaryEvalCase> SummaryCases { get; set; } = new();
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
}
