using System.Text;
using DNA.Email.Core.Models;
using DNA.Email.Core.Models.Responses;

namespace DNA.Email.Core.Mime;

public sealed class MimeMessageOptions
{
    public EmailAddress From { get; init; } = new();
    public List<EmailAddress> To { get; init; } = [];
    public List<EmailAddress> Cc { get; init; } = [];
    public List<EmailAddress> Bcc { get; init; } = [];
    public EmailAddress? ReplyTo { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string? TextBody { get; init; }
    public string? HtmlBody { get; init; }
    public EmailPriority Priority { get; init; } = EmailPriority.Normal;
    public EmailSensitivity Sensitivity { get; init; } = EmailSensitivity.Normal;
    public bool RequestDeliveryReceipt { get; init; }
    public bool RequestReadReceipt { get; init; }
    public string? UnsubscribeUrl { get; init; }
    public List<MimeAttachment> Attachments { get; init; } = [];
    public Dictionary<string, string> CustomHeaders { get; init; } = [];
}

public sealed class MimeAttachment
{
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = "application/octet-stream";
    public string? ContentId { get; init; }
    public byte[] Data { get; init; } = [];
}

public static class MimeBuilder
{
    public static string BuildMessage(MimeMessageOptions opts)
    {
        var sb = new StringBuilder();
        var date = DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss +0000");

        // Standard headers
        sb.AppendLine($"Date: {date}");
        sb.AppendLine($"From: {FormatAddress(opts.From)}");
        sb.AppendLine($"To: {string.Join(", ", opts.To.Select(FormatAddress))}");

        if (opts.Cc.Count > 0)
            sb.AppendLine($"Cc: {string.Join(", ", opts.Cc.Select(FormatAddress))}");

        // BCC intentionally omitted from headers (RFC correct — only in SMTP envelope)

        if (opts.ReplyTo is not null)
            sb.AppendLine($"Reply-To: {FormatAddress(opts.ReplyTo)}");

        sb.AppendLine($"Subject: {EncodeHeader(opts.Subject)}");
        sb.AppendLine("MIME-Version: 1.0");
        sb.AppendLine($"Message-ID: <{Guid.NewGuid():N}@dna.local>");

        if (opts.Priority != EmailPriority.Normal)
        {
            var xPriority = opts.Priority == EmailPriority.High ? "1 (High)" : "5 (Low)";
            sb.AppendLine($"X-Priority: {xPriority}");
            sb.AppendLine($"Importance: {(opts.Priority == EmailPriority.High ? "High" : "Low")}");
        }

        if (opts.Sensitivity != EmailSensitivity.Normal)
        {
            sb.AppendLine($"Sensitivity: {opts.Sensitivity switch
            {
                EmailSensitivity.Personal => "Personal",
                EmailSensitivity.Private => "Private",
                EmailSensitivity.CompanyConfidential => "Company-Confidential",
                _ => "Normal"
            }}");
        }

        if (opts.RequestDeliveryReceipt)
            sb.AppendLine($"Return-Receipt-To: {opts.From.Email}");

        if (opts.RequestReadReceipt)
            sb.AppendLine($"Disposition-Notification-To: {opts.From.Email}");

        if (!string.IsNullOrWhiteSpace(opts.UnsubscribeUrl))
            sb.AppendLine($"List-Unsubscribe: <{opts.UnsubscribeUrl}>");

        foreach (var (k, v) in opts.CustomHeaders)
            sb.AppendLine($"{k}: {v}");

        bool hasText = !string.IsNullOrEmpty(opts.TextBody);
        bool hasHtml = !string.IsNullOrEmpty(opts.HtmlBody);
        bool hasAttachments = opts.Attachments.Count > 0;

        if (hasAttachments)
        {
            var mixedBoundary = $"=_mixed_{Guid.NewGuid():N}";
            sb.AppendLine($"Content-Type: multipart/mixed; boundary=\"{mixedBoundary}\"");
            sb.AppendLine();
            sb.AppendLine($"--{mixedBoundary}");

            if (hasText && hasHtml)
            {
                AppendAlternativePart(sb, opts.TextBody!, opts.HtmlBody!, mixedBoundary);
            }
            else if (hasHtml)
            {
                sb.AppendLine("Content-Type: text/html; charset=utf-8");
                sb.AppendLine("Content-Transfer-Encoding: quoted-printable");
                sb.AppendLine();
                sb.AppendLine(QuotedPrintable(opts.HtmlBody!));
            }
            else
            {
                sb.AppendLine("Content-Type: text/plain; charset=utf-8");
                sb.AppendLine("Content-Transfer-Encoding: quoted-printable");
                sb.AppendLine();
                sb.AppendLine(QuotedPrintable(opts.TextBody!));
            }

            foreach (var att in opts.Attachments)
            {
                sb.AppendLine($"--{mixedBoundary}");
                var disposition = string.IsNullOrWhiteSpace(att.ContentId) ? "attachment" : "inline";
                sb.AppendLine($"Content-Type: {att.ContentType}; name=\"{att.FileName}\"");
                sb.AppendLine("Content-Transfer-Encoding: base64");
                sb.AppendLine($"Content-Disposition: {disposition}; filename=\"{att.FileName}\"");
                if (!string.IsNullOrWhiteSpace(att.ContentId))
                    sb.AppendLine($"Content-ID: <{att.ContentId}>");
                sb.AppendLine();
                sb.AppendLine(WrapBase64(Convert.ToBase64String(att.Data)));
            }

            sb.AppendLine($"--{mixedBoundary}--");
        }
        else if (hasText && hasHtml)
        {
            AppendAlternativeHeaders(sb, out var altBoundary);
            sb.AppendLine();
            AppendTextAndHtmlParts(sb, opts.TextBody!, opts.HtmlBody!, altBoundary);
        }
        else if (hasHtml)
        {
            sb.AppendLine("Content-Type: text/html; charset=utf-8");
            sb.AppendLine("Content-Transfer-Encoding: quoted-printable");
            sb.AppendLine();
            sb.AppendLine(QuotedPrintable(opts.HtmlBody!));
        }
        else
        {
            sb.AppendLine("Content-Type: text/plain; charset=utf-8");
            sb.AppendLine("Content-Transfer-Encoding: quoted-printable");
            sb.AppendLine();
            sb.AppendLine(QuotedPrintable(opts.TextBody ?? string.Empty));
        }

        return sb.ToString();
    }

