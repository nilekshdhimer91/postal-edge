namespace DNA.Email.Core.Options;

public sealed class ApiKeyOptions
{
    public const string SectionName = "ApiKey";

    public string HeaderName { get; set; } = "X-Api-Key";
    public List<ApiKeyEntry> Keys { get; set; } = [];
}

public sealed class ApiKeyEntry
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ApiKeyRateLimit? RateLimit { get; set; }
}

public sealed class ApiKeyRateLimit
{
    public int PermitLimit { get; set; } = 100;
    public int WindowSeconds { get; set; } = 60;
    public int QueueLimit { get; set; } = 0;
}
