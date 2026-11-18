using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace PatientManagement.Tests;

public class SwaggerTests
{
    [Fact]
    public async Task SwaggerUiAndOpenApiJson_LoadWithoutErrors()
    {
        using var factory = new CustomWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Swagger:Enabled"] = "true"
                    });
                });
            });

        var client = factory.CreateClient();

        // 1. Swagger JSON must return 200 OK and valid OpenAPI document
        var jsonRes = await client.GetAsync("/swagger/v1/swagger.json");
        var jsonBody = await jsonRes.Content.ReadAsStringAsync();
        jsonRes.StatusCode.Should().Be(HttpStatusCode.OK, because: jsonBody);

        using var doc = JsonDocument.Parse(jsonBody);
        doc.RootElement.TryGetProperty("paths", out var paths).Should().BeTrue();
        paths.EnumerateObject().Should().NotBeEmpty();

        // 2. Swagger UI root must return 200 OK HTML
        var uiRes = await client.GetAsync("/");
        uiRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await uiRes.Content.ReadAsStringAsync();
        html.Should().Contain("Swagger UI");
    }
}
