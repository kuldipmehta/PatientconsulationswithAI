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

    public async Task<bool> SendPatientWelcomeEmailAsync(
        Patient patient,
        string? oneTimeSetupLink = null,
        CancellationToken cancellationToken = default)
    {
        var subject = $"Welcome to Patient Care Portal - Registration Confirmed (ID: #{patient.Id})";

        var onboardingSection = string.IsNullOrWhiteSpace(oneTimeSetupLink)
            ? "To activate portal access, please request a one-time account setup link from reception."
            : $"""
               Complete Your Portal Setup (One-Time Link, Valid for 24 Hours):
               {oneTimeSetupLink}
               (For your security, passwords are never sent by email.)
               """;

        var body = $"""
            ====================================================================================
            [PATIENT CARE PORTAL - WELCOME NOTIFICATION]
            ====================================================================================
            Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
            To: {patient.FirstName} {patient.LastName} <{patient.Email}>
            Subject: {subject}

            Dear {patient.FirstName} {patient.LastName},

            Your patient profile has been successfully registered with Patient Care Portal.

            Registration Summary:
            -----------------------------------------
            - Patient ID: #{patient.Id}
            - Full Name: {patient.FirstName} {patient.LastName}
            - Registered On: {patient.CreatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC

            {onboardingSection}

            Warm regards,
            Patient Management Team
            ====================================================================================
            """;

        return await SendEmailAsync(patient.Email, subject, body, patient.Id, cancellationToken);
    }

    public async Task<bool> SendPasswordSetupEmailAsync(
        AppUser user,
        string oneTimeSetupLink,
        CancellationToken cancellationToken = default)
    {
        var subject = "Set Your Patient Care Portal Password (One-Time Link)";
        var body = $"""
            Hello {user.FullName},

            Use the one-time secure link below to set your password (valid for 24 hours):
            {oneTimeSetupLink}

            If you did not expect this message, please contact your clinic administrator.
            """;

        return await SendEmailAsync(user.Email, subject, body, null, cancellationToken);
    }

    public async Task<bool> SendEmailAsync(
        string toEmail,
        string subject,
        string body,
        int? patientId = null,
        CancellationToken cancellationToken = default)
    {
        var sentAt = DateTime.UtcNow;

        _logger.LogInformation(
            "[EMAIL SERVICE] Notification logged for {ToEmail} (PatientId={PatientId}, Subject={Subject})",
            toEmail,
            patientId,
            subject);

        var logEntry = new SentEmailLog
        {
            PatientId = patientId,
            RecipientEmail = toEmail,
            Subject = subject,
            Body = body,
            SentAtUtc = sentAt
        };
        _inMemoryEmailHistory.Add(logEntry);

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
