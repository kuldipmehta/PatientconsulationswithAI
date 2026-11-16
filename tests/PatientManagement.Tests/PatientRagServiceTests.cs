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

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
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

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }
}
