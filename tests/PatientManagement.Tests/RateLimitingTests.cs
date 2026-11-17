using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PatientManagement.Core.DTOs.Auth;
using PatientManagement.Core.DTOs.Common;
using Xunit;

namespace PatientManagement.Tests;

public class RateLimitingTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    [Fact]
    public async Task AuthRateLimiter_ElevenBadLoginsFromOneIpReturn429_WhileAnotherIpStillWorks()
    {
        // Arrange: Fresh isolated factory instance so rate limit windows start clean
        using var factory = new CustomWebApplicationFactory();
        var clientIpA = factory.CreateClient();
        clientIpA.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.10");

        var clientIpB = factory.CreateClient();
        clientIpB.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.20");

        var badCredentials = new LoginRequestDto
        {
            Email = "admin@patientportal.com",
            Password = "WrongPassword!999"
        };

        // Act: 10 bad logins from IP A hit the auth controller and return 401
        for (int i = 0; i < 10; i++)
        {
            var res = await clientIpA.PostAsJsonAsync("/api/auth/login", badCredentials);
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized, because: $"attempt {i + 1} should be within the 10/min per-IP permit limit");
        }

        // 11th bad login from IP A is rejected with 429 Too Many Requests
        var eleventhRes = await clientIpA.PostAsJsonAsync("/api/auth/login", badCredentials);
        eleventhRes.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // A legitimate login from a different IP (IP B) still succeeds with 200 OK
        var validLoginFromIpB = await clientIpB.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = "admin@patientportal.com",
            Password = "Admin@12345"
        });
        validLoginFromIpB.StatusCode.Should().Be(HttpStatusCode.OK, because: await validLoginFromIpB.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AiRateLimiter_PartitionsPerAuthenticatedUser_DoesNotBlockAnotherUserOnSameIp()
    {
        // Arrange: Two different authenticated users sharing the exact same client IP
        using var factory = new CustomWebApplicationFactory();
        const string sharedIp = "198.51.100.5";

        var doctorClient = factory.CreateClient();
        doctorClient.DefaultRequestHeaders.Add("X-Forwarded-For", sharedIp);
        var doctorLogin = await doctorClient.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = "sarah.jenkins@patientportal.com",
            Password = "Doctor@12345"
        });
        doctorLogin.StatusCode.Should().Be(HttpStatusCode.OK);
        var doctorToken = (await doctorLogin.Content.ReadFromJsonAsync<ApiResponse<LoginResponseDto>>(JsonOptions))!.Data!.AccessToken;
        doctorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", doctorToken);

        var adminClient = factory.CreateClient();
        adminClient.DefaultRequestHeaders.Add("X-Forwarded-For", sharedIp);
        var adminLogin = await adminClient.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = "admin@patientportal.com",
            Password = "Admin@12345"
        });
        adminLogin.StatusCode.Should().Be(HttpStatusCode.OK);
        var adminToken = (await adminLogin.Content.ReadFromJsonAsync<ApiResponse<LoginResponseDto>>(JsonOptions))!.Data!.AccessToken;
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Act: Doctor exhausts their 30/min per-user AI quota
        for (int i = 0; i < 30; i++)
        {
            var res = await doctorClient.GetAsync("/api/ai/status");
            res.StatusCode.Should().Be(HttpStatusCode.OK, because: $"AI call {i + 1} should be within the 30/min per-user permit limit");
        }

        // 31st AI call from Doctor is throttled with 429 Too Many Requests
        var doctorThrottled = await doctorClient.GetAsync("/api/ai/status");
        doctorThrottled.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // Admin on the SAME IP still has their own per-user AI bucket -> 200 OK
        var adminAllowed = await adminClient.GetAsync("/api/ai/status");
        adminAllowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
