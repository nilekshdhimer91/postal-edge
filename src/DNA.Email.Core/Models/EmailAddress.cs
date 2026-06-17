using System.ComponentModel.DataAnnotations;

namespace DNA.Email.Core.Models;

public sealed class EmailAddress : IValidatableObject
{
    [Required(ErrorMessage = "Email address is required.")]
    [MaxLength(320)]
    public string Email { get; init; } = string.Empty;

    [MaxLength(256)]
    public string? DisplayName { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Email)) yield break;

        if (!IsValidEmail(Email))
            yield return new ValidationResult(
                $"'{Email}' is not a valid email address.",
                [nameof(Email)]);
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return string.Equals(addr.Address, email.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
