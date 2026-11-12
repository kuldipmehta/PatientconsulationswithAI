using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.DTOs.Patients;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Doctor,Receptionist")]
[Produces("application/json")]
public class PatientsController : ControllerBase
{
    private readonly IPatientService _patientService;

    public PatientsController(IPatientService patientService)
    {
        _patientService = patientService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PatientSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] PatientFilterParams filter, CancellationToken cancellationToken)
    {
        var result = await _patientService.GetPatientsAsync(filter, cancellationToken);
        return Ok(ApiResponse<PagedResult<PatientSummaryDto>>.Ok(result, "Patients retrieved successfully"));
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<PatientResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var patient = await _patientService.GetPatientByIdAsync(id, cancellationToken);
        if (patient == null)
        {
            return NotFound(ApiResponse<object>.Fail($"Patient with ID {id} was not found."));
        }

        return Ok(ApiResponse<PatientResponseDto>.Ok(patient, "Patient retrieved successfully"));
    }

    [HttpGet("{id:int}/picture")]
    public async Task<IActionResult> GetPicture(int id, CancellationToken cancellationToken)
    {
        var blob = await _patientService.GetPatientPictureAsync(id, cancellationToken);
        if (blob == null)
        {
            return NotFound();
        }

        return File(blob.Content, blob.ContentType);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Receptionist,Doctor")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<PatientResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromForm] CreatePatientDto dto, CancellationToken cancellationToken)
    {
        var createdPatient = await _patientService.CreatePatientAsync(dto, cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = createdPatient.Id },
            ApiResponse<PatientResponseDto>.Ok(createdPatient, "Patient registered securely, picture stored privately, and onboarding notification logged."));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Receptionist,Doctor")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<PatientResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(int id, [FromForm] UpdatePatientDto dto, CancellationToken cancellationToken)
    {
        var updatedPatient = await _patientService.UpdatePatientAsync(id, dto, cancellationToken);
        if (updatedPatient == null)
        {
            return NotFound(ApiResponse<object>.Fail($"Patient with ID {id} was not found."));
        }

        return Ok(ApiResponse<PatientResponseDto>.Ok(updatedPatient, "Patient updated successfully"));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var deleted = await _patientService.DeletePatientAsync(id, cancellationToken);
        if (!deleted)
        {
            return NotFound(ApiResponse<object>.Fail($"Patient with ID {id} was not found."));
        }

        return Ok(ApiResponse<bool>.Ok(true, "Patient archived (soft-deleted) successfully with audit trail."));
    }
}
