using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using PatientManagement.Api.Filters;
using PatientManagement.Api.Middleware;
using PatientManagement.Api.Services;
using PatientManagement.Core.Interfaces;
using PatientManagement.Core.Options;
using PatientManagement.Infrastructure;
using PatientManagement.Infrastructure.Data;
using PatientManagement.Infrastructure.Validation;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// 1. Structured Logging (Serilog + optional Application Insights)
builder.Host.UseSerilog((context, services, loggerConfig) =>
{
    loggerConfig
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console();

    var appInsightsConn = context.Configuration["ApplicationInsights:ConnectionString"];
    if (!string.IsNullOrWhiteSpace(appInsightsConn))
    {
        loggerConfig.WriteTo.ApplicationInsights(appInsightsConn, TelemetryConverter.Traces);
    }
});

// 2. Controllers, JSON & FluentValidation Filter
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddValidatorsFromAssemblyContaining<CreatePatientDtoValidator>();

builder.Services.AddControllers(options =>
    {
        options.Filters.Add<FluentValidationActionFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// 3. Strict CORS Policy (Origin Allowlist)
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:4200", "https://localhost:4200"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("RestrictedCors", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("Content-Disposition");
    });
});

// 4. Rate Limiting (Global + Auth + AI Policies)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 20;
        opt.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("ai", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 30;
        opt.QueueLimit = 2;
    });

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1)
            }));
});

// 5. JWT Options & Bearer Authentication (Built-in JWT + optional Azure AD / Entra ID)
builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.SectionName)
    .PostConfigure(options =>
    {
        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            var envKey = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY");
            if (!string.IsNullOrWhiteSpace(envKey))
            {
                options.SigningKey = envKey;
            }
        }
    });

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>, IConfiguration>((options, jwtOptionsAccessor, config) =>
    {
        var jwtOptions = jwtOptionsAccessor.Value;
        var entraAuthority = config["AzureAd:Authority"];
        var jwtAudience = string.IsNullOrWhiteSpace(jwtOptions.Audience) ? "PatientManagement.Client" : jwtOptions.Audience;
        if (!string.IsNullOrWhiteSpace(entraAuthority))
        {
            options.Authority = entraAuthority;
            options.Audience = config["AzureAd:ClientId"] ?? jwtAudience;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
            {
                throw new InvalidOperationException("Jwt:SigningKey must be configured.");
            }

            var jwtIssuer = string.IsNullOrWhiteSpace(jwtOptions.Issuer) ? "PatientManagement.Api" : jwtOptions.Issuer;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtIssuer,
                ValidAudience = jwtAudience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                ClockSkew = TimeSpan.FromMinutes(1)
            };
        }
    });

builder.Services.AddAuthorization();

// 6. Infrastructure & Health Checks
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("sqlserver");

// 7. Swagger / OpenAPI with Bearer Auth
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Patient Consultations & AI Clinical Support API",
        Version = "v1",
        Description = "Production-hardened .NET 10 Web API with JWT RBAC, Private Blob SAS storage, Soft Delete + Audit Trail, Double-Booking Prevention, and Safe Gemini Clinical AI."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter JWT Bearer token obtained from /api/auth/login"
    });

    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });

    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

var jwtKey = app.Services.GetRequiredService<IOptions<JwtOptions>>().Value.SigningKey;
if (!app.Environment.IsDevelopment() &&
    (string.IsNullOrWhiteSpace(jwtKey) ||
     jwtKey.Length < 32 ||
     jwtKey.StartsWith("DEV_ONLY", StringComparison.OrdinalIgnoreCase)))
{
    throw new InvalidOperationException("Jwt:SigningKey must be configured.");
}

app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Patient Management API v1");
        c.RoutePrefix = string.Empty;
        c.DocumentTitle = "Patient Management API - Swagger UI";
        c.DisplayRequestDuration();
    });
    app.MapGet("/swagger", () => Results.Redirect("/")).AllowAnonymous();
}

app.UseCors("RestrictedCors");

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Health Check Endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            Status = report.Status.ToString(),
            Timestamp = DateTime.UtcNow,
            Environment = app.Environment.EnvironmentName,
            Framework = ".NET 10",
            Checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString())
        });
    }
}).AllowAnonymous();

app.MapHealthChecks("/health/ready").AllowAnonymous();

// Apply migrations in Development or when Database:AutoMigrateOnStartup is explicitly enabled.
// Demo database seeding runs STRICTLY in Development — AutoMigrateOnStartup never enables seeding outside Development.
var isTesting = string.Equals(app.Environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase);
var autoMigrate = app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:AutoMigrateOnStartup");
if (autoMigrate && !isTesting)
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    var dbContext = services.GetRequiredService<ApplicationDbContext>();

    try
    {
        if (dbContext.Database.IsRelational())
        {
            logger.LogInformation("Applying SQL Server database migrations...");
            await dbContext.Database.MigrateAsync();
        }

        if (app.Environment.IsDevelopment())
        {
            await DbInitializer.SeedAsync(dbContext, logger, isDevelopment: true, app.Configuration);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while applying migrations or seeding the database.");
    }
}

app.Run();

public partial class Program { }
