using System.ComponentModel.DataAnnotations;

namespace PatientManagement.Core.Entities;

public class Doctor
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int StartHourUtc { get; set; } = 9;
    public int EndHourUtc { get; set; } = 17;
    public int SlotDurationMinutes { get; set; } = 30;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<Patient> AssignedPatients { get; set; } = new List<Patient>();
    public ICollection<Consultation> Consultations { get; set; } = new List<Consultation>();
}
