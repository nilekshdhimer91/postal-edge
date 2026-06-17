namespace DNA.Email.Core.Options;

public sealed class SmimeOptions
{
    public const string SectionName = "Smime";

    public bool Enabled { get; set; }
    public string CertificatePath { get; set; } = string.Empty;
    public string CertificatePassword { get; set; } = string.Empty;
}
