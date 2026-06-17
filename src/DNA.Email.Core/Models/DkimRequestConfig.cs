using System.ComponentModel.DataAnnotations;

namespace DNA.Email.Core.Models;

/// <summary>
/// Per-request DKIM configuration. Private key travels over the wire — HTTPS only.
/// </summary>
public sealed class DkimRequestConfig
{
    [Required]
    [MaxLength(253)]
    public string Domain { get; init; } = string.Empty;

    [Required]
    [MaxLength(63)]
    public string Selector { get; init; } = "mail";

    [Required]
    public string PrivateKeyPem { get; init; } = string.Empty;

    public string[] HeadersToSign { get; init; } = ["from", "to", "subject", "reply-to", "content-type"];
}
