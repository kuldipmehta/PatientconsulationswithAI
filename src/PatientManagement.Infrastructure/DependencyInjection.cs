using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Data;
using PatientManagement.Infrastructure.Services;

namespace PatientManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // SQL Server EF Core Configuration
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=PatientManagementDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);
            });
        });

        // Register Azure Blob Storage Service
        services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();

        // Register Dummy Email Service
        services.AddSingleton<IEmailService, DummyEmailService>();

        // Register Domain Services
        services.AddScoped<IPatientService, PatientService>();
        services.AddScoped<IConsultationService, ConsultationService>();

        // Register Google Gemini AI Patient Agent Service
        services.AddHttpClient<IAiPatientAgentService, GeminiPatientAgentService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        return services;
    }
}
