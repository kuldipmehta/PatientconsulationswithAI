using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PatientManagement.Core.DTOs.Auth;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.DTOs.Patients;
using Xunit;

namespace PatientManagement.Tests;

public class ReceptionistLeastPrivilegeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static async Task<HttpClient> CreateClientForUserAsync(
        CustomWebApplicationFactory factory,
        string email,
        string password,
        string forwardedIp)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedIp);

        var loginRes = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = email,
            Password = password
        });
        loginRes.StatusCode.Should().Be(HttpStatusCode.OK, because: await loginRes.Content.ReadAsStringAsync());

        var payload = await loginRes.Content.ReadFromJsonAsync<ApiResponse<LoginResponseDto>>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Data!.AccessToken);
        return client;
    }

    [Fact]
    public async Task Receptionist_GetPatientAndConsultations_ReturnsReducedDtoWithNoClinicalFields()
    {
        using var factory = new CustomWebApplicationFactory();
        var receptionClient = await CreateClientForUserAsync(factory, "reception@patientportal.com", "Reception@12345", "198.51.100.31");
        var adminClient = await CreateClientForUserAsync(factory, "admin@patientportal.com", "Admin@12345", "198.51.100.32");

        // 1. Receptionist GET /api/patients/1 has NO MedicalNotes and NO clinical fields on nested consultations
        var receptionPatientRes = await receptionClient.GetAsync("/api/patients/1");
        receptionPatientRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var receptionPatientJson = await receptionPatientRes.Content.ReadAsStringAsync();

        using (var doc = JsonDocument.Parse(receptionPatientJson))
        {
            var data = doc.RootElement.GetProperty("data");
            data.TryGetProperty("firstName", out _).Should().BeTrue();
            data.TryGetProperty("phoneNumber", out _).Should().BeTrue();
            data.TryGetProperty("medicalNotes", out _).Should().BeFalse();

            var consultations = data.GetProperty("consultations");
            consultations.GetArrayLength().Should().BeGreaterThan(0);
            foreach (var c in consultations.EnumerateArray())
            {
                c.TryGetProperty("diagnosis", out _).Should().BeFalse();
                c.TryGetProperty("symptoms", out _).Should().BeFalse();
                c.TryGetProperty("notes", out _).Should().BeFalse();
                c.TryGetProperty("treatmentPlan", out _).Should().BeFalse();
                c.TryGetProperty("prescription", out _).Should().BeFalse();
            }
        }

        receptionPatientJson.Should().NotContain("penicillin");
        receptionPatientJson.Should().NotContain("Cetirizine");

        // 2. Receptionist GET /api/consultations and GET /api/consultations/{id} have NO clinical fields
        var receptionConsultListRes = await receptionClient.GetAsync("/api/consultations");
        receptionConsultListRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var receptionConsultListJson = await receptionConsultListRes.Content.ReadAsStringAsync();

        int firstConsultationId;
        using (var doc = JsonDocument.Parse(receptionConsultListJson))
        {
            var list = doc.RootElement.GetProperty("data");
            list.GetArrayLength().Should().BeGreaterThan(0);
            firstConsultationId = list[0].GetProperty("id").GetInt32();
            foreach (var c in list.EnumerateArray())
            {
                c.TryGetProperty("diagnosis", out _).Should().BeFalse();
                c.TryGetProperty("symptoms", out _).Should().BeFalse();
                c.TryGetProperty("notes", out _).Should().BeFalse();
                c.TryGetProperty("treatmentPlan", out _).Should().BeFalse();
                c.TryGetProperty("prescription", out _).Should().BeFalse();
            }
        }

        var receptionConsultByIdRes = await receptionClient.GetAsync($"/api/consultations/{firstConsultationId}");
        receptionConsultByIdRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var receptionConsultByIdJson = await receptionConsultByIdRes.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(receptionConsultByIdJson))
        {
            var c = doc.RootElement.GetProperty("data");
            c.TryGetProperty("diagnosis", out _).Should().BeFalse();
            c.TryGetProperty("prescription", out _).Should().BeFalse();
        }

        // 3. Admin STILL receives full clinical fields
        var adminPatientRes = await adminClient.GetFromJsonAsync<ApiResponse<PatientResponseDto>>("/api/patients/1", JsonOptions);
        adminPatientRes!.Data!.MedicalNotes.Should().Contain("penicillin");
        adminPatientRes.Data.Consultations.Should().Contain(c => !string.IsNullOrWhiteSpace(c.Diagnosis));
    }

    [Fact]
    public async Task Receptionist_UpdatingPhoneNumber_LeavesMedicalNotesIntact()
    {
        using var factory = new CustomWebApplicationFactory();
        var receptionClient = await CreateClientForUserAsync(factory, "reception@patientportal.com", "Reception@12345", "198.51.100.41");
        var adminClient = await CreateClientForUserAsync(factory, "admin@patientportal.com", "Admin@12345", "198.51.100.42");

        // Capture original clinical notes as Admin
        var before = (await adminClient.GetFromJsonAsync<ApiResponse<PatientResponseDto>>("/api/patients/1", JsonOptions))!.Data!;
        var originalMedicalNotes = before.MedicalNotes;
        originalMedicalNotes.Should().NotBeNullOrWhiteSpace();

        // 1. Receptionist updates ONLY PhoneNumber (omitting MedicalNotes)
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent(before.FirstName), "FirstName");
            form.Add(new StringContent(before.LastName), "LastName");
            form.Add(new StringContent(before.Email), "Email");
            form.Add(new StringContent("+1-555-7777"), "PhoneNumber");
            form.Add(new StringContent(before.Age.ToString()), "Age");
            form.Add(new StringContent(before.Gender), "Gender");

            var updateRes = await receptionClient.PutAsync("/api/patients/1", form);
            updateRes.StatusCode.Should().Be(HttpStatusCode.OK, because: await updateRes.Content.ReadAsStringAsync());

            // Receptionist response also contains no MedicalNotes
            var updateJson = await updateRes.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(updateJson);
            doc.RootElement.GetProperty("data").TryGetProperty("medicalNotes", out _).Should().BeFalse();
        }

        var afterPhoneUpdate = (await adminClient.GetFromJsonAsync<ApiResponse<PatientResponseDto>>("/api/patients/1", JsonOptions))!.Data!;
        afterPhoneUpdate.PhoneNumber.Should().Be("+1-555-7777");
        afterPhoneUpdate.MedicalNotes.Should().Be(originalMedicalNotes);

        // 2. Even if Receptionist explicitly sends MedicalNotes, it is ignored
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent(before.FirstName), "FirstName");
            form.Add(new StringContent(before.LastName), "LastName");
            form.Add(new StringContent(before.Email), "Email");
            form.Add(new StringContent("+1-555-8888"), "PhoneNumber");
            form.Add(new StringContent(before.Age.ToString()), "Age");
            form.Add(new StringContent(before.Gender), "Gender");
            form.Add(new StringContent("Attempted receptionist overwrite"), "MedicalNotes");

            var attemptRes = await receptionClient.PutAsync("/api/patients/1", form);
            attemptRes.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var afterOverwriteAttempt = (await adminClient.GetFromJsonAsync<ApiResponse<PatientResponseDto>>("/api/patients/1", JsonOptions))!.Data!;
        afterOverwriteAttempt.PhoneNumber.Should().Be("+1-555-8888");
        afterOverwriteAttempt.MedicalNotes.Should().Be(originalMedicalNotes);
    }

    [Fact]
    public async Task DoctorOrAdmin_UpdateWithoutMedicalNotesPreservesExisting_WithMedicalNotesUpdatesIt()
    {
        using var factory = new CustomWebApplicationFactory();
        var doctorClient = await CreateClientForUserAsync(factory, "sarah.jenkins@patientportal.com", "Doctor@12345", "198.51.100.51");

        var before = (await doctorClient.GetFromJsonAsync<ApiResponse<PatientResponseDto>>("/api/patients/1", JsonOptions))!.Data!;
        var originalNotes = before.MedicalNotes;
        originalNotes.Should().NotBeNullOrWhiteSpace();

        // 1. Doctor updates phone number WITHOUT sending MedicalNotes -> preserves MedicalNotes
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent(before.FirstName), "FirstName");
            form.Add(new StringContent(before.LastName), "LastName");
            form.Add(new StringContent(before.Email), "Email");
            form.Add(new StringContent("+1-555-3333"), "PhoneNumber");
            form.Add(new StringContent(before.Age.ToString()), "Age");
            form.Add(new StringContent(before.Gender), "Gender");

            var res = await doctorClient.PutAsync("/api/patients/1", form);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var afterPhoneOnly = (await doctorClient.GetFromJsonAsync<ApiResponse<PatientResponseDto>>("/api/patients/1", JsonOptions))!.Data!;
        afterPhoneOnly.PhoneNumber.Should().Be("+1-555-3333");
        afterPhoneOnly.MedicalNotes.Should().Be(originalNotes);

        // 2. Doctor explicitly sends MedicalNotes -> updates MedicalNotes
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent(before.FirstName), "FirstName");
            form.Add(new StringContent(before.LastName), "LastName");
            form.Add(new StringContent(before.Email), "Email");
            form.Add(new StringContent("+1-555-3333"), "PhoneNumber");
            form.Add(new StringContent(before.Age.ToString()), "Age");
            form.Add(new StringContent(before.Gender), "Gender");
            form.Add(new StringContent("Penicillin allergy verified by Dr. Jenkins."), "MedicalNotes");

            var res = await doctorClient.PutAsync("/api/patients/1", form);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var afterClinicalUpdate = (await doctorClient.GetFromJsonAsync<ApiResponse<PatientResponseDto>>("/api/patients/1", JsonOptions))!.Data!;
        afterClinicalUpdate.MedicalNotes.Should().Be("Penicillin allergy verified by Dr. Jenkins.");
    }
}
