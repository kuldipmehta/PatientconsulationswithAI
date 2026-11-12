using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.DTOs.Doctors;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Doctor,Receptionist")]
[Produces("application/json")]
public class DoctorsController : ControllerBase
{
    private readonly IDoctorService _doctorService;

    public DoctorsController(IDoctorService doctorService)
    {
        _doctorService = doctorService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DoctorDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var doctors = await _doctorService.GetDoctorsAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<DoctorDto>>.Ok(doctors, "Doctors retrieved successfully."));
    }

    [HttpGet("{id:int}/availability")]
    [ProducesResponseType(typeof(ApiResponse<DoctorAvailabilityResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvailability(int id, [FromQuery] DateTime? dateUtc, CancellationToken cancellationToken)
    {
        var targetDate = dateUtc?.Date ?? DateTime.UtcNow.Date;
        var availability = await _doctorService.GetAvailabilityAsync(id, targetDate, cancellationToken);
        if (availability == null)
        {
            return NotFound(ApiResponse<object>.Fail($"Doctor with ID {id} was not found."));
        }

        return Ok(ApiResponse<DoctorAvailabilityResponseDto>.Ok(availability, "Doctor availability slots retrieved."));
    }
}
