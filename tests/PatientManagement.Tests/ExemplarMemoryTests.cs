using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Services.Ai;
using Xunit;

namespace PatientManagement.Tests;

public class ExemplarMemoryTests
{
    private readonly ExemplarMemoryService _memoryService;

    public ExemplarMemoryTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Rag:AllowPseudoEmbeddings"] = "true"
            })
            .Build();

        var embeddingService = new GeminiTextEmbeddingService(
            new HttpClient(),
            config,
            NullLogger<GeminiTextEmbeddingService>.Instance);

        _memoryService = new ExemplarMemoryService(
            embeddingService,
            NullLogger<ExemplarMemoryService>.Instance);
    }

    [Fact]
    public void GetAllExemplars_ReturnsSeededCuratedExemplars()
    {
        var exemplars = _memoryService.GetAllExemplars();

        Assert.NotEmpty(exemplars);
        Assert.Contains(exemplars, e => e.Id == "EX-PHARMACO-01");
        Assert.Contains(exemplars, e => e.Id == "EX-TRIAGE-01");
        Assert.Contains(exemplars, e => e.Id == "EX-ALLERGY-01");
        Assert.Contains(exemplars, e => e.Id == "EX-SUMMARY-01");
        Assert.Contains(exemplars, e => e.Id == "EX-SCHED-01");
    }

    [Fact]
    public async Task GetRelevantExemplarsAsync_WarfarinQuery_ReturnsPharmacovigilanceExemplar()
    {
        var query = "Patient is taking Warfarin daily and asking for pain relief with Ibuprofen.";

        var matches = await _memoryService.GetRelevantExemplarsAsync(query, topK: 2, minSimilarity: 0.10);

        Assert.NotEmpty(matches);
        var top = matches[0];
        Assert.Equal("EX-PHARMACO-01", top.Id);
        Assert.Equal("Pharmacovigilance", top.Domain);
        Assert.NotNull(top.RelevanceScore);
        Assert.True(top.RelevanceScore > 0);
    }

    [Fact]
    public async Task GetRelevantExemplarsAsync_CardiacChestPainQuery_ReturnsEmergencyTriageExemplar()
    {
        var query = "Sudden crushing chest pressure radiating down left arm with acute shortness of breath.";

        var matches = await _memoryService.GetRelevantExemplarsAsync(query, topK: 2, minSimilarity: 0.10);

        Assert.NotEmpty(matches);
        Assert.Contains(matches, m => m.Id == "EX-TRIAGE-01");
    }

    [Fact]
    public async Task GetRelevantExemplarsAsync_WithDomainFilter_FiltersAccurately()
    {
        var query = "Patient needs consultation scheduling for persistent joint swelling.";

        var matches = await _memoryService.GetRelevantExemplarsAsync(
            query,
            domain: "Specialist Scheduling",
            topK: 2,
            minSimilarity: 0.10);

        Assert.NotEmpty(matches);
        Assert.All(matches, m => Assert.Equal("Specialist Scheduling", m.Domain));
    }

    [Fact]
    public void FormatFewShotPrompt_IncludesTargetDelimitersAndGoldStandardResponse()
    {
        var exemplars = _memoryService.GetAllExemplars().Take(2).ToList();

        var prompt = _memoryService.FormatFewShotPrompt(exemplars);

        Assert.Contains("[CLINICAL EXEMPLAR GUIDANCE - GOLD-STANDARD CLINICAL PATTERNS]", prompt);
        Assert.Contains("EX-PHARMACO-01", prompt);
        Assert.Contains("Gold-Standard Reference Response", prompt);
        Assert.Contains("[END CLINICAL EXEMPLAR GUIDANCE]", prompt);
    }

    [Fact]
    public async Task GetRelevantExemplarsAsync_CompletelyUnrelatedQuery_ReturnsEmptyListWhenBelowThreshold()
    {
        var query = "What is the capital city of Australia?";

        var matches = await _memoryService.GetRelevantExemplarsAsync(query, minSimilarity: 0.85);

        Assert.Empty(matches);
    }
}
