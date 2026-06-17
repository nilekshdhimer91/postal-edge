using System.ComponentModel.DataAnnotations;

namespace DNA.Email.Core.Models;

public sealed class RequestSmtpSettings
{
    [Required(ErrorMessage = "SMTP Host is required.")]
    [MaxLength(253)]
    public string Host { get; init; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; init; } = 587;

    public bool EnableSsl { get; init; } = true;

    [Required(ErrorMessage = "SMTP Username is required.")]
    [MaxLength(320)]
    public string Username { get; init; } = string.Empty;

    [Required(ErrorMessage = "SMTP Password is required.")]
    [MaxLength(256)]
    public string Password { get; init; } = string.Empty;

    public int TimeoutMs { get; init; } = 30_000;

    [MaxLength(256)]
    public string? DisplayName { get; init; }
}
