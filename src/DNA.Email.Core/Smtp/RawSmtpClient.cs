using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace DNA.Email.Core.Smtp;

public sealed class RawSmtpClient : IAsyncDisposable
{
    private TcpClient? _tcp;
    private Stream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public async Task ConnectAsync(string host, int port, bool implicitSsl, int timeoutMs, CancellationToken ct)
    {
        _tcp = new TcpClient { ReceiveTimeout = timeoutMs, SendTimeout = timeoutMs };
        await _tcp.ConnectAsync(host, port, ct);

        Stream baseStream = _tcp.GetStream();

        if (implicitSsl)
        {
            var ssl = new SslStream(baseStream, false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.None
            }, ct);
            _stream = ssl;
        }
        else
        {
            _stream = baseStream;
        }

        _reader = new StreamReader(_stream, Encoding.ASCII, leaveOpen: true);
        _writer = new StreamWriter(_stream, Encoding.ASCII, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };

        // Read greeting
        await ReadResponseAsync(220, ct);

        // EHLO
        await SendCommandAsync($"EHLO {host}", ct);
        var ehloResp = await ReadMultilineResponseAsync(ct);

        if (!implicitSsl)
        {
            // STARTTLS upgrade
            if (!ehloResp.Any(l => l.Contains("STARTTLS", StringComparison.OrdinalIgnoreCase)))
                throw new SmtpProtocolException(530, "Server does not support STARTTLS.");

            await SendCommandAsync("STARTTLS", ct);
            await ReadResponseAsync(220, ct);

            var sslUpgrade = new SslStream(baseStream, false);
            await sslUpgrade.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.None
            }, ct);
            _stream = sslUpgrade;
            _reader = new StreamReader(_stream, Encoding.ASCII, leaveOpen: true);
            _writer = new StreamWriter(_stream, Encoding.ASCII, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };

            // Re-EHLO after upgrade
            await SendCommandAsync($"EHLO {host}", ct);
            await ReadMultilineResponseAsync(ct);
        }
    }

    public async Task AuthenticateAsync(string username, string password, CancellationToken ct)
    {
        // Try AUTH PLAIN first
        try
        {
            var plain = Convert.ToBase64String(Encoding.UTF8.GetBytes($"\0{username}\0{password}"));
            await SendCommandAsync($"AUTH PLAIN {plain}", ct);
            await ReadResponseAsync(235, ct);
            return;
        }
        catch (SmtpProtocolException) { /* fall through to LOGIN */ }

        // AUTH LOGIN fallback
        await SendCommandAsync("AUTH LOGIN", ct);
        await ReadResponseAsync(334, ct);
        await SendCommandAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(username)), ct);
        await ReadResponseAsync(334, ct);
        await SendCommandAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(password)), ct);
        await ReadResponseAsync(235, ct);
    }

    public async Task SendMessageAsync(string from, IEnumerable<string> recipients, string rawMime, CancellationToken ct)
    {
        await SendCommandAsync($"MAIL FROM:<{from}>", ct);
        await ReadResponseAsync(250, ct);

        foreach (var rcpt in recipients)
        {
            await SendCommandAsync($"RCPT TO:<{rcpt}>", ct);
            await ReadResponseAsync(250, ct);
        }

        await SendCommandAsync("DATA", ct);
        await ReadResponseAsync(354, ct);

        // Dot-stuffing per RFC 5321 §4.5.2
        var stuffed = DotStuff(rawMime);
        await _writer!.WriteAsync(stuffed);
        await _writer.WriteLineAsync(".");
        await _writer.FlushAsync(ct);
        await ReadResponseAsync(250, ct);

        await SendCommandAsync("QUIT", ct);
    }

    private static string DotStuff(string message)
    {
        var sb = new StringBuilder();
        foreach (var line in message.Split("\r\n"))
            sb.AppendLine(line.StartsWith('.') ? "." + line : line);
        return sb.ToString();
    }

    private async Task SendCommandAsync(string command, CancellationToken ct)
    {
        await _writer!.WriteLineAsync(command.AsMemory(), ct);
        await _writer.FlushAsync(ct);
    }

    private async Task<string> ReadResponseAsync(int expectedCode, CancellationToken ct)
    {
        var line = await _reader!.ReadLineAsync(ct) ?? string.Empty;
        var code = ParseCode(line);
        if (code != expectedCode)
            throw new SmtpProtocolException(code, line);
        return line;
    }

    private async Task<List<string>> ReadMultilineResponseAsync(CancellationToken ct)
    {
        var lines = new List<string>();
        string line;
        do
        {
            line = await _reader!.ReadLineAsync(ct) ?? string.Empty;
            lines.Add(line);
        } while (line.Length >= 4 && line[3] == '-');

        var code = ParseCode(lines[^1]);
        if (code < 200 || code > 399)
            throw new SmtpProtocolException(code, lines[^1]);

        return lines;
    }

    private static int ParseCode(string line) =>
        line.Length >= 3 && int.TryParse(line.AsSpan(0, 3), out var c) ? c : 0;

    public async ValueTask DisposeAsync()
    {
        if (_writer is not null) { try { await _writer.DisposeAsync(); } catch { } }
        if (_reader is not null) { try { _reader.Dispose(); } catch { } }
        if (_stream is not null) { try { await _stream.DisposeAsync(); } catch { } }
        _tcp?.Dispose();
    }
}

public sealed class SmtpProtocolException(int code, string message) : Exception(message)
{
    public int SmtpCode { get; } = code;
}
