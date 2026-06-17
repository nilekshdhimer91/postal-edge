namespace DNA.Email.Core.Options;

public sealed class DkimOptions
{
    public const string SectionName = "Dkim";

    public bool Enabled { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string Selector { get; set; } = "mail";
    public string PrivateKeyPem { get; set; } = string.Empty;
    public string[] HeadersToSign { get; set; } = ["from", "to", "subject", "reply-to", "content-type"];
}
