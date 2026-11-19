using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.DTOs.Consultations;
using PatientManagement.Core.DTOs.Patients;
using PatientManagement.Core.DTOs.Rag;
using PatientManagement.Core.Entities;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Data;
using PatientManagement.Infrastructure.Services.Ai;
using PatientManagement.Infrastructure.Services.Rag;
using Xunit;

namespace PatientManagement.Tests;

public class PatientRagServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IBlobStorageService> _blobStorageMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly Mock<ILogger<PatientRagService>> _loggerMock;
    private readonly ITextEmbeddingService _embeddingService;

    public PatientRagServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _blobStorageMock = new Mock<IBlobStorageService>();
        _currentUserMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<PatientRagService>>();

        _currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(u => u.Role).Returns(UserRole.Doctor);
        _currentUserMock.Setup(u => u.DoctorId).Returns(1);
        _currentUserMock.Setup(u => u.UserId).Returns(10);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Rag:AllowPseudoEmbeddings"] = "true" })
            .Build();
        _embeddingService = new GeminiTextEmbeddingService(
            new HttpClient(),
            config,
            Mock.Of<ILogger<GeminiTextEmbeddingService>>());
    }

    [Fact]
    public void TextChunker_SplitsLongTextIntoOverlappingChunks()
    {
        var longText = "Patient has a long history of mild asthma. " +
                       "Prescribed salbutamol inhaler as needed for wheezing. " +
                       "Reported shortness of breath during cold weather exercise. " +
                       "Lungs are clear to auscultation bilaterally. " +
                       "No acute distress noted on examination today. " +
                       "Follow up recommended in three months or if symptoms worsen.";

        var chunks = TextChunker.ChunkText(longText, maxChunkSize: 120, overlapSize: 30);

        chunks.Should().NotBeEmpty();
        chunks.Count.Should().BeGreaterThan(1);
        foreach (var c in chunks)
        {
            c.Length.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void VectorOperations_CosineSimilarity_CalculatesAccurateSimilarity()
    {
        float[] a = [1.0f, 0.0f, 0.0f];
        float[] b = [1.0f, 0.0f, 0.0f];
        float[] c = [0.0f, 1.0f, 0.0f];

        var simSame = VectorOperations.CosineSimilarity(a, b);
        var simOrthogonal = VectorOperations.CosineSimilarity(a, c);

        simSame.Should().BeApproximately(1.0, 0.0001);
        simOrthogonal.Should().BeApproximately(0.0, 0.0001);
    }

    [Fact]
    public void VectorOperations_DeterministicPseudoEmbedding_SimilarTextsHaveHigherSimilarity()
    {
        var text1 = "Patient diagnosed with essential hypertension and high blood pressure.";
        var text2 = "Patient blood pressure elevated; hypertension treatment plan initiated.";
        var text3 = "Patient underwent routine dermatology wart removal with liquid nitrogen.";

        var emb1 = VectorOperations.GenerateDeterministicPseudoEmbedding(text1);
        var emb2 = VectorOperations.GenerateDeterministicPseudoEmbedding(text2);
        var emb3 = VectorOperations.GenerateDeterministicPseudoEmbedding(text3);

        var simRelated = VectorOperations.CosineSimilarity(emb1, emb2);
        var simUnrelated = VectorOperations.CosineSimilarity(emb1, emb3);

        simRelated.Should().BeGreaterThan(simUnrelated);
    }

    [Fact]
    public async Task UploadAndIndexDocumentAsync_WithValidTxtFile_ChunksAndStoresInDatabase()
    {
        // Arrange
        var patient = new Patient
        {
            Id = 5,
            FirstName = "Alice",
            LastName = "Smith",
            Email = "alice@example.com",
            PrimaryDoctorId = 1
        };
        _dbContext.Patients.Add(patient);
        await _dbContext.SaveChangesAsync();

        _blobStorageMock.Setup(b => b.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlobUploadResult("blob-alice.txt", "https://blob/alice.txt", "text/plain", 100));

        var sut = new PatientRagService(
            _dbContext,
            _blobStorageMock.Object,
            _embeddingService,
            _currentUserMock.Object,
            _loggerMock.Object);

        var textContent = "Clinical Discharge Summary: Patient admitted for acute asthma exacerbation. " +
                          "Treated with nebulized albuterol and oral prednisone. Discharged in stable condition.";
        var bytes = Encoding.UTF8.GetBytes(textContent);
        var fileMock = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "discharge_summary.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };

        // Act
        var result = await sut.UploadAndIndexDocumentAsync(5, fileMock, new UploadPatientDocumentDto { DocumentType = "DischargeSummary" });

        // Assert
        result.Should().NotBeNull();
        result.FileName.Should().Be("discharge_summary.txt");
        result.TotalChunks.Should().BeGreaterThan(0);

        var storedChunks = await _dbContext.PatientDocumentChunks.Where(c => c.PatientId == 5).ToListAsync();
        storedChunks.Should().NotBeEmpty();
        storedChunks.Should().Contain(c => c.SourceType == "PatientDocument");
    }

    [Fact]
    public async Task UploadAndIndexDocumentAsync_WithUnsupportedExtension_ThrowsArgumentException()
    {
        // Arrange
        var patient = new Patient { Id = 6, FirstName = "Bob", LastName = "Jones", Email = "bob@example.com", PrimaryDoctorId = 1 };
        _dbContext.Patients.Add(patient);
        await _dbContext.SaveChangesAsync();

        var sut = new PatientRagService(
            _dbContext,
            _blobStorageMock.Object,
            _embeddingService,
            _currentUserMock.Object,
            _loggerMock.Object);

        var bytes = Encoding.UTF8.GetBytes("malicious executable content");
        var fileMock = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "malicious.exe")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/x-msdownload"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.UploadAndIndexDocumentAsync(6, fileMock, new UploadPatientDocumentDto()));
    }

    [Fact]
    public async Task SearchPatientKnowledgeAsync_ReturnsRankedMatchesAboveThreshold()
    {
        // Arrange
        var patient = new Patient { Id = 8, FirstName = "Charlie", LastName = "Brown", Email = "charlie@example.com", PrimaryDoctorId = 1 };
        _dbContext.Patients.Add(patient);

        var consultation = new Consultation
        {
            Id = 801,
            PatientId = 8,
            DoctorName = "Dr. Jenkins",
            ConsultationDate = DateTime.UtcNow.AddDays(-10),
            Diagnosis = "Severe recurrent asthma with nocturnal wheezing",
            Symptoms = "Shortness of breath, chest tightness, wheezing at night",
            Notes = "Patient uses rescue inhaler three times daily. Stepped up to fluticasone/salmeterol.",
            Status = ConsultationStatus.Completed
        };
        _dbContext.Consultations.Add(consultation);
        await _dbContext.SaveChangesAsync();

        var sut = new PatientRagService(
            _dbContext,
            _blobStorageMock.Object,
            _embeddingService,
            _currentUserMock.Object,
            _loggerMock.Object);

        await sut.IndexConsultationAsync(801);

        // Act
        var results = await sut.SearchPatientKnowledgeAsync(8, "asthma inhaler wheezing", topK: 3, minScore: 0.25);

        // Assert
        results.Should().NotBeEmpty();
        results[0].SourceType.Should().Be("Consultation");
        results[0].TextContent.Should().Contain("asthma");
        results[0].SimilarityScore.Should().BeGreaterThan(0.25);
    }

    [Fact]
    public async Task SearchPatientKnowledgeAsync_WithLabReportKeywordMatch_RetrievesDocumentChunkEvenWithDimensionMismatch()
    {
        // Arrange
        var patient = new Patient { Id = 10, FirstName = "Kuldip", LastName = "Mehta", Email = "kuldip@example.com", PrimaryDoctorId = 1 };
        _dbContext.Patients.Add(patient);

        var doc = new PatientDocument
        {
            Id = 55,
            PatientId = 10,
            FileName = "SGPT_Liver_Report.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 12040,
            DocumentType = "Lab Report",
            Status = "Indexed",
            TotalChunks = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        _dbContext.PatientDocuments.Add(doc);

        var pseudoEmb768 = VectorOperations.GenerateDeterministicPseudoEmbedding("SGPT ALT Liver Panel 68 U/L", 768);
        var chunk = new PatientDocumentChunk
        {
            PatientDocumentId = 55,
            PatientId = 10,
            ChunkIndex = 1,
            SourceType = "PatientDocument",
            SourceLabel = "SGPT_Liver_Report.pdf (Section 1/1)",
            TextContent = "Serum Glutamic Pyruvic Transaminase (SGPT / ALT) level: 68 U/L (Elevated. Normal range: 7-56 U/L). Mild transaminitis.",
            EmbeddingVectorJson = VectorOperations.SerializeVector(pseudoEmb768),
            EmbeddingDimensions = 768,
            CreatedAtUtc = DateTime.UtcNow
        };
        _dbContext.PatientDocumentChunks.Add(chunk);
        await _dbContext.SaveChangesAsync();

        var sut = new PatientRagService(
            _dbContext,
            _blobStorageMock.Object,
            _embeddingService,
            _currentUserMock.Object,
            _loggerMock.Object);

        // Act - Query specifically asks for SGPT value
        var results = await sut.SearchPatientKnowledgeAsync(10, "What is the SGPT value for the patient?", topK: 3, minScore: 0.15);

        // Assert
        results.Should().NotBeEmpty();
        results[0].SourceType.Should().Be("PatientDocument");
        results[0].TextContent.Should().Contain("SGPT / ALT");
        results[0].TextContent.Should().Contain("68 U/L");
        results[0].SimilarityScore.Should().BeGreaterThan(0.40);
    }

    [Fact]
    public async Task ReindexPatientConsultationsAsync_ReIndexesBothConsultationsAndDocumentChunks()
    {
        // Arrange
        var patient = new Patient { Id = 11, FirstName = "Reindex", LastName = "Test", Email = "reindex@example.com", PrimaryDoctorId = 1 };
        _dbContext.Patients.Add(patient);

        var consultation = new Consultation
        {
            Id = 901,
            PatientId = 11,
            DoctorName = "Dr. Adams",
            ConsultationDate = DateTime.UtcNow.AddDays(-2),
            Diagnosis = "Hypertension check",
            Status = ConsultationStatus.Completed
        };
        _dbContext.Consultations.Add(consultation);

        var doc = new PatientDocument { Id = 60, PatientId = 11, FileName = "Blood_Test.pdf", ContentType = "application/pdf", Status = "Indexed" };
        _dbContext.PatientDocuments.Add(doc);

        var docChunk = new PatientDocumentChunk
        {
            PatientDocumentId = 60,
            PatientId = 11,
            ChunkIndex = 1,
            SourceType = "PatientDocument",
            SourceLabel = "Blood_Test.pdf (Section 1/1)",
            TextContent = "Fasting blood sugar: 95 mg/dL. Normal.",
            EmbeddingVectorJson = "[]",
            EmbeddingDimensions = 0,
            CreatedAtUtc = DateTime.UtcNow
        };
        _dbContext.PatientDocumentChunks.Add(docChunk);
        await _dbContext.SaveChangesAsync();

        var sut = new PatientRagService(
            _dbContext,
            _blobStorageMock.Object,
            _embeddingService,
            _currentUserMock.Object,
            _loggerMock.Object);

        // Act
        var count = await sut.ReindexPatientConsultationsAsync(11);

        // Assert
        count.Should().Be(1);
        var updatedChunk = await _dbContext.PatientDocumentChunks.FirstAsync(c => c.PatientDocumentId == 60);
        updatedChunk.EmbeddingDimensions.Should().BeGreaterThan(0);
        updatedChunk.EmbeddingVectorJson.Should().NotBe("[]");
    }

    [Fact]
    public async Task SearchPatientKnowledgeAsync_ExcludesChunksOfSoftDeletedConsultations()
    {
        _dbContext.Patients.Add(new Patient { Id = 31, FirstName = "Hidden", LastName = "Visit", Email = "hidden@example.com", PrimaryDoctorId = 1 });
        var consultation = new Consultation
        {
            Id = 3100,
            PatientId = 31,
            DoctorName = "Dr. Adams",
            ConsultationDate = DateTime.UtcNow.AddDays(-3),
            Diagnosis = "Secret hypertension diagnosis",
            Status = ConsultationStatus.Completed,
            IsDeleted = true
        };
        _dbContext.Consultations.Add(consultation);
        _dbContext.PatientDocumentChunks.Add(new PatientDocumentChunk
        {
            PatientId = 31,
            ConsultationId = 3100,
            SourceType = "Consultation",
            SourceLabel = "Consultation #3100",
            TextContent = "Diagnosis: Secret hypertension diagnosis",
            EmbeddingVectorJson = VectorOperations.SerializeVector(VectorOperations.GenerateDeterministicPseudoEmbedding("Secret hypertension diagnosis", 3072)),
            EmbeddingDimensions = 3072
        });
        await _dbContext.SaveChangesAsync();

        var sut = new PatientRagService(
            _dbContext,
            _blobStorageMock.Object,
            _embeddingService,
            _currentUserMock.Object,
            _loggerMock.Object);

        var results = await sut.SearchPatientKnowledgeAsync(31, "hypertension diagnosis", topK: 5, minScore: 0.15);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchPatientKnowledgeAsync_MatchesWholeWordsOnly_NotSubstrings()
    {
        _dbContext.Patients.Add(new Patient { Id = 41, FirstName = "Word", LastName = "Boundary", Email = "wb@example.com", PrimaryDoctorId = 1 });
        _dbContext.PatientDocumentChunks.AddRange(
            new PatientDocumentChunk
            {
                PatientId = 41,
                SourceType = "PatientDocument",
                SourceLabel = "diet.txt (Section 1/1)",
                TextContent = "Advised salt restriction. The default dosing was kept.",
                EmbeddingVectorJson = "[]",
                EmbeddingDimensions = 0
            },
            new PatientDocumentChunk
            {
                PatientId = 41,
                SourceType = "PatientDocument",
                SourceLabel = "lab.txt (Section 1/1)",
                TextContent = "ALT is 68 U/L, mildly elevated.",
                EmbeddingVectorJson = "[]",
                EmbeddingDimensions = 0
            });
        await _dbContext.SaveChangesAsync();

        var sut = new PatientRagService(_dbContext, _blobStorageMock.Object, _embeddingService, _currentUserMock.Object, _loggerMock.Object);

        var results = await sut.SearchPatientKnowledgeAsync(41, "alt", topK: 5, minScore: 0.15);

        results.Should().ContainSingle();
        results[0].SourceLabel.Should().Be("lab.txt (Section 1/1)");
    }

    [Fact]
    public async Task SearchPatientKnowledgeAsync_ConfiguredMinScoreFloor_RaisesTheBarForWeakKeywordMatches()
    {
        _dbContext.Patients.Add(new Patient { Id = 42, FirstName = "Score", LastName = "Floor", Email = "sf@example.com", PrimaryDoctorId = 1 });
        _dbContext.PatientDocumentChunks.Add(new PatientDocumentChunk
        {
            PatientId = 42,
            SourceType = "PatientDocument",
            SourceLabel = "lab.txt (Section 1/1)",
            TextContent = "SGPT is 68 U/L.",
            EmbeddingVectorJson = "[]",
            EmbeddingDimensions = 0
        });
        await _dbContext.SaveChangesAsync();

        var strictConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Rag:MinScoreFloor"] = "0.9" })
            .Build();

        var lenient = new PatientRagService(_dbContext, _blobStorageMock.Object, _embeddingService, _currentUserMock.Object, _loggerMock.Object);
        var strict = new PatientRagService(_dbContext, _blobStorageMock.Object, _embeddingService, _currentUserMock.Object, _loggerMock.Object, strictConfig);

        // One of the two query words matches => keyword score 0.625
        (await lenient.SearchPatientKnowledgeAsync(42, "sgpt zzzzqq", topK: 5, minScore: 0.15)).Should().ContainSingle();
        (await strict.SearchPatientKnowledgeAsync(42, "sgpt zzzzqq", topK: 5, minScore: 0.15)).Should().BeEmpty();
    }

    [Fact]
    public async Task EmbeddingService_WithoutKeyAndPseudoDisabled_ThrowsEmbeddingUnavailable()
    {
        var strictConfig = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var strict = new GeminiTextEmbeddingService(
            new HttpClient(),
            strictConfig,
            Mock.Of<ILogger<GeminiTextEmbeddingService>>());

        var single = async () => await strict.GenerateEmbeddingAsync("SGPT 68 U/L");
        var batch = async () => await strict.GenerateBatchEmbeddingsAsync(new[] { "SGPT 68 U/L" });

        await single.Should().ThrowAsync<PatientManagement.Core.Exceptions.EmbeddingUnavailableException>();
        await batch.Should().ThrowAsync<PatientManagement.Core.Exceptions.EmbeddingUnavailableException>();
    }

    [Fact]
    public async Task UploadAndIndexDocumentAsync_WhenEmbeddingsFail_StoresNothingAndSkipsBlobUpload()
    {
        // Arrange
        _dbContext.Patients.Add(new Patient { Id = 21, FirstName = "Fail", LastName = "Closed", Email = "fail@example.com", PrimaryDoctorId = 1 });
        await _dbContext.SaveChangesAsync();

        var failingEmbeddings = new Mock<ITextEmbeddingService>();
        failingEmbeddings
            .Setup(e => e.GenerateBatchEmbeddingsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PatientManagement.Core.Exceptions.EmbeddingUnavailableException("quota"));

        var sut = new PatientRagService(
            _dbContext,
            _blobStorageMock.Object,
            failingEmbeddings.Object,
            _currentUserMock.Object,
            _loggerMock.Object);

        var bytes = Encoding.UTF8.GetBytes("SGPT (ALT): 68 U/L. Normal range 7-56 U/L.");
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "lab.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };

        // Act
        var act = async () => await sut.UploadAndIndexDocumentAsync(21, file, new UploadPatientDocumentDto());

        // Assert
        await act.Should().ThrowAsync<PatientManagement.Core.Exceptions.EmbeddingUnavailableException>();
        (await _dbContext.PatientDocuments.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await _dbContext.PatientDocumentChunks.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        _blobStorageMock.Verify(b => b.UploadAsync(
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UploadAndIndexDocumentAsync_WhenEmbeddingCountMismatches_DoesNotStoreZeroVectors()
    {
        _dbContext.Patients.Add(new Patient { Id = 22, FirstName = "Count", LastName = "Mismatch", Email = "count@example.com", PrimaryDoctorId = 1 });
        await _dbContext.SaveChangesAsync();

        var shortEmbeddings = new Mock<ITextEmbeddingService>();
        shortEmbeddings
            .Setup(e => e.GenerateBatchEmbeddingsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]>());

        var sut = new PatientRagService(
            _dbContext,
            _blobStorageMock.Object,
            shortEmbeddings.Object,
            _currentUserMock.Object,
            _loggerMock.Object);

        var bytes = Encoding.UTF8.GetBytes("Hemoglobin 13.5 g/dL within normal limits.");
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "cbc.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };

        var act = async () => await sut.UploadAndIndexDocumentAsync(22, file, new UploadPatientDocumentDto());

        await act.Should().ThrowAsync<PatientManagement.Core.Exceptions.EmbeddingUnavailableException>();
        (await _dbContext.PatientDocumentChunks.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }
}
