using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PatientManagement.Core.DTOs.Audit;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public class AuditController : ControllerBase
{
    private readonly IAuditService _auditService;

    public AuditController(IAuditService auditService)
    {
        _auditService = auditService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AuditLogDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAuditLogs([FromQuery] int take = 100, CancellationToken cancellationToken = default)
    {
        var logs = await _auditService.GetAuditLogsAsync(take, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AuditLogDto>>.Ok(logs, "Audit trail retrieved successfully."));
    }

    [HttpGet("ai")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AiInteractionLogDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAiLogs([FromQuery] int take = 100, CancellationToken cancellationToken = default)
    {
        var logs = await _auditService.GetAiInteractionLogsAsync(take, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AiInteractionLogDto>>.Ok(logs, "AI interaction logs retrieved successfully."));
    }
}
