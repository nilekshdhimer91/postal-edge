using DNA.Email.Core.Models.Requests;
using DNA.Email.Core.Models.Responses;

namespace DNA.Email.Core.Interfaces;

public interface IEmailService
{
    Task<EmailSendResult> SendTextAsync(SendTextEmailRequest req, CancellationToken ct = default);
    Task<EmailSendResult> SendTextHtmlAsync(SendTextHtmlEmailRequest req, CancellationToken ct = default);
    Task<EmailSendResult> SendHtmlAsync(SendHtmlEmailRequest req, CancellationToken ct = default);

    Task<EmailSendResult> SendHtmlWithAttachmentsAsync(
        SendHtmlAttachmentsEmailRequest req,
        IReadOnlyList<(string FileName, string ContentType, byte[] Data)>? multipartFiles,
        CancellationToken ct = default);

    Task<EmailSendResult> SendAsync(
        SendEmailRequest req,
        IReadOnlyList<(string FileName, string ContentType, byte[] Data)>? multipartFiles,
        CancellationToken ct = default);
}
