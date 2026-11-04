using System.ComponentModel.DataAnnotations;
using PatientManagement.Core.Enums;

namespace PatientManagement.Core.DTOs.Consultations;

public class UpdateConsultationDto
{
    [Required(ErrorMessage = "Doctor name is required")]
    [StringLength(100, ErrorMessage = "Doctor name cannot exceed 100 characters")]
    public string DoctorName { get; set; } = string.Empty;

    public DateTime ConsultationDate { get; set; }

    [Required(ErrorMessage = "Diagnosis is required")]
    [StringLength(250, ErrorMessage = "Diagnosis cannot exceed 250 characters")]
    public string Diagnosis { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Symptoms cannot exceed 500 characters")]
    public string? Symptoms { get; set; }

    [StringLength(2000, ErrorMessage = "Notes cannot exceed 2000 characters")]
    public string? Notes { get; set; }

    [StringLength(1000, ErrorMessage = "Treatment plan cannot exceed 1000 characters")]
    public string? TreatmentPlan { get; set; }

    [StringLength(1000, ErrorMessage = "Prescription cannot exceed 1000 characters")]
    public string? Prescription { get; set; }

    public ConsultationStatus Status { get; set; }

    [Range(0, 100000, ErrorMessage = "Fee must be a non-negative value")]
    public decimal? Fee { get; set; }
}
