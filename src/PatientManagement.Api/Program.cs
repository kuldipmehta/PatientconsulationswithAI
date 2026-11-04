using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using PatientManagement.Api.Middleware;
using PatientManagement.Infrastructure;
using PatientManagement.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// 1. Add Controllers with JSON configuration
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// 2. Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("*");
    });
});

// 3. Add Infrastructure Services (EF Core SQL Server, Azure Blob Storage, Dummy Email Service)
builder.Services.AddInfrastructure(builder.Configuration);

// 4. Configure Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Patient Management API",
        Version = "v1",
        Description = """
            ### Patient Management API (.NET 10 + EF Core + SQL Server + Azure Blob Storage)

            #### Features:
            - **Create Patient**: Multipart form endpoint storing profile pictures in Azure Blob Storage.
            - **Dummy Email**: Automated welcome email dispatch and audit logging upon patient registration.
            - **Patient List & Search**: Retrieve, filter, sort, and paginate patient records from SQL Server.
            - **Data Handling**: Fetch and update patient details and consultation records via EF Core.
            - **Google Gemini AI Agent**: Autonomous clinical AI assistant with function calling (database queries, appointment booking, clinical summaries & symptom triage).
            - **Azure App Service Ready**: Pre-configured for deployment with public Swagger documentation.
            """,
        Contact = new OpenApiContact
        {
            Name = "Healthcare Engineering Team",
            Email = "support@patientmanagement.com"
        }
    });

    // Include XML comments documentation if available
    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

// 5. Global Exception Handling Middleware
app.UseMiddleware<ExceptionHandlingMiddleware>();

// 6. Enable Swagger in all environments (including Azure App Service Production)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Patient Management API v1");
    // Serve Swagger at both root (/) and /swagger
    c.RoutePrefix = string.Empty;
    c.DocumentTitle = "Patient Management API - Swagger UI";
    c.DisplayRequestDuration();
});

// Also keep /swagger accessible
app.MapGet("/swagger", () => Results.Redirect("/"));

// CORS must execute before any redirects or authorization to ensure headers are always present
app.UseCors("AllowAll");

// Only redirect HTTP to HTTPS in Production (prevents preflight CORS failures & SSL certificate errors in local development)
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy",
    Timestamp = DateTime.UtcNow,
    Environment = app.Environment.EnvironmentName,
    Framework = ".NET 10"
}));

// 7. Auto-migrate SQL Server database and seed initial test data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    var dbContext = services.GetRequiredService<ApplicationDbContext>();

    try
    {
        logger.LogInformation("Applying SQL Server database migrations...");
        await dbContext.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully.");

        await DbInitializer.SeedAsync(dbContext, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while applying migrations or seeding the database.");
    }
}

app.Run();

// For integration tests visibility
public partial class Program { }
