using System.ComponentModel.DataAnnotations;

namespace DNA.Email.Core.Models.Requests;

public sealed class SendEmailRequest : IValidatableObject
{
    [Required] public EmailAddress From { get; init; } = new();
    [Required, MinLength(1, ErrorMessage = "At least one To recipient is required.")]
    public List<EmailAddress> To { get; init; } = [];
    public List<EmailAddress> Cc { get; init; } = [];
    public List<EmailAddress> Bcc { get; init; } = [];
    public EmailAddress? ReplyTo { get; init; }

    [Required, MaxLength(998)] public string Subject { get; init; } = string.Empty;

    public string? TextBody { get; init; }
    public string? HtmlBody { get; init; }

    public Dictionary<string, string> TemplateVariables { get; init; } = [];

    public EmailPriority Priority { get; init; } = EmailPriority.Normal;
    public EmailSensitivity Sensitivity { get; init; } = EmailSensitivity.Normal;

    public bool RequestDeliveryReceipt { get; init; }
    public bool RequestReadReceipt { get; init; }

    [MaxLength(2048)]
    public string? UnsubscribeUrl { get; init; }

    public List<EmailAttachment> Attachments { get; init; } = [];
    public Dictionary<string, string> CustomHeaders { get; init; } = [];

    public EmailSigningRequest? Signing { get; init; }
    public RequestSmtpSettings? SmtpSettings { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(TextBody) && string.IsNullOrWhiteSpace(HtmlBody))
            yield return new ValidationResult(
                "At least one of TextBody or HtmlBody must be provided.",
                [nameof(TextBody), nameof(HtmlBody)]);

        if (Signing?.Type is SigningType.Dkim or SigningType.Both && Signing.DkimConfig is null)
            yield return new ValidationResult(
                "DkimConfig is required when signing type is Dkim or Both.",
                [nameof(Signing)]);
    }
}
