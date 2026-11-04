using PatientManagement.Core.DTOs.Emails;
using PatientManagement.Core.Entities;

namespace PatientManagement.Core.Interfaces;

public interface IEmailService
{
    Task<bool> SendPatientWelcomeEmailAsync(Patient patient, CancellationToken cancellationToken = default);
    Task<bool> SendEmailAsync(string toEmail, string subject, string body, int? patientId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SentEmailLog>> GetSentEmailsAsync(CancellationToken cancellationToken = default);
}
