using Microsoft.AspNetCore.Mvc;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.DTOs.Emails;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class EmailsController : ControllerBase
{
    private readonly IEmailService _emailService;

    public EmailsController(IEmailService emailService)
    {
        _emailService = emailService;
    }

    /// <summary>
    /// View sent dummy email notifications dispatched by the system
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of sent emails</returns>
    [HttpGet("logs")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SentEmailLog>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLogs(CancellationToken cancellationToken)
    {
        var logs = await _emailService.GetSentEmailsAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<SentEmailLog>>.Ok(logs, "Email dispatch history retrieved successfully"));
    }
}
