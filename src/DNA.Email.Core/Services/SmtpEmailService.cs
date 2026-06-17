using System.Text;
using DNA.Email.Core.Interfaces;
using DNA.Email.Core.Mime;
using DNA.Email.Core.Models;
using DNA.Email.Core.Models.Requests;
using DNA.Email.Core.Models.Responses;
using DNA.Email.Core.Options;
using DNA.Email.Core.Signing;
using DNA.Email.Core.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DNA.Email.Core.Services;

public sealed class SmtpEmailService : IEmailService, ISmtpConnectionTester
{
    private readonly SmtpOptions _defaultSmtp;
    private readonly SmimeOptions _smime;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(
        IOptions<SmtpOptions> smtp,
        IOptions<SmimeOptions> smime,
        ILogger<SmtpEmailService> logger)
    {
        _defaultSmtp = smtp.Value;
        _smime = smime.Value;
        _logger = logger;
    }

    // ── ISmtpConnectionTester ─────────────────────────────────────────────────

    public async Task<(bool Success, string Message, string? Detail)> TestConnectionAsync(CancellationToken ct = default)
    {
        if (!_defaultSmtp.IsConfigured)
            return (false, "No server-level SMTP configuration found. Provide SmtpSettings in your request instead.", null);

        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            await tcp.ConnectAsync(_defaultSmtp.Host, _defaultSmtp.Port, ct);
            return (true, $"TCP connection to {_defaultSmtp.Host}:{_defaultSmtp.Port} succeeded.", null);
        }
        catch (Exception ex)
        {
            return (false, $"Cannot reach SMTP server {_defaultSmtp.Host}:{_defaultSmtp.Port}.", ex.Message);
        }
    }

    // ── IEmailService ─────────────────────────────────────────────────────────

    public Task<EmailSendResult> SendTextAsync(SendTextEmailRequest req, CancellationToken ct = default)
    {
        var smtp = ResolveSmtp(req.SmtpSettings);
        if (smtp.IsError) return Task.FromResult(smtp.Error!);

        return ExecuteAsync(new MimeMessageOptions
        {
            From = req.From, To = req.To, Cc = req.Cc, Bcc = req.Bcc,
            Subject = req.Subject, TextBody = req.TextBody
        }, smtp.Options!, null, ct);
    }

    public Task<EmailSendResult> SendTextHtmlAsync(SendTextHtmlEmailRequest req, CancellationToken ct = default)
    {
        var smtp = ResolveSmtp(req.SmtpSettings);
        if (smtp.IsError) return Task.FromResult(smtp.Error!);

        return ExecuteAsync(new MimeMessageOptions
        {
            From = req.From, To = req.To, Cc = req.Cc, Bcc = req.Bcc,
            Subject = req.Subject, TextBody = req.TextBody, HtmlBody = req.HtmlBody
        }, smtp.Options!, null, ct);
    }

    public Task<EmailSendResult> SendHtmlAsync(SendHtmlEmailRequest req, CancellationToken ct = default)
    {
        var smtp = ResolveSmtp(req.SmtpSettings);
        if (smtp.IsError) return Task.FromResult(smtp.Error!);

        return ExecuteAsync(new MimeMessageOptions
        {
            From = req.From, To = req.To, Cc = req.Cc, Bcc = req.Bcc,
            Subject = req.Subject, HtmlBody = req.HtmlBody
        }, smtp.Options!, null, ct);
    }

    public Task<EmailSendResult> SendHtmlWithAttachmentsAsync(
        SendHtmlAttachmentsEmailRequest req,
        IReadOnlyList<(string FileName, string ContentType, byte[] Data)>? multipartFiles,
        CancellationToken ct = default)
    {
        var smtp = ResolveSmtp(req.SmtpSettings);
        if (smtp.IsError) return Task.FromResult(smtp.Error!);

        return ExecuteAsync(new MimeMessageOptions
        {
            From = req.From, To = req.To, Cc = req.Cc, Bcc = req.Bcc,
            Subject = req.Subject, HtmlBody = req.HtmlBody,
            Attachments = BuildAttachments(req.Attachments, multipartFiles)
        }, smtp.Options!, null, ct);
    }

    public Task<EmailSendResult> SendAsync(
        SendEmailRequest req,
        IReadOnlyList<(string FileName, string ContentType, byte[] Data)>? multipartFiles,
        CancellationToken ct = default)
    {
        var smtp = ResolveSmtp(req.SmtpSettings);
        if (smtp.IsError) return Task.FromResult(smtp.Error!);

        var subject = ApplyTemplateVars(req.Subject, req.TemplateVariables);
        var textBody = req.TextBody is not null ? ApplyTemplateVars(req.TextBody, req.TemplateVariables) : null;
        var htmlBody = req.HtmlBody is not null ? ApplyTemplateVars(req.HtmlBody, req.TemplateVariables) : null;

        return ExecuteAsync(new MimeMessageOptions
        {
            From = req.From, To = req.To, Cc = req.Cc, Bcc = req.Bcc, ReplyTo = req.ReplyTo,
            Subject = subject, TextBody = textBody, HtmlBody = htmlBody,
            Priority = req.Priority, Sensitivity = req.Sensitivity,
            RequestDeliveryReceipt = req.RequestDeliveryReceipt,
            RequestReadReceipt = req.RequestReadReceipt,
            UnsubscribeUrl = req.UnsubscribeUrl,
            Attachments = BuildAttachments(req.Attachments, multipartFiles),
            CustomHeaders = req.CustomHeaders
        }, smtp.Options!, req.Signing, ct);
    }

    // ── Core execution ────────────────────────────────────────────────────────

    private async Task<EmailSendResult> ExecuteAsync(
        MimeMessageOptions opts,
        SmtpOptions smtp,
        EmailSigningRequest? signing,
        CancellationToken ct)
    {
        var messageId = $"{Guid.NewGuid():N}@{ExtractDomain(opts.From.Email)}";

        bool signDkim = signing?.Type is SigningType.Dkim or SigningType.Both && signing.DkimConfig is not null;
        bool signSmime = signing?.Type is SigningType.Smime or SigningType.Both && _smime.Enabled;

        // BCC recipients go into the SMTP envelope (RCPT TO) but are never written to MIME headers
        var allRecipients = opts.To
            .Concat(opts.Cc)
            .Concat(opts.Bcc)
            .Select(a => a.Email)
            .ToList();

        try
        {
            await SendRawAsync(opts, smtp, allRecipients, signing, signDkim, signSmime, ct);

            _logger.LogInformation(
                "Email sent. Id={Id} From={From} To={To} Host={Host} Dkim={Dkim} Smime={Smime}",
                messageId, opts.From.Email, string.Join(",", allRecipients), smtp.Host, signDkim, signSmime);

            return EmailSendResult.Ok(messageId, new EmailSendSummary
            {
                From = opts.From.Email,
                To = opts.To.Select(a => a.Email).ToList(),
                Cc = opts.Cc.Select(a => a.Email).ToList(),
                Subject = opts.Subject,
                HasTextBody = !string.IsNullOrEmpty(opts.TextBody),
                HasHtmlBody = !string.IsNullOrEmpty(opts.HtmlBody),
                AttachmentCount = opts.Attachments.Count,
                DkimSigned = signDkim,
                SmimeSigned = signSmime,
                SmtpHost = smtp.Host
            });
        }
        catch (SmtpProtocolException ex)
        {
            _logger.LogError(ex, "Raw SMTP error. Code={Code}", ex.SmtpCode);
            return EmailSendResult.Fail(new EmailSendError
            {
                Code = "SMTP_PROTOCOL_ERROR",
                Message = "SMTP server rejected the message during transmission.",
                Detail = ex.Message,
                SmtpStatusCode = ex.SmtpCode.ToString()
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Email send cancelled. Id={Id}", messageId);
            return EmailSendResult.Fail(new EmailSendError
            {
                Code = "CANCELLED",
                Message = "The email send operation was cancelled before it completed."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error. Id={Id}", messageId);
            return EmailSendResult.Fail(new EmailSendError
            {
                Code = "INTERNAL_ERROR",
                Message = "An unexpected error occurred while sending the email.",
                Detail = ex.Message,
                InnerError = ex.InnerException?.Message
            });
        }
    }

    private async Task SendRawAsync(
        MimeMessageOptions opts,
        SmtpOptions smtp,
        List<string> recipients,
        EmailSigningRequest? signing,
        bool signDkim,
        bool signSmime,
        CancellationToken ct)
    {
        var rawMime = MimeBuilder.BuildMessage(opts);

        if (signDkim && signing?.DkimConfig is { } dkimCfg)
        {
            var dkimOptions = new DkimOptions
            {
                Enabled = true,
                Domain = dkimCfg.Domain,
                Selector = dkimCfg.Selector,
                PrivateKeyPem = dkimCfg.PrivateKeyPem,
                HeadersToSign = dkimCfg.HeadersToSign
            };
            using var signer = new DkimSigner(dkimOptions);
            var headers = ExtractHeaderDict(rawMime);
            var bodyStart = rawMime.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var body = bodyStart >= 0 ? rawMime[(bodyStart + 4)..] : rawMime;
            rawMime = signer.ComputeSignatureHeader(headers, body) + "\r\n" + rawMime;
        }

        if (signSmime && _smime.Enabled)
        {
            using var signer = new SmimeSigner(_smime);
            var bodyStart = rawMime.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (bodyStart >= 0)
            {
                var boundary = $"=_smime_{Guid.NewGuid():N}";
                var hdr = rawMime[..(bodyStart + 4)];
                var bodyBytes = Encoding.UTF8.GetBytes(rawMime[(bodyStart + 4)..]);
                var signedBytes = signer.Sign(bodyBytes);
                var b64 = WrapBase64(Convert.ToBase64String(signedBytes));

                var rebuilt = new StringBuilder(hdr);
                rebuilt.AppendLine($"--{boundary}");
                rebuilt.Append(rawMime[(bodyStart + 4)..]);
                rebuilt.AppendLine();
                rebuilt.AppendLine($"--{boundary}");
                rebuilt.AppendLine("Content-Type: application/pkcs7-signature; name=\"smime.p7s\"");
                rebuilt.AppendLine("Content-Transfer-Encoding: base64");
                rebuilt.AppendLine("Content-Disposition: attachment; filename=\"smime.p7s\"");
                rebuilt.AppendLine();
                rebuilt.AppendLine(b64);
                rebuilt.AppendLine($"--{boundary}--");
                rawMime = rebuilt.ToString();
            }
        }

        bool implicitSsl = smtp.Port == 465;
        await using var raw = new RawSmtpClient();
        await raw.ConnectAsync(smtp.Host, smtp.Port, implicitSsl, smtp.TimeoutMs, ct);
        await raw.AuthenticateAsync(smtp.Username, smtp.Password, ct);
        await raw.SendMessageAsync(opts.From.Email, recipients, rawMime, ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private SmtpResolution ResolveSmtp(RequestSmtpSettings? requestSettings)
    {
        if (requestSettings is not null)
        {
            return SmtpResolution.Success(new SmtpOptions
            {
                Host = requestSettings.Host,
                Port = requestSettings.Port,
                EnableSsl = requestSettings.EnableSsl,
                Username = requestSettings.Username,
                Password = requestSettings.Password,
                TimeoutMs = requestSettings.TimeoutMs,
                DisplayName = requestSettings.DisplayName
            });
        }

        if (_defaultSmtp.IsConfigured)
            return SmtpResolution.Success(_defaultSmtp);

        return SmtpResolution.Fail(EmailSendResult.Fail(new EmailSendError
        {
            Code = "SMTP_NOT_CONFIGURED",
            Message = "No SMTP server configured. " +
                      "Provide 'smtpSettings' in the request body, or configure Smtp__Host / Smtp__Username " +
                      "via environment variables on the server."
        }));
    }

    private static Dictionary<string, string> ExtractHeaderDict(string rawMime)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in rawMime.Split("\r\n"))
        {
            if (string.IsNullOrEmpty(line)) break;
            var idx = line.IndexOf(':');
            if (idx < 0) continue;
            dict[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }
        return dict;
    }

    private static List<MimeAttachment> BuildAttachments(
        List<Models.EmailAttachment> jsonAtts,
        IReadOnlyList<(string FileName, string ContentType, byte[] Data)>? multipart)
    {
        var list = new List<MimeAttachment>();
        foreach (var a in jsonAtts)
        {
            var data = a.Data ?? (a.Base64Content is not null ? Convert.FromBase64String(a.Base64Content) : null);
            if (data is null || data.Length == 0) continue;
            list.Add(new MimeAttachment { FileName = a.FileName, ContentType = a.ContentType, ContentId = a.ContentId, Data = data });
        }
        if (multipart is not null)
            foreach (var (fn, ct2, d) in multipart)
                list.Add(new MimeAttachment { FileName = fn, ContentType = ct2, Data = d });
        return list;
    }

    private static string ApplyTemplateVars(string template, Dictionary<string, string> vars)
    {
        foreach (var (k, v) in vars)
            template = template.Replace($"{{{{{k}}}}}", v, StringComparison.OrdinalIgnoreCase);
        return template;
    }

    private static string ExtractDomain(string email)
    {
        var i = email.IndexOf('@');
        return i >= 0 ? email[(i + 1)..] : "local";
    }

    private static string WrapBase64(string b64, int w = 76)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < b64.Length; i += w)
            sb.AppendLine(b64.AsSpan(i, Math.Min(w, b64.Length - i)).ToString());
        return sb.ToString().TrimEnd();
    }
}

internal sealed class SmtpResolution
{
    public bool IsError { get; private init; }
    public SmtpOptions? Options { get; private init; }
    public EmailSendResult? Error { get; private init; }

    public static SmtpResolution Success(SmtpOptions opts) => new() { Options = opts };
    public static SmtpResolution Fail(EmailSendResult err) => new() { IsError = true, Error = err };
}
