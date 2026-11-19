using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PatientManagement.Core.Entities;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Data;
using PatientManagement.Infrastructure.Services.Rag;
using Xunit;

namespace PatientManagement.Tests;

public class TwoStageRagTests
{
    [Fact]
    public void ContextCompressor_WhenGivenBoilerplate_StripsHeadersAndPreservesClinicalFindings()
    {
        // Arrange
        var compressor = new ClinicalContextCompressor();
        var rawClinicalDocument =
            "CLINICAL VISIT NOTE - ST. MARY HOSPITAL - DEPARTMENT OF ENDOCRINOLOGY.\n" +
            "Room 402B. Attending Physician: Dr. Jenkins, MD. MRN #88219. Page 1 of 3.\n" +
            "Patient presented for routine endocrinology evaluation.\n" +
            "Subjective: Patient reports good compliance with daily medications.\n" +
            "Objective: Laboratory results from yesterday showed HbA1c was 6.8%, decreased from 7.4% last year.\n" +
            "Blood pressure was recorded as 122/80 mmHg with resting pulse of 68 bpm.\n" +
            "Assessment and Plan: Continue Metformin 500mg BID. Follow-up appointment in 6 months.\n" +
            "Electronically signed by Dr. Jenkins. Confidentiality notice: do not distribute.";

        var query = "What was the latest HbA1c lab result?";

        // Act
        var compressed = compressor.Compress(query, rawClinicalDocument, maxWords: 45);

        // Assert
        Assert.NotEmpty(compressed);
        // High-signal clinical metric should be preserved
        Assert.Contains("HbA1c was 6.8%", compressed, StringComparison.OrdinalIgnoreCase);
        // Administrative boilerplate should be stripped out
        Assert.DoesNotContain("DEPARTMENT OF ENDOCRINOLOGY", compressed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Room 402B", compressed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Confidentiality notice", compressed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Page 1 of 3", compressed, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContextCompressor_WhenContentUnderMaxWords_ReturnsOriginalText()
    {
        // Arrange
        var compressor = new ClinicalContextCompressor();
        var shortText = "Patient has known penicillin allergy resulting in hives.";

        // Act
        var result = compressor.Compress("penicillin", shortText, maxWords: 50);

        // Assert
        Assert.Equal(shortText, result);
    }

    [Fact]
    public void ContextCompressor_PreservesChronologicalSentenceOrder()
    {
        // Arrange
        var compressor = new ClinicalContextCompressor();
        var text =
            "First observation: Blood pressure was 140/90 mmHg. " +
            "Administrative note: patient arrived 15 minutes late to clinic. " +
            "Second observation: Administered Lisinopril 10mg orally. " +
            "Third observation: Repeat blood pressure 30 minutes later was 126/82 mmHg.";

        // Act
        var result = compressor.Compress("blood pressure lisinopril", text, maxWords: 40);

        // Assert
        var firstIndex = result.IndexOf("140/90", StringComparison.Ordinal);
        var secondIndex = result.IndexOf("Lisinopril", StringComparison.Ordinal);
        var thirdIndex = result.IndexOf("126/82", StringComparison.Ordinal);

        Assert.True(firstIndex >= 0, "First observation should be retained.");
        Assert.True(secondIndex >= 0, "Intervention should be retained.");
        Assert.True(thirdIndex >= 0, "Repeat measurement should be retained.");
        Assert.True(firstIndex < secondIndex && secondIndex < thirdIndex, "Sentences must remain in chronological sequence.");
    }

    [Fact]
    public async Task CrossEncoderReranker_ElevatesNeedleInHaystack_OverHighBiEncoderDistractor()
    {
        // Arrange
        var compressor = new ClinicalContextCompressor();
        var reranker = new CrossEncoderRerankerService(compressor, NullLogger<CrossEncoderRerankerService>.Instance);

        var query = "What is the patient's current daily dose of Atorvastatin?";

        // Candidate 1: High general bi-encoder similarity score (0.85) because it mentions cardiology, statins,
        // cholesterol, and lipid profiles multiple times, but does NOT contain the exact dosage answer.
        var distractorCandidate = new RerankCandidate(
            ChunkId: 1,
            TextContent: "Cardiology follow-up notes: Reviewed general hyperlipidemia guidelines. Discussed statin therapy, lifestyle modification, and routine lipid profile monitoring. Patient tolerates medications well without myalgia.",
            InitialSimilarityScore: 0.85,
            SourceType: "Consultation",
            SourceLabel: "Cardiology General"
        );

        // Candidate 2: Moderate bi-encoder similarity (0.62) because it is brief, but contains the EXACT needle:
        // "current daily dose of Atorvastatin 40mg"
        var needleCandidate = new RerankCandidate(
            ChunkId: 2,
            TextContent: "Medication reconciliation: Current daily dose of Atorvastatin is 40mg taken at bedtime. Patient reports no side effects.",
            InitialSimilarityScore: 0.62,
            SourceType: "PatientDocument",
            SourceLabel: "Medication Reconciliation"
        );

        var candidates = new List<RerankCandidate> { distractorCandidate, needleCandidate };

        // Act
        var results = await reranker.RerankAsync(query, candidates, topN: 2);

        // Assert
        Assert.Equal(2, results.Count);
        // Stage 2 Cross-Encoder should elevate the needleCandidate to #1 position
        var topResult = results[0];
        Assert.Equal(2, topResult.ChunkId);
        Assert.Contains("Atorvastatin is 40mg", topResult.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.True(topResult.RelevanceScore > results[1].RelevanceScore,
            $"Expected needle chunk score ({topResult.RelevanceScore}) to exceed distractor score ({results[1].RelevanceScore})");
    }

    [Fact]
    public async Task CrossEncoderReranker_WhenQueryEmpty_ReturnsInitialOrder()
    {
        // Arrange
        var compressor = new ClinicalContextCompressor();
        var reranker = new CrossEncoderRerankerService(compressor, NullLogger<CrossEncoderRerankerService>.Instance);

        var candidate = new RerankCandidate(
            ChunkId: 10,
            TextContent: "General follow-up note.",
            InitialSimilarityScore: 0.75
        );

        // Act
        var results = await reranker.RerankAsync("", new[] { candidate }, topN: 1);

        // Assert
        Assert.Single(results);
        Assert.Equal(10, results[0].ChunkId);
        Assert.Equal(0.75, results[0].RelevanceScore);
    }

    [Fact]
    public async Task PatientRagService_TwoStageSearch_RetrievesAndReranksCorrectly()
    {
        // Arrange: In-memory DB
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new ApplicationDbContext(options);

        var patient = new Patient
        {
            Id = 1,
            FirstName = "Alice",
            LastName = "Smith",
            DateOfBirth = new DateTime(1980, 5, 12),
            Email = "alice@example.com",
            PhoneNumber = "555-1234"
        };
        dbContext.Patients.Add(patient);

        // Create 2 chunks for Alice: 
        // Chunk A: general lab note
        // Chunk B: specific penicillin allergy notice
        var chunkA = new PatientDocumentChunk
        {
            Id = 101,
            PatientId = 1,
            SourceType = "Consultation",
            SourceLabel = "General Visit",
            TextContent = "General wellness visit. Routine checkup completed with no issues reported.",
            EmbeddingVectorJson = "[0.1, 0.2, 0.3]",
            CreatedAtUtc = DateTime.UtcNow
        };

        var chunkB = new PatientDocumentChunk
        {
            Id = 102,
            PatientId = 1,
            SourceType = "PatientDocument",
            SourceLabel = "Allergy Report",
            TextContent =
                "ST. JUDE REGIONAL CLINIC - DEPARTMENT OF IMMUNOLOGY AND ALLERGY MEDICINE.\n" +
                "Facility Room 501. Fax number: 555-0199. Attending Physician: Dr. Reed, MD. MRN #99104. Page 1 of 2.\n" +
                "Initial consultation record for acute drug sensitivity evaluation.\n" +
                "Patient history intake: Patient presented with immediate severe penicillin allergy causing respiratory distress and anaphylaxis in 2023.\n" +
                "Clinical Assessment: True IgE-mediated beta-lactam hypersensitivity. Avoid all amoxicillin and penicillin class antibiotics completely.\n" +
                "Plan and Recommendations: Prescribe EpiPen Auto-Injector 0.3mg IM PRN. Update electronic allergy bracelet.\n" +
                "Administrative notice: Electronically signed by Dr. Reed. Confidentiality notice: privileged hospital document.",
            EmbeddingVectorJson = "[0.1, 0.2, 0.3]",
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.PatientDocumentChunks.AddRange(chunkA, chunkB);
        await dbContext.SaveChangesAsync();

        var mockEmbedding = new Mock<ITextEmbeddingService>();
        mockEmbedding.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        var mockBlob = new Mock<IBlobStorageService>();
        var mockUser = new Mock<ICurrentUserService>();
        mockUser.Setup(u => u.Role).Returns(PatientManagement.Core.Enums.UserRole.Doctor);
        mockUser.Setup(u => u.UserId).Returns(1);
        mockUser.Setup(u => u.DoctorId).Returns(1);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Rag:MinScoreFloor"] = "0.0",
                ["Rag:Reranker:Enabled"] = "true",
                ["Rag:Reranker:CandidateMultiplier"] = "3"
            })
            .Build();

        var compressor = new ClinicalContextCompressor();
        var reranker = new CrossEncoderRerankerService(compressor, NullLogger<CrossEncoderRerankerService>.Instance);

        var ragService = new PatientRagService(
            dbContext,
            mockBlob.Object,
            mockEmbedding.Object,
            mockUser.Object,
            NullLogger<PatientRagService>.Instance,
            config,
            semanticCache: null,
            reranker: reranker
        );

        // Act
        var results = await ragService.SearchPatientKnowledgeAsync(
            patientId: 1,
            query: "Does the patient have a penicillin allergy?",
            topK: 2,
            minScore: 0.1
        );

        // Assert
        Assert.NotEmpty(results);
        var topResult = results[0];
        Assert.Equal(102, topResult.ChunkId); // Allergy chunk should be reranked to top
        Assert.NotNull(topResult.RelevanceScore);
        Assert.True(topResult.RelevanceScore > 0.4, "Top result should have strong relevance score.");
        Assert.True(topResult.IsCompressed, "Boilerplate should be compressed from the allergy chunk.");
        Assert.Contains("penicillin allergy", topResult.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DEPARTMENT OF IMMUNOLOGY", topResult.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Facility Room 501", topResult.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Confidentiality notice", topResult.TextContent, StringComparison.OrdinalIgnoreCase);
    }
}
