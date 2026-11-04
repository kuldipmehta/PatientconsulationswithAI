using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.DTOs.Consultations;
using PatientManagement.Core.DTOs.Patients;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Data;
using Xunit;

namespace PatientManagement.Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }
}

public class PatientsApiIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public PatientsApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthyStatus()
    {
        // Act
        var response = await _client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Healthy");
    }

    [Fact]
    public async Task GetPatients_ReturnsOkWithPatientList()
    {
        // Act
        var response = await _client.GetAsync("/api/patients");

        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, because: content);
        var apiResult = await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<PatientSummaryDto>>>(JsonOptions);

        apiResult.Should().NotBeNull();
        apiResult!.Success.Should().BeTrue();
        apiResult.Data.Should().NotBeNull();
        apiResult.Data!.Items.Should().NotBeNull();
    }

    [Fact]
    public async Task CreatePatient_ViaMultipartFormData_Returns201Created_AndDispatchesEmail()
    {
        // Arrange
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Robert"), "FirstName");
        form.Add(new StringContent("Frost"), "LastName");
        form.Add(new StringContent("robert.frost@example.com"), "Email");
        form.Add(new StringContent("55"), "Age");
        form.Add(new StringContent("Male"), "Gender");
        form.Add(new StringContent("+1-555-8822"), "PhoneNumber");
        form.Add(new StringContent("New England woods"), "Address");
        form.Add(new StringContent("Healthy overall"), "MedicalNotes");

        var fileBytes = "fake-jpg-binary-image-data"u8.ToArray();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
        form.Add(fileContent, "Picture", "robert-frost.jpg");

        // Act
        var response = await _client.PostAsync("/api/patients", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var apiResult = await response.Content.ReadFromJsonAsync<ApiResponse<PatientResponseDto>>(JsonOptions);

        apiResult.Should().NotBeNull();
        apiResult!.Success.Should().BeTrue();
        apiResult.Data.Should().NotBeNull();
        apiResult.Data!.FirstName.Should().Be("Robert");
        apiResult.Data.LastName.Should().Be("Frost");
        apiResult.Data.Email.Should().Be("robert.frost@example.com");
        apiResult.Data.ProfilePictureUrl.Should().NotBeNullOrWhiteSpace();

        // Verify dummy email was logged
        var emailResponse = await _client.GetAsync("/api/emails/logs");
        emailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var emailLogs = await emailResponse.Content.ReadAsStringAsync();
        emailLogs.Should().Contain("robert.frost@example.com");
    }

    [Fact]
    public async Task CreateAndFetchConsultation_ReturnsSuccess()
    {
        // 1. Create Patient first
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Clara"), "FirstName");
        form.Add(new StringContent("Oswald"), "LastName");
        form.Add(new StringContent("clara.oswald@tardis.org"), "Email");
        form.Add(new StringContent("27"), "Age");
        form.Add(new StringContent("Female"), "Gender");

        var createPatientRes = await _client.PostAsync("/api/patients", form);
        var patientRes = await createPatientRes.Content.ReadFromJsonAsync<ApiResponse<PatientResponseDto>>(JsonOptions);
        var patientId = patientRes!.Data!.Id;

        // 2. Create Consultation for this patient
        var consultationDto = new CreateConsultationDto
        {
            PatientId = patientId,
            DoctorName = "The 11th Doctor",
            ConsultationDate = DateTime.UtcNow,
            Diagnosis = "Time Vortex Exposure checkup",
            Symptoms = "Occasional déjà vu",
            Status = ConsultationStatus.Scheduled,
            Fee = 199.99m
        };

        var createConsRes = await _client.PostAsJsonAsync("/api/consultations", consultationDto);
        createConsRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var consRes = await createConsRes.Content.ReadFromJsonAsync<ApiResponse<ConsultationResponseDto>>(JsonOptions);
        consRes.Should().NotBeNull();
        consRes!.Success.Should().BeTrue();
        consRes.Data!.DoctorName.Should().Be("The 11th Doctor");
        consRes.Data.PatientId.Should().Be(patientId);

        // 3. Update Consultation
        var updateDto = new UpdateConsultationDto
        {
            DoctorName = "The 12th Doctor",
            ConsultationDate = DateTime.UtcNow,
            Diagnosis = "Cleared of time radiation",
            Status = ConsultationStatus.Completed,
            Fee = 250.00m
        };

        var updateConsRes = await _client.PutAsJsonAsync($"/api/consultations/{consRes.Data.Id}", updateDto);
        updateConsRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Verify patient record now contains this consultation
        var getPatientRes = await _client.GetAsync($"/api/patients/{patientId}");
        var patientDetail = await getPatientRes.Content.ReadFromJsonAsync<ApiResponse<PatientResponseDto>>(JsonOptions);
        patientDetail!.Data!.Consultations.Should().HaveCount(1);
        patientDetail.Data.Consultations.First().DoctorName.Should().Be("The 12th Doctor");
    }

    [Fact]
    public async Task AiStatusEndpoint_ReturnsHealthyStatusAndModel()
    {
        // Act
        var response = await _client.GetAsync("/api/ai/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Google Gemini");
        content.Should().Contain("Clinical AI Assistant");
    }

    [Fact]
    public async Task AiChatEndpoint_HandlesPatientQuerySuccessfully()
    {
        // Arrange
        var chatRequest = new
        {
            Message = "Search for patient Eleanor Vance in the database"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/ai/chat", chatRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Eleanor");
    }

    [Fact]
    public async Task AiTriageEndpoint_ReturnsSpecialtyRecommendation()
    {
        // Arrange
        var triageRequest = new
        {
            Symptoms = "Swelling and acute pain in right knee after twisting injury",
            PatientAge = 40,
            Gender = "Male"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/ai/triage", triageRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Orthopedics");
    }
}
