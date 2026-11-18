using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.DTOs.Auth;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.Entities;
using PatientManagement.Infrastructure.Data;
using Xunit;

namespace PatientManagement.Tests;

public class AiUsageSummaryTests
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
    public async Task GetAiUsageSummary_DoctorAndReceptionistGet403_AnonymousGets401()
    {
        using var factory = new CustomWebApplicationFactory();
        var anonClient = factory.CreateClient();
        var doctorClient = await CreateClientForUserAsync(factory, "sarah.jenkins@patientportal.com", "Doctor@12345", "198.51.100.61");
        var receptionClient = await CreateClientForUserAsync(factory, "reception@patientportal.com", "Reception@12345", "198.51.100.62");

        var anonRes = await anonClient.GetAsync("/api/ai/usage");
        var doctorRes = await doctorClient.GetAsync("/api/ai/usage");
        var receptionRes = await receptionClient.GetAsync("/api/ai/usage");

        anonRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        doctorRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        receptionRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAiUsageSummary_AdminReceivesAccurateTotalsGroupedByModelAndDayWithP95Latency()
    {
        using var factory = new CustomWebApplicationFactory();
        var adminClient = await CreateClientForUserAsync(factory, "admin@patientportal.com", "Admin@12345", "198.51.100.63");

        // Arrange: Seed AI interaction rows across 3 dates and 2 models
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Out-of-range row (Nov 10) — should be excluded by ?from=2026-11-17&to=2026-11-18
            db.AiInteractionLogs.Add(new AiInteractionLog
            {
                UserEmail = "admin@patientportal.com",
                UserRole = "Admin",
                Endpoint = "chat",
                ModelUsed = "gemini-2.5-flash",
                PromptTokens = 9999,
                CompletionTokens = 9999,
                TotalTokens = 19998,
                EstimatedCostUsd = 9.99m,
                LatencyMs = 5000,
                DurationMs = 5000,
                CreatedAtUtc = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc)
            });

            // 20 rows on 2026-11-18 for gemini-2.5-flash with latencies 100..2000 (step 100) => p95 = 1900ms
            for (int i = 1; i <= 20; i++)
            {
                db.AiInteractionLogs.Add(new AiInteractionLog
                {
                    UserEmail = "sarah.jenkins@patientportal.com",
                    UserRole = "Doctor",
                    Endpoint = "chat",
                    ModelUsed = "gemini-2.5-flash",
                    PromptTokens = 100,
                    CompletionTokens = 50,
                    TotalTokens = 150,
                    EstimatedCostUsd = 0.00015500m,
                    LatencyMs = i * 100,
                    DurationMs = i * 100,
                    CreatedAtUtc = new DateTime(2026, 11, 18, 9, i, 0, DateTimeKind.Utc)
                });
            }

            // 2 rows on 2026-11-17 for gemini-2.5-pro
            db.AiInteractionLogs.AddRange(
                new AiInteractionLog
                {
                    UserEmail = "admin@patientportal.com",
                    UserRole = "Admin",
                    Endpoint = "live-summary",
                    ModelUsed = "gemini-2.5-pro",
                    PromptTokens = 1000,
                    CompletionTokens = 400,
                    TotalTokens = 1400,
                    EstimatedCostUsd = 0.00525000m,
                    LatencyMs = 800,
                    DurationMs = 800,
                    CreatedAtUtc = new DateTime(2026, 11, 17, 15, 0, 0, DateTimeKind.Utc)
                },
                new AiInteractionLog
                {
                    UserEmail = "admin@patientportal.com",
                    UserRole = "Admin",
                    Endpoint = "triage",
                    ModelUsed = "gemini-2.5-pro",
                    PromptTokens = 500,
                    CompletionTokens = 200,
                    TotalTokens = 700,
                    EstimatedCostUsd = 0.00262500m,
                    LatencyMs = 1200,
                    DurationMs = 1200,
                    CreatedAtUtc = new DateTime(2026, 11, 17, 18, 30, 0, DateTimeKind.Utc)
                });

            await db.SaveChangesAsync();
        }

        // Act
        var res = await adminClient.GetAsync("/api/ai/usage?from=2026-11-17&to=2026-11-18");
        res.StatusCode.Should().Be(HttpStatusCode.OK, because: await res.Content.ReadAsStringAsync());

        var payload = await res.Content.ReadFromJsonAsync<ApiResponse<AiUsageSummaryDto>>(JsonOptions);
        var summary = payload!.Data!;

        // Assert grand totals match exact sum of the 22 in-range rows
        summary.TotalRequests.Should().Be(22);
        summary.TotalPromptTokens.Should().Be(20 * 100 + 1000 + 500);
        summary.TotalCompletionTokens.Should().Be(20 * 50 + 400 + 200);
        summary.TotalTokens.Should().Be(20 * 150 + 1400 + 700);
        summary.TotalEstimatedCostUsd.Should().Be(20 * 0.00015500m + 0.00525000m + 0.00262500m);

        // Assert grouped breakdown by day and model
        summary.Breakdown.Should().HaveCount(2);

        var nov18Flash = summary.Breakdown.Single(b => b.DayUtc == "2026-11-18" && b.ModelUsed == "gemini-2.5-flash");
        nov18Flash.Requests.Should().Be(20);
        nov18Flash.PromptTokens.Should().Be(2000);
        nov18Flash.CompletionTokens.Should().Be(1000);
        nov18Flash.TotalTokens.Should().Be(3000);
        nov18Flash.EstimatedCostUsd.Should().Be(0.00310000m);
        nov18Flash.P95LatencyMs.Should().Be(1900);

        var nov17Pro = summary.Breakdown.Single(b => b.DayUtc == "2026-11-17" && b.ModelUsed == "gemini-2.5-pro");
        nov17Pro.Requests.Should().Be(2);
        nov17Pro.PromptTokens.Should().Be(1500);
        nov17Pro.CompletionTokens.Should().Be(600);
        nov17Pro.TotalTokens.Should().Be(2100);
        nov17Pro.EstimatedCostUsd.Should().Be(0.00787500m);
        nov17Pro.P95LatencyMs.Should().Be(1200);
    }
}
