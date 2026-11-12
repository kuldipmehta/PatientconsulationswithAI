namespace PatientManagement.Core.DTOs.Doctors;

public class DoctorDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int StartHourUtc { get; set; }
    public int EndHourUtc { get; set; }
    public int SlotDurationMinutes { get; set; }
}

public class TimeSlotDto
{
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public bool IsAvailable { get; set; }
}

public class DoctorAvailabilityResponseDto
{
    public int DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;
    public DateTime DateUtc { get; set; }
    public List<TimeSlotDto> Slots { get; set; } = new();
}
