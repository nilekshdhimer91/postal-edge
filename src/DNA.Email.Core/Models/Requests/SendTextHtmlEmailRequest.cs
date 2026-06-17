using System.ComponentModel.DataAnnotations;

namespace DNA.Email.Core.Models.Requests;

public sealed class SendTextHtmlEmailRequest
{
    [Required] public EmailAddress From { get; init; } = new();
    [Required, MinLength(1, ErrorMessage = "At least one To recipient is required.")]
    public List<EmailAddress> To { get; init; } = [];
    public List<EmailAddress> Cc { get; init; } = [];
    public List<EmailAddress> Bcc { get; init; } = [];

    [Required, MaxLength(998)] public string Subject { get; init; } = string.Empty;

    [Required(ErrorMessage = "TextBody is required.")]
    public string TextBody { get; init; } = string.Empty;

    [Required(ErrorMessage = "HtmlBody is required.")]
    public string HtmlBody { get; init; } = string.Empty;

    public RequestSmtpSettings? SmtpSettings { get; init; }
}
