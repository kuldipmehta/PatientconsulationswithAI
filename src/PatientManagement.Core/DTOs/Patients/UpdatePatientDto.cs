using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace PatientManagement.Core.DTOs.Patients;

public class UpdatePatientDto
{
    [Required(ErrorMessage = "First name is required")]
    [StringLength(50, ErrorMessage = "First name cannot exceed 50 characters")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required")]
    [StringLength(50, ErrorMessage = "Last name cannot exceed 50 characters")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [StringLength(100, ErrorMessage = "Email cannot exceed 100 characters")]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Invalid phone number")]
    [StringLength(20, ErrorMessage = "Phone number cannot exceed 20 characters")]
    public string? PhoneNumber { get; set; }

    [Range(0, 130, ErrorMessage = "Age must be between 0 and 130")]
    public int Age { get; set; }

    [Required(ErrorMessage = "Gender is required")]
    [StringLength(20, ErrorMessage = "Gender cannot exceed 20 characters")]
    public string Gender { get; set; } = string.Empty;

    public DateTime? DateOfBirth { get; set; }

    [StringLength(200, ErrorMessage = "Address cannot exceed 200 characters")]
    public string? Address { get; set; }

    [StringLength(1000, ErrorMessage = "Medical notes cannot exceed 1000 characters")]
    public string? MedicalNotes { get; set; }

    /// <summary>
    /// Optional replacement profile picture file to upload to Azure Blob Storage
    /// </summary>
    public IFormFile? Picture { get; set; }
}
