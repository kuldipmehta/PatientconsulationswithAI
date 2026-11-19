using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.Entities;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Data;
using PatientManagement.Infrastructure.Services;
using PatientManagement.Infrastructure.Services.Ai;
using PatientManagement.Infrastructure.Services.Rag;
using Xunit;

namespace PatientManagement.Tests;

public class SemanticCacheTests
{
    private readonly Mock<ITextEmbeddingService> _embeddingMock = new();
    private readonly Mock<ILogger<MemorySemanticCacheService>> _loggerMock = new();

    private IConfiguration CreateConfig(bool enabled = true, double threshold = 0.90, int ttlMinutes = 60)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:SemanticCache:Enabled"] = enabled.ToString(),
                ["Ai:SemanticCache:SimilarityThreshold"] = threshold.ToString(),
                ["Ai:SemanticCache:DefaultTtlMinutes"] = ttlMinutes.ToString()
            })
            .Build();
    }

    [Fact]
    public async Task ExactMatch_HitsCache_ImmediatelyWithPerfectScore()
    {
        var config = CreateConfig();
        var sut = new MemorySemanticCacheService(_embeddingMock.Object, config, _loggerMock.Object);

        // Vector for "What is normal ALT range?"
        var vector = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };
        _embeddingMock.Setup(e => e.GenerateEmbeddingAsync("What is normal ALT range?", It.IsAny<CancellationToken>()))
            .ReturnsAsync(vector);

        await sut.SetAsync("What is normal ALT range?", "Normal ALT is 7-56 U/L.", "gemini-flash-latest", scope: "global");

        var hit = await sut.GetAsync("What is normal ALT range?", scope: "global");

        hit.Should().NotBeNull();
        hit!.ResponseContent.Should().Be("Normal ALT is 7-56 U/L.");
        hit.SimilarityScore.Should().Be(1.0);
        hit.ModelUsed.Should().Be("gemini-flash-latest");
    }

    [Fact]
    public async Task ParaphrasedQuery_HitsCache_WhenCosineSimilarityAboveThreshold()
    {
        var config = CreateConfig(threshold: 0.85);
        var sut = new MemorySemanticCacheService(_embeddingMock.Object, config, _loggerMock.Object);

        // Vector A: original prompt
        var vecA = new float[] { 0.8f, 0.6f, 0.0f };
        // Vector B: slightly varied prompt (angle is small, high cosine similarity)
        var vecB = new float[] { 0.79f, 0.61f, 0.02f };

        _embeddingMock.Setup(e => e.GenerateEmbeddingAsync("Metformin adult dosage", It.IsAny<CancellationToken>()))
            .ReturnsAsync(vecA);
        _embeddingMock.Setup(e => e.GenerateEmbeddingAsync("What is the standard adult dose of metformin?", It.IsAny<CancellationToken>()))
            .ReturnsAsync(vecB);

        await sut.SetAsync("Metformin adult dosage", "Typical starting dose is 500mg BID.", "gemini-flash-latest");

        var result = await sut.GetAsync("What is the standard adult dose of metformin?");

        result.Should().NotBeNull();
        result!.ResponseContent.Should().Be("Typical starting dose is 500mg BID.");
        result.SimilarityScore.Should().BeGreaterThanOrEqualTo(0.85);
    }

    [Fact]
    public async Task DistinctQuery_MissesCache_WhenCosineSimilarityBelowThreshold()
    {
        var config = CreateConfig(threshold: 0.90);
        var sut = new MemorySemanticCacheService(_embeddingMock.Object, config, _loggerMock.Object);

        // Vector A: Metformin query
        var vecA = new float[] { 1.0f, 0.0f, 0.0f };
        // Vector B: Insulin query (orthogonal vector -> similarity ~ 0.0)
        var vecB = new float[] { 0.0f, 1.0f, 0.0f };

        _embeddingMock.Setup(e => e.GenerateEmbeddingAsync("Metformin dosage", It.IsAny<CancellationToken>()))
            .ReturnsAsync(vecA);
        _embeddingMock.Setup(e => e.GenerateEmbeddingAsync("Insulin glargine dosage", It.IsAny<CancellationToken>()))
            .ReturnsAsync(vecB);

        await sut.SetAsync("Metformin dosage", "500mg daily", "gemini-flash-latest");

        var result = await sut.GetAsync("Insulin glargine dosage");

        result.Should().BeNull();
    }

    [Fact]
    public async Task PatientScope_IsolatesData_BetweenDifferentPatients()
    {
        var config = CreateConfig();
        var sut = new MemorySemanticCacheService(_embeddingMock.Object, config, _loggerMock.Object);

        var vec = new float[] { 0.5f, 0.5f };
        _embeddingMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(vec);

        // Seed confidential question for Patient 10
        await sut.SetAsync(
            "What was the last systolic blood pressure reading?",
            "Last reading was 142 mmHg on Nov 10.",
            "gemini-flash-latest",
            scope: "patient:10");

        // Doctor asks identical question for Patient 20
        var crossPatientHit = await sut.GetAsync(
            "What was the last systolic blood pressure reading?",
            scope: "patient:20");

        // Must NOT leak patient 10 data to patient 20!
        crossPatientHit.Should().BeNull();

        // Same question for Patient 10 should hit
        var samePatientHit = await sut.GetAsync(
            "What was the last systolic blood pressure reading?",
            scope: "patient:10");

        samePatientHit.Should().NotBeNull();
        samePatientHit!.ResponseContent.Should().Contain("142 mmHg");
    }

    [Fact]
    public async Task TtlExpiration_EvictsEntry_WhenExpired()
    {
        var config = CreateConfig();
        var sut = new MemorySemanticCacheService(_embeddingMock.Object, config, _loggerMock.Object);

        var vec = new float[] { 0.1f, 0.2f };
        _embeddingMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(vec);

        // Store with 30ms TTL
        await sut.SetAsync(
            "Temporary advisory note",
            "Advisory details",
            "gemini-flash-latest",
            ttl: TimeSpan.FromMilliseconds(30));

        sut.Count.Should().Be(1);

        // Wait for expiration
        await Task.Delay(50);

        var hit = await sut.GetAsync("Temporary advisory note");
        hit.Should().BeNull();
        sut.Count.Should().Be(0);
    }

    [Fact]
    public async Task InvalidateScope_PurgesOnlyTargetScopeEntries()
    {
        var config = CreateConfig();
        var sut = new MemorySemanticCacheService(_embeddingMock.Object, config, _loggerMock.Object);

        var vec = new float[] { 0.1f, 0.2f };
        _embeddingMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(vec);

        await sut.SetAsync("Prompt P1", "Resp P1", "gemini-flash-latest", scope: "patient:1");
        await sut.SetAsync("Prompt P2", "Resp P2", "gemini-flash-latest", scope: "patient:2");
        await sut.SetAsync("Prompt Global", "Resp Global", "gemini-flash-latest", scope: "global");

        sut.Count.Should().Be(3);

        // Invalidate Patient 1 scope
        sut.InvalidateScope("patient:1");

        (await sut.GetAsync("Prompt P1", scope: "patient:1")).Should().BeNull();
        (await sut.GetAsync("Prompt P2", scope: "patient:2")).Should().NotBeNull();
        (await sut.GetAsync("Prompt Global", scope: "global")).Should().NotBeNull();
    }

    [Fact]
    public async Task ChatAsync_WhenSemanticCacheHit_ReturnsCachedResponseAndLogsZeroCost()
    {
        // Arrange
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"SemanticCacheIntegration_{Guid.NewGuid()}")
            .Options;
        using var db = new ApplicationDbContext(dbOptions);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"] = "fake-api-key",
                ["Gemini:Model"] = "gemini-flash-latest",
                ["Ai:SemanticCache:Enabled"] = "true",
                ["Ai:SemanticCache:SimilarityThreshold"] = "0.90"
            })
            .Build();

        var embeddingMock = new Mock<ITextEmbeddingService>();
        embeddingMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.5f, 0.5f });

        var cache = new MemorySemanticCacheService(embeddingMock.Object, config, Mock.Of<ILogger<MemorySemanticCacheService>>());
        await cache.SetAsync("What are normal lab ranges for ALT?", "Normal ALT is 7-56 U/L.", "gemini-flash-latest", scope: "global");

        var agentService = new GeminiPatientAgentService(
            new HttpClient(),
            db,
            config,
            Mock.Of<IPatientService>(),
            Mock.Of<IConsultationService>(),
            Mock.Of<IDoctorService>(),
            Mock.Of<ILogger<GeminiPatientAgentService>>(),
            semanticCacheService: cache);

        // Act - send request matching cached query
        var response = await agentService.ChatAsync(new AiChatRequestDto
        {
            Message = "What are normal lab ranges for ALT?"
        });

        // Assert
        response.Reply.Should().Be("Normal ALT is 7-56 U/L.");
        response.ModelUsed.Should().Contain("(cached)");
        response.EmergencyRuleTriggered.Should().BeFalse();

        // Verify logged interaction has 0 cost and 0 tokens!
        var log = await db.AiInteractionLogs.FirstAsync();
        log.EstimatedCostUsd.Should().Be(0m);
        log.TotalTokens.Should().Be(0);
        log.ModelUsed.Should().Contain("(cached)");
    }
}
