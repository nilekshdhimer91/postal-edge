using DNA.Email.Core.Interfaces;
using DNA.Email.Core.Models.Requests;
using DNA.Email.Core.Models.Responses;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace DNA.Email.API.Controllers;

[ApiController]
[Route("api/email")]
public sealed class EmailController(IEmailService emailService) : ControllerBase
{
    [HttpPost("send-text")]
    public async Task<IActionResult> SendText([FromBody] SendTextEmailRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelStateToResult());
        return ToActionResult(await emailService.SendTextAsync(req, ct));
    }

    [HttpPost("send-text-html")]
    public async Task<IActionResult> SendTextHtml([FromBody] SendTextHtmlEmailRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelStateToResult());
        return ToActionResult(await emailService.SendTextHtmlAsync(req, ct));
    }

    [HttpPost("send-html")]
    public async Task<IActionResult> SendHtml([FromBody] SendHtmlEmailRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelStateToResult());
        return ToActionResult(await emailService.SendHtmlAsync(req, ct));
    }

    [HttpPost("send-html-attachments")]
    [Consumes("application/json", "multipart/form-data")]
    public async Task<IActionResult> SendHtmlWithAttachments(CancellationToken ct)
    {
        var (req, files, error) = await ParseDualContentTypeRequest<SendHtmlAttachmentsEmailRequest>(ct);
        if (error is not null) return BadRequest(error);
        if (!ModelState.IsValid) return BadRequest(ModelStateToResult());
        return ToActionResult(await emailService.SendHtmlWithAttachmentsAsync(req!, files, ct));
    }

    [HttpPost("send")]
    [Consumes("application/json", "multipart/form-data")]
    public async Task<IActionResult> Send(CancellationToken ct)
    {
        var (req, files, error) = await ParseDualContentTypeRequest<SendEmailRequest>(ct);
        if (error is not null) return BadRequest(error);
        if (!ModelState.IsValid) return BadRequest(ModelStateToResult());
        return ToActionResult(await emailService.SendAsync(req!, files, ct));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(T? Request, IReadOnlyList<(string FileName, string ContentType, byte[] Data)>? Files, EmailSendResult? Error)>
        ParseDualContentTypeRequest<T>(CancellationToken ct) where T : class
    {
        if (Request.ContentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) == true)
        {
            var jsonPart = Request.Form["json"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(jsonPart))
                return (null, null, EmailSendResult.Fail(new EmailSendError
                {
                    Code = "INVALID_REQUEST",
                    Message = "For multipart/form-data, include a 'json' form field with the request JSON."
                }));

            T? req;
            try { req = JsonSerializer.Deserialize<T>(jsonPart, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
            catch (JsonException ex)
            {
                return (null, null, EmailSendResult.Fail(new EmailSendError
                {
                    Code = "INVALID_JSON",
                    Message = "Failed to parse the 'json' form field.",
                    Detail = ex.Message
                }));
            }

            if (req is null)
                return (null, null, EmailSendResult.Fail(new EmailSendError { Code = "INVALID_REQUEST", Message = "Request body is null." }));

            // Validate the deserialized model
            var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(req);
            var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
            if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(req, validationContext, validationResults, true))
            {
                var errors = validationResults
                    .GroupBy(r => r.MemberNames.FirstOrDefault() ?? string.Empty)
                    .ToDictionary(g => g.Key, g => g.Select(r => r.ErrorMessage ?? string.Empty).ToArray());
                return (null, null, EmailSendResult.Fail(new EmailSendError
                {
                    Code = "VALIDATION_ERROR",
                    Message = "One or more validation errors occurred.",
                    ValidationErrors = errors
                }));
            }

            var files = new List<(string, string, byte[])>();
            foreach (var file in Request.Form.Files)
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, ct);
                files.Add((file.FileName, file.ContentType, ms.ToArray()));
            }

            return (req, files, null);
        }

        // JSON path
        {
            T? req;
            try { req = await Request.ReadFromJsonAsync<T>(ct); }
            catch (JsonException ex)
            {
                return (null, null, EmailSendResult.Fail(new EmailSendError
                {
                    Code = "INVALID_JSON",
                    Message = "Failed to parse JSON request body.",
                    Detail = ex.Message
                }));
            }

            if (req is null)
                return (null, null, EmailSendResult.Fail(new EmailSendError { Code = "INVALID_REQUEST", Message = "Request body is null." }));

            // Manually validate for JSON path too
            TryValidateModel(req);
            return (req, null, null);
        }
    }

    private EmailSendResult ModelStateToResult()
    {
        var errors = ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                e => e.Key,
                e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray());

        return EmailSendResult.Fail(new EmailSendError
        {
            Code = "VALIDATION_ERROR",
            Message = "One or more validation errors occurred.",
            ValidationErrors = errors
        });
    }

    private IActionResult ToActionResult(EmailSendResult result)
    {
        if (result.Success) return Ok(result);

        return result.Error?.Code switch
        {
            "VALIDATION_ERROR" => BadRequest(result),
            "SMTP_NOT_CONFIGURED" => StatusCode(503, result),
            "CANCELLED" => StatusCode(499, result),
            _ => StatusCode(502, result)
        };
    }
}
