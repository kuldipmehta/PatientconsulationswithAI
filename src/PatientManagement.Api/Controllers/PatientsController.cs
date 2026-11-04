using Microsoft.AspNetCore.Mvc;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.DTOs.Patients;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class PatientsController : ControllerBase
{
    private readonly IPatientService _patientService;
    private readonly ILogger<PatientsController> _logger;

    public PatientsController(IPatientService patientService, ILogger<PatientsController> logger)
    {
        _patientService = patientService;
        _logger = logger;
    }

    /// <summary>
    /// View Patient List: Display a list of patients fetched from SQL Server with filtering, sorting, and pagination
    /// </summary>
    /// <param name="filter">Filter, sort, and pagination criteria</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paged list of patients with consultation counts</returns>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PatientSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] PatientFilterParams filter, CancellationToken cancellationToken)
    {
        var result = await _patientService.GetPatientsAsync(filter, cancellationToken);
        return Ok(ApiResponse<PagedResult<PatientSummaryDto>>.Ok(result, "Patients retrieved successfully"));
    }

    /// <summary>
    /// Fetch single patient details by ID with their complete consultation history
    /// </summary>
    /// <param name="id">Patient unique identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Patient details with consultations</returns>
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

    /// <summary>
    /// Create a Patient: Accepts multipart form data with basic details and picture file, stores picture in Azure Blob Storage, saves to SQL Server, and dispatches a dummy email
    /// </summary>
    /// <param name="dto">Patient details form including optional picture file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Newly created patient details</returns>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<PatientResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromForm] CreatePatientDto dto, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            return BadRequest(ApiResponse<object>.Fail("Validation failed", errors));
        }

        var createdPatient = await _patientService.CreatePatientAsync(dto, cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = createdPatient.Id },
            ApiResponse<PatientResponseDto>.Ok(createdPatient, "Patient created successfully, picture stored in Azure Blob Storage, and welcome email sent."));
    }

    /// <summary>
    /// Update existing patient data in SQL Server using EF Core (supports updating details and replacement picture)
    /// </summary>
    /// <param name="id">Patient unique identifier</param>
    /// <param name="dto">Updated patient details and optional new picture</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated patient details</returns>
    [HttpPut("{id:int}")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<PatientResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromForm] UpdatePatientDto dto, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            return BadRequest(ApiResponse<object>.Fail("Validation failed", errors));
        }

        var updatedPatient = await _patientService.UpdatePatientAsync(id, dto, cancellationToken);
        if (updatedPatient == null)
        {
            return NotFound(ApiResponse<object>.Fail($"Patient with ID {id} was not found."));
        }

        return Ok(ApiResponse<PatientResponseDto>.Ok(updatedPatient, "Patient updated successfully"));
    }

    /// <summary>
    /// Delete patient and associated consultations from SQL Server
    /// </summary>
    /// <param name="id">Patient unique identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success confirmation</returns>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var deleted = await _patientService.DeletePatientAsync(id, cancellationToken);
        if (!deleted)
        {
            return NotFound(ApiResponse<object>.Fail($"Patient with ID {id} was not found."));
        }

        return Ok(ApiResponse<bool>.Ok(true, "Patient deleted successfully"));
    }
}
