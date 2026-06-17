using DNA.Email.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DNA.Email.API.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController(ISmtpConnectionTester tester) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "healthy",
        timestamp = DateTime.UtcNow
    });

    [HttpGet("smtp")]
    public async Task<IActionResult> GetSmtp(CancellationToken ct)
    {
        var (success, message, detail) = await tester.TestConnectionAsync(ct);
        var result = new { success, message, detail, timestamp = DateTime.UtcNow };
        return success ? Ok(result) : StatusCode(503, result);
    }
}