    private static void AppendAlternativePart(StringBuilder sb, string text, string html, string outerBoundary)
    {
        var altBoundary = $"=_alt_{Guid.NewGuid():N}";
        sb.AppendLine($"Content-Type: multipart/alternative; boundary=\"{altBoundary}\"");
        sb.AppendLine();
        AppendTextAndHtmlParts(sb, text, html, altBoundary);
        sb.AppendLine($"--{outerBoundary}");
    }

    private static void AppendAlternativeHeaders(StringBuilder sb, out string boundary)
    {
        boundary = $"=_alt_{Guid.NewGuid():N}";
        sb.AppendLine($"Content-Type: multipart/alternative; boundary=\"{boundary}\"");
    }

    private static void AppendTextAndHtmlParts(StringBuilder sb, string text, string html, string boundary)
    {
        sb.AppendLine($"--{boundary}");
        sb.AppendLine("Content-Type: text/plain; charset=utf-8");
        sb.AppendLine("Content-Transfer-Encoding: quoted-printable");
        sb.AppendLine();
        sb.AppendLine(QuotedPrintable(text));
        sb.AppendLine($"--{boundary}");
        sb.AppendLine("Content-Type: text/html; charset=utf-8");
        sb.AppendLine("Content-Transfer-Encoding: quoted-printable");
        sb.AppendLine();
        sb.AppendLine(QuotedPrintable(html));
        sb.AppendLine($"--{boundary}--");
    }

    private static string FormatAddress(EmailAddress addr)
    {
        if (string.IsNullOrWhiteSpace(addr.DisplayName)) return addr.Email;
        var name = NeedsEncoding(addr.DisplayName)
            ? EncodeHeader(addr.DisplayName)
            : $"\"{addr.DisplayName}\"";
        return $"{name} <{addr.Email}>";
    }

    private static bool NeedsEncoding(string value) =>
        value.Any(c => c > 127);

    private static string EncodeHeader(string value) =>
        NeedsEncoding(value)
            ? $"=?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}?="
            : value;

    private static string QuotedPrintable(string input)
    {
        var sb = new StringBuilder();
        foreach (var line in input.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            var encoded = new StringBuilder();
            foreach (var ch in trimmed)
            {
                if (ch > 126 || (ch < 32 && ch != '\t') || ch == '=')
                    encoded.Append($"={((int)ch):X2}");
                else
                    encoded.Append(ch);
            }
            sb.AppendLine(encoded.ToString());
        }
        return sb.ToString().TrimEnd();
    }

    private static string WrapBase64(string b64, int width = 76)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < b64.Length; i += width)
            sb.AppendLine(b64.AsSpan(i, Math.Min(width, b64.Length - i)).ToString());
        return sb.ToString().TrimEnd();
    }
}
