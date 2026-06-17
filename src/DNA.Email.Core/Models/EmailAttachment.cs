using System.ComponentModel.DataAnnotations;

namespace DNA.Email.Core.Models;

public sealed class EmailAttachment
{
    [Required]
    [MaxLength(260)]
    public string FileName { get; init; } = string.Empty;

    [Required]
    [MaxLength(127)]
    public string ContentType { get; init; } = "application/octet-stream";

    /// <summary>Base64-encoded file content. Used when sending via JSON body.</summary>
    public string? Base64Content { get; init; }

    /// <summary>Raw bytes — populated when attachment arrives via multipart/form-data.</summary>
    public byte[]? Data { get; init; }

    /// <summary>Optional Content-ID for inline images referenced in HTML as cid:value.</summary>
    public string? ContentId { get; init; }
}
