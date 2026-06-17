namespace DNA.Email.Core.Models.Responses;

public sealed class EmailSendResult
{
    public bool Success { get; init; }
    public string MessageId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTime SentAtUtc { get; init; }
    public EmailSendSummary? Summary { get; init; }
    public EmailSendError? Error { get; init; }

    public static EmailSendResult Ok(string messageId, EmailSendSummary summary) => new()
    {
        Success = true,
        MessageId = messageId,
        Message = "Email sent successfully.",
        SentAtUtc = DateTime.UtcNow,
        Summary = summary
    };

    public static EmailSendResult Fail(EmailSendError error) => new()
    {
        Success = false,
        MessageId = string.Empty,
        Message = error.Message,
        SentAtUtc = default,
        Error = error
    };
}

public sealed class EmailSendSummary
{
    public string From { get; init; } = string.Empty;
    public List<string> To { get; init; } = [];
    public List<string> Cc { get; init; } = [];
    public string Subject { get; init; } = string.Empty;
    public bool HasTextBody { get; init; }
    public bool HasHtmlBody { get; init; }
    public int AttachmentCount { get; init; }
    public bool DkimSigned { get; init; }
    public bool SmimeSigned { get; init; }
    public string SmtpHost { get; init; } = string.Empty;
}

public sealed class EmailSendError
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public string? SmtpStatusCode { get; init; }
    public string? SmtpStatusDescription { get; init; }
    public string? InnerError { get; init; }
    public Dictionary<string, string[]>? ValidationErrors { get; init; }
}
