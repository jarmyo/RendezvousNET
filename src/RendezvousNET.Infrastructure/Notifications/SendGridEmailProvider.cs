using Microsoft.Extensions.Logging;
using RendezvousNET.Shared.DTOs.Notifications;

namespace RendezvousNET.Infrastructure.Notifications;

/// <summary>
/// Stub SendGrid email provider.
/// Replace the implementation body with the actual SendGrid SDK calls.
/// </summary>
public sealed class SendGridEmailProvider : IEmailNotificationProvider
{
    private readonly ILogger<SendGridEmailProvider> _logger;
    private readonly string _apiKey;
    private readonly string _fromEmail;

    public SendGridEmailProvider(
        ILogger<SendGridEmailProvider> logger,
        string apiKey,
        string fromEmail)
    {
        _logger = logger;
        _apiKey = apiKey;
        _fromEmail = fromEmail;
    }

    public Task<bool> SendEmailAsync(
        SendNotificationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        // TODO: Replace with actual SendGrid SDK implementation:
        // var client = new SendGridClient(_apiKey);
        // var msg = MailHelper.CreateSingleEmail(
        //     new EmailAddress(_fromEmail), new EmailAddress(request.RecipientEmail),
        //     request.Subject, request.Body, request.Body);
        // var response = await client.SendEmailAsync(msg, cancellationToken);
        // return response.IsSuccessStatusCode;

        _logger.LogInformation(
            "[SendGrid STUB] Email would be sent to {To} with subject '{Subject}'.",
            request.RecipientEmail, request.Subject);

        return Task.FromResult(true);
    }
}
