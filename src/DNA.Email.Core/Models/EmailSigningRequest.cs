namespace DNA.Email.Core.Models;

public enum SigningType
{
    None,
    Dkim,
    Smime,
    Both
}

public sealed class EmailSigningRequest
{
    public SigningType Type { get; init; } = SigningType.None;

    /// <summary>Required when Type is Dkim or Both.</summary>
    public DkimRequestConfig? DkimConfig { get; init; }
}
