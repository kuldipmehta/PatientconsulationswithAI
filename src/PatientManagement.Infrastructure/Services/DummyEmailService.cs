using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.DTOs.Emails;
using PatientManagement.Core.Entities;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Data;

namespace PatientManagement.Infrastructure.Services;

public class DummyEmailService : IEmailService
{
    private readonly ILogger<DummyEmailService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly ConcurrentBag<SentEmailLog> _inMemoryEmailHistory = new();

    public DummyEmailService(ILogger<DummyEmailService> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task<bool> SendPatientWelcomeEmailAsync(Patient patient, CancellationToken cancellationToken = default)
    {
        var subject = $"Welcome to Patient Care Portal - Registration Confirmed (ID: #{patient.Id})";

        var body = $"""
            ====================================================================================
            [PATIENT CARE PORTAL - WELCOME EMAIL NOTIFICATION]
            ====================================================================================
            Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
            To: {patient.FirstName} {patient.LastName} <{patient.Email}>
            Subject: {subject}

            Dear {patient.FirstName} {patient.LastName},

            Welcome to our Healthcare Portal! Your patient record has been successfully registered.

            Profile Details:
            -----------------------------------------
            - Patient ID: #{patient.Id}
            - Full Name: {patient.FirstName} {patient.LastName}
            - Email: {patient.Email}
            - Phone: {patient.PhoneNumber ?? "Not provided"}
            - Age: {patient.Age}
            - Gender: {patient.Gender}
            - Date of Birth: {(patient.DateOfBirth.HasValue ? patient.DateOfBirth.Value.ToString("yyyy-MM-dd") : "Not specified")}
            - Profile Picture: {(string.IsNullOrEmpty(patient.ProfilePictureUrl) ? "None (Default avatar)" : patient.ProfilePictureUrl)}
            - Registered On: {patient.CreatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC

            If you have any questions or need to schedule a consultation, please contact
            our support team or log into your portal account.

            Warm regards,
            Patient Management Team
            ====================================================================================
            """;

        return await SendEmailAsync(patient.Email, subject, body, patient.Id, cancellationToken);
    }

    public async Task<bool> SendEmailAsync(
        string toEmail,
        string subject,
        string body,
        int? patientId = null,
        CancellationToken cancellationToken = default)
    {
        var sentAt = DateTime.UtcNow;

        // 1. Structured console/log output
        _logger.LogInformation("""
            [EMAIL SERVICE] Dummy email dispatched successfully:
            To: {ToEmail}
            Patient ID: {PatientId}
            Subject: {Subject}
            Body Preview: {BodyPreview}
            SentAt: {SentAt}
            """,
            toEmail,
            patientId,
            subject,
            body.Length > 200 ? body[..200] + "..." : body,
            sentAt);

        // 2. Track in-memory for testing / query API
        var logEntry = new SentEmailLog
        {
            PatientId = patientId,
            RecipientEmail = toEmail,
            Subject = subject,
            Body = body,
            SentAtUtc = sentAt
        };
        _inMemoryEmailHistory.Add(logEntry);

        // 3. Persist to SQL Server database for audit trail
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var auditLog = new EmailAuditLog
            {
                PatientId = patientId,
                RecipientEmail = toEmail,
                Subject = subject,
                Body = body,
                Status = "Delivered (Simulated)",
                SentAtUtc = sentAt
            };

            dbContext.EmailAuditLogs.Add(auditLog);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist email audit log into database for recipient {Recipient}.", toEmail);
        }

        return true;
    }

    public Task<IReadOnlyList<SentEmailLog>> GetSentEmailsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SentEmailLog> list = _inMemoryEmailHistory.OrderByDescending(e => e.SentAtUtc).ToList();
        return Task.FromResult(list);
    }
}
