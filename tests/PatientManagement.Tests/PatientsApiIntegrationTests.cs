using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.DTOs.Auth;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.DTOs.Consultations;
using PatientManagement.Core.DTOs.Patients;
using PatientManagement.Core.Enums;
using PatientManagement.Infrastructure.Data;
using Xunit;

namespace PatientManagement.Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"IntegrationDb_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = "INTEGRATION_TEST_SIGNING_KEY_32_CHARS_MINIMUM_SECRET",
                ["Jwt:Issuer"] = "PatientManagement.Api",
                ["Jwt:Audience"] = "PatientManagement.Client",
                ["Gemini:ApiKey"] = "", // Force deterministic offline simulation
                ["Database:AutoMigrateOnStartup"] = "false",
                ["Swagger:Enabled"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<ApplicationDbContext>();

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<CustomWebApplicationFactory>>();
            var seedConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Seed:AdminPassword"] = "Admin@12345",
                    ["Seed:DoctorPassword"] = "Doctor@12345",
                    ["Seed:ReceptionistPassword"] = "Reception@12345"
                })
                .Build();
            db.Database.EnsureCreated();
            DbInitializer.SeedAsync(db, logger, isDevelopment: true, seedConfig).GetAwaiter().GetResult();
        });
    }
}

public class PatientsApiIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static readonly byte[] ValidJpegHeaderBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    public PatientsApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string password)
    {
        var client = _factory.CreateClient();
        var loginRes = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        loginRes.StatusCode.Should().Be(HttpStatusCode.OK, because: await loginRes.Content.ReadAsStringAsync());
        var payload = await loginRes.Content.ReadFromJsonAsync<ApiResponse<LoginResponseDto>>(JsonOptions);
        payload!.Data!.AccessToken.Should().NotBeNullOrWhiteSpace();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload.Data.AccessToken);
        return client;
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthyStatus_Anonymously()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Healthy");
    }

    [Fact]
    public async Task ProtectedEndpoints_RejectAnonymousRequestsWith401()
    {
        var anonClient = _factory.CreateClient();
        var patientsRes = await anonClient.GetAsync("/api/patients");
        var aiRes = await anonClient.GetAsync("/api/ai/status");
        var auditRes = await anonClient.GetAsync("/api/audit");

        patientsRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        aiRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        auditRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RbacPolicies_EnforceDoctorOwnershipAndAdminRestrictions()
    {
        // Doctor #1 (Dr. Sarah Jenkins) should only see Doctor #1 patients and NOT Doctor #2 patients
        var doctorClient = await CreateAuthenticatedClientAsync("sarah.jenkins@patientportal.com", "Doctor@12345");
        var adminClient = await CreateAuthenticatedClientAsync("admin@patientportal.com", "Admin@12345");
        var receptionClient = await CreateAuthenticatedClientAsync("reception@patientportal.com", "Reception@12345");

        // Admin finds patient assigned to Doctor #2 (e.g., Marcus Vance has PrimaryDoctorId = 2 in seed)
        var allPatientsRes = await adminClient.GetFromJsonAsync<ApiResponse<PagedResult<PatientSummaryDto>>>("/api/patients?pageSize=20", JsonOptions);
        var doc2Patient = allPatientsRes!.Data!.Items.First(p => p.PrimaryDoctorId == 2);

        // Doctor #1 attempting to access Doctor #2's patient -> 403 Forbidden
        var forbiddenGet = await doctorClient.GetAsync($"/api/patients/{doc2Patient.Id}");
        forbiddenGet.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Receptionist cannot delete patients or access AI clinical endpoints or Admin audit endpoints
        var receptionDelete = await receptionClient.DeleteAsync($"/api/patients/{doc2Patient.Id}");
        var receptionAi = await receptionClient.GetAsync("/api/ai/status");
        var doctorAudit = await doctorClient.GetAsync("/api/audit");

        receptionDelete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        receptionAi.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        doctorAudit.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Admin CAN access Audit Logs
        var adminAudit = await adminClient.GetAsync("/api/audit");
        adminAudit.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreatePatient_WithValidJpegAndInvalidMagicBytes_EnforcesValidationAndStreamsPrivatePicture()
    {
        var adminClient = await CreateAuthenticatedClientAsync("admin@patientportal.com", "Admin@12345");

        // 1. Reject fake disguised image payload
        using (var invalidForm = new MultipartFormDataContent())
        {
            invalidForm.Add(new StringContent("Fake"), "FirstName");
            invalidForm.Add(new StringContent("Upload"), "LastName");
            invalidForm.Add(new StringContent("fake.upload@example.com"), "Email");
            invalidForm.Add(new StringContent("30"), "Age");
            invalidForm.Add(new StringContent("Male"), "Gender");

            var badBytes = "not-really-a-jpeg-file"u8.ToArray();
            var badContent = new ByteArrayContent(badBytes);
            badContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
            invalidForm.Add(badContent, "Picture", "fake.jpg");

            var rejected = await adminClient.PostAsync("/api/patients", invalidForm);
            rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // 2. Accept valid JPEG header bytes
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Robert"), "FirstName");
        form.Add(new StringContent("Frost"), "LastName");
        form.Add(new StringContent($"robert.frost.{Guid.NewGuid():N}@example.com"), "Email");
        form.Add(new StringContent("55"), "Age");
        form.Add(new StringContent("Male"), "Gender");
        form.Add(new StringContent("1"), "PrimaryDoctorId");

        var fileContent = new ByteArrayContent(ValidJpegHeaderBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
        form.Add(fileContent, "Picture", "robert-frost.jpg");

        var response = await adminClient.PostAsync("/api/patients", form);
        response.StatusCode.Should().Be(HttpStatusCode.Created, because: await response.Content.ReadAsStringAsync());

        var apiResult = await response.Content.ReadFromJsonAsync<ApiResponse<PatientResponseDto>>(JsonOptions);
        apiResult!.Data!.ProfilePictureUrl.Should().NotBeNullOrWhiteSpace();

        // Private proxy endpoint serves the image when authenticated
        var picResponse = await adminClient.GetAsync($"/api/patients/{apiResult.Data.Id}/picture");
        picResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        picResponse.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");

        // Welcome email logged without leaking passwords
        var emailResponse = await adminClient.GetAsync("/api/emails/logs");
        emailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var emailLogs = await emailResponse.Content.ReadAsStringAsync();
        emailLogs.Should().Contain(apiResult.Data.Email);
        emailLogs.Should().Contain("set-password?token=");
        emailLogs.Should().NotContain("Temporary Password");
    }

    [Fact]
    public async Task Consultations_DoubleBookingReturns409Conflict()
    {
        var adminClient = await CreateAuthenticatedClientAsync("admin@patientportal.com", "Admin@12345");
        var slotDate = new DateTime(2027, 3, 15, 11, 0, 0, DateTimeKind.Utc);

        var firstBooking = new CreateConsultationDto
        {
            PatientId = 1,
            DoctorId = 1,
            DoctorName = "Dr. Sarah Jenkins, MD",
            ConsultationDate = slotDate,
            DurationMinutes = 30,
            Diagnosis = "Routine cardiac follow-up",
            Status = ConsultationStatus.Scheduled
        };

        var res1 = await adminClient.PostAsJsonAsync("/api/consultations", firstBooking);
        res1.StatusCode.Should().Be(HttpStatusCode.Created, because: await res1.Content.ReadAsStringAsync());

        var overlappingBooking = new CreateConsultationDto
        {
            PatientId = 2,
            DoctorId = 1,
            DoctorName = "Dr. Sarah Jenkins, MD",
            ConsultationDate = slotDate.AddMinutes(10),
            DurationMinutes = 30,
            Diagnosis = "Conflicting slot attempt",
            Status = ConsultationStatus.Scheduled
        };

        var res2 = await adminClient.PostAsJsonAsync("/api/consultations", overlappingBooking);
        res2.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Consultations_DoctorBCannotAttachToDoctorAPatientWithOwnDoctorId_Returns403AndForcesCurrentDoctor()
    {
        var adminClient = await CreateAuthenticatedClientAsync("admin@patientportal.com", "Admin@12345");
        var doctorAClient = await CreateAuthenticatedClientAsync("sarah.jenkins@patientportal.com", "Doctor@12345");
        var doctorBClient = await CreateAuthenticatedClientAsync("robert.sterling@patientportal.com", "Doctor@12345");

        // Find Doctor A's patient (PrimaryDoctorId == 1)
        var allPatientsRes = await adminClient.GetFromJsonAsync<ApiResponse<PagedResult<PatientSummaryDto>>>("/api/patients?pageSize=20", JsonOptions);
        var doctorAPatient = allPatientsRes!.Data!.Items.First(p => p.PrimaryDoctorId == 1);

        // 1. Doctor B (DoctorId = 2) tries to create a consultation for Doctor A's patient with their own DoctorId = 2 -> 403 Forbidden
        var unauthorizedAttempt = new CreateConsultationDto
        {
            PatientId = doctorAPatient.Id,
            DoctorId = 2,
            DoctorName = "Dr. Robert Sterling",
            ConsultationDate = new DateTime(2026, 12, 20, 14, 0, 0, DateTimeKind.Utc),
            DurationMinutes = 30,
            Diagnosis = "Unauthorized cross-doctor attachment",
            Status = ConsultationStatus.Scheduled
        };

        var forbiddenRes = await doctorBClient.PostAsJsonAsync("/api/consultations", unauthorizedAttempt);
        forbiddenRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. Doctor A (DoctorId = 1) creates a consultation for Doctor A's patient while passing DoctorId = 2 -> succeeds, DoctorId forced to 1
        var spoofedDoctorRequest = new CreateConsultationDto
        {
            PatientId = doctorAPatient.Id,
            DoctorId = 2,
            DoctorName = "Dr. Robert Sterling",
            ConsultationDate = new DateTime(2026, 12, 21, 14, 0, 0, DateTimeKind.Utc),
            DurationMinutes = 30,
            Diagnosis = "Follow-up visit",
            Status = ConsultationStatus.Scheduled
        };

        var createdRes = await doctorAClient.PostAsJsonAsync("/api/consultations", spoofedDoctorRequest);
        createdRes.StatusCode.Should().Be(HttpStatusCode.Created, because: await createdRes.Content.ReadAsStringAsync());
        var createdPayload = await createdRes.Content.ReadFromJsonAsync<ApiResponse<ConsultationResponseDto>>(JsonOptions);
        createdPayload!.Data!.DoctorId.Should().Be(1);
        createdPayload.Data.DoctorName.Should().Be("Dr. Sarah Jenkins, MD");
    }

    [Fact]
    public async Task AiEndpoints_StatusChatStreamAndEmergencyTriage_WorkSafely()
    {
        var doctorClient = await CreateAuthenticatedClientAsync("sarah.jenkins@patientportal.com", "Doctor@12345");

        // 1. Status reflects server-side simulated banner and clinical disclaimer
        var statusRes = await doctorClient.GetAsync("/api/ai/status");
        statusRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var statusText = await statusRes.Content.ReadAsStringAsync();
        statusText.Should().Contain("Google Gemini");
        statusText.Should().Contain("SIMULATED MODE ACTIVE");

        // 2. SSE Stream returns meta, token, and done events
        var sseRes = await doctorClient.PostAsJsonAsync("/api/ai/chat/stream", new
        {
            Message = "Summarize my accessible patients"
        });
        sseRes.StatusCode.Should().Be(HttpStatusCode.OK);
        sseRes.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");
        var sseBody = await sseRes.Content.ReadAsStringAsync();
        sseBody.Should().Contain("event: meta");
        sseBody.Should().Contain("event: token");
        sseBody.Should().Contain("event: done");

        // 3. Emergency Triage triggers deterministic rule engine
        var triageRes = await doctorClient.PostAsJsonAsync("/api/ai/triage", new
        {
            Symptoms = "Crushing chest pain radiating to left arm and sweating",
            PatientAge = 60,
            Gender = "Male"
        });
        triageRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var triageBody = await triageRes.Content.ReadAsStringAsync();
        triageBody.Should().Contain("Emergency");
        triageBody.Should().Contain("911");
    }
}
