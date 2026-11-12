using PatientManagement.Core.DTOs.Emails;
using PatientManagement.Core.Entities;

namespace PatientManagement.Core.Interfaces;

public interface IEmailService
{
    Task<bool> SendPatientWelcomeEmailAsync(Patient patient, string? oneTimeSetupLink = null, CancellationToken cancellationToken = default);
    Task<bool> SendPasswordSetupEmailAsync(AppUser user, string oneTimeSetupLink, CancellationToken cancellationToken = default);
    Task<bool> SendEmailAsync(string toEmail, string subject, string body, int? patientId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SentEmailLog>> GetSentEmailsAsync(CancellationToken cancellationToken = default);
}
