using Microsoft.AspNetCore.Mvc;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.DTOs.Consultations;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ConsultationsController : ControllerBase
{
    private readonly IConsultationService _consultationService;
    private readonly ILogger<ConsultationsController> _logger;

    public ConsultationsController(IConsultationService consultationService, ILogger<ConsultationsController> logger)
    {
        _consultationService = consultationService;
        _logger = logger;
    }

    /// <summary>
    /// Fetch list of consultations, optionally filtered by Patient ID
    /// </summary>
    /// <param name="patientId">Optional filter by patient identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of consultation records</returns>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<ConsultationResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] int? patientId, CancellationToken cancellationToken)
    {
        var result = await _consultationService.GetConsultationsAsync(patientId, cancellationToken);
        return Ok(ApiResponse<IEnumerable<ConsultationResponseDto>>.Ok(result, "Consultations retrieved successfully"));
    }

    /// <summary>
    /// Fetch a single consultation by ID
    /// </summary>
    /// <param name="id">Consultation identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Consultation record</returns>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<ConsultationResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var consultation = await _consultationService.GetConsultationByIdAsync(id, cancellationToken);
        if (consultation == null)
        {
            return NotFound(ApiResponse<object>.Fail($"Consultation with ID {id} was not found."));
        }

        return Ok(ApiResponse<ConsultationResponseDto>.Ok(consultation, "Consultation retrieved successfully"));
    }

    /// <summary>
    /// Create a new consultation for an existing patient in SQL Server
    /// </summary>
    /// <param name="dto">Consultation creation payload</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Newly created consultation</returns>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ConsultationResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreateConsultationDto dto, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            return BadRequest(ApiResponse<object>.Fail("Validation failed", errors));
        }

        try
        {
            var created = await _consultationService.CreateConsultationAsync(dto, cancellationToken);
            return CreatedAtAction(
                nameof(GetById),
                new { id = created.Id },
                ApiResponse<ConsultationResponseDto>.Ok(created, "Consultation created successfully"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Update existing consultation data using EF Core
    /// </summary>
    /// <param name="id">Consultation identifier</param>
    /// <param name="dto">Updated consultation data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated consultation</returns>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<ConsultationResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateConsultationDto dto, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            return BadRequest(ApiResponse<object>.Fail("Validation failed", errors));
        }

        var updated = await _consultationService.UpdateConsultationAsync(id, dto, cancellationToken);
        if (updated == null)
        {
            return NotFound(ApiResponse<object>.Fail($"Consultation with ID {id} was not found."));
        }

        return Ok(ApiResponse<ConsultationResponseDto>.Ok(updated, "Consultation updated successfully"));
    }

    /// <summary>
    /// Delete a consultation by ID
    /// </summary>
    /// <param name="id">Consultation identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success confirmation</returns>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var deleted = await _consultationService.DeleteConsultationAsync(id, cancellationToken);
        if (!deleted)
        {
            return NotFound(ApiResponse<object>.Fail($"Consultation with ID {id} was not found."));
        }

        return Ok(ApiResponse<bool>.Ok(true, "Consultation deleted successfully"));
    }
}
