namespace DNA.Email.Core.Interfaces;

public interface ISmtpConnectionTester
{
    Task<(bool Success, string Message, string? Detail)> TestConnectionAsync(CancellationToken ct = default);
}
