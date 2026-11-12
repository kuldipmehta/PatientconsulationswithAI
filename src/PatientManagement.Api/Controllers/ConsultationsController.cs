using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.DTOs.Consultations;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Doctor,Receptionist")]
[Produces("application/json")]
public class ConsultationsController : ControllerBase
{
    private readonly IConsultationService _consultationService;

    public ConsultationsController(IConsultationService consultationService)
    {
        _consultationService = consultationService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<ConsultationResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] int? patientId, CancellationToken cancellationToken)
    {
        var result = await _consultationService.GetConsultationsAsync(patientId, cancellationToken);
        return Ok(ApiResponse<IEnumerable<ConsultationResponseDto>>.Ok(result, "Consultations retrieved successfully"));
    }

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

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ConsultationResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateConsultationDto dto, CancellationToken cancellationToken)
    {
        var created = await _consultationService.CreateConsultationAsync(dto, cancellationToken);
        return CreatedAtAction(
            nameof(GetById),
            new { id = created.Id },
            ApiResponse<ConsultationResponseDto>.Ok(created, "Consultation scheduled successfully"));
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<ConsultationResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateConsultationDto dto, CancellationToken cancellationToken)
    {
        var updated = await _consultationService.UpdateConsultationAsync(id, dto, cancellationToken);
        if (updated == null)
        {
            return NotFound(ApiResponse<object>.Fail($"Consultation with ID {id} was not found."));
        }

        return Ok(ApiResponse<ConsultationResponseDto>.Ok(updated, "Consultation updated successfully"));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin,Doctor")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var deleted = await _consultationService.DeleteConsultationAsync(id, cancellationToken);
        if (!deleted)
        {
            return NotFound(ApiResponse<object>.Fail($"Consultation with ID {id} was not found."));
        }

        return Ok(ApiResponse<bool>.Ok(true, "Consultation archived (soft-deleted) successfully"));
    }
}
