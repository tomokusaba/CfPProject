using Azure;
using Azure.Communication.Email;
using Azure.Identity;
using Cfp.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Cfp.Infrastructure.Email;

public sealed class AzureCommunicationEmailSender(IConfiguration configuration) : IEmailSender
{
    private readonly Lazy<EmailClient> _client = new(() =>
    {
        var endpoint = configuration["Communication:Endpoint"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) ||
            endpointUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new EmailSendFailureException(
                "ACS Email endpoint is not configured.",
                outcomeUnknown: false,
                errorCode: "EmailEndpointMissing");
        }

        return new EmailClient(endpointUri, new DefaultAzureCredential());
    });

    public async Task<string> SendAsync(
        EmailMessageEnvelope message,
        CancellationToken cancellationToken)
    {
        var sender = string.IsNullOrWhiteSpace(message.SenderAddress)
            ? configuration["Communication:SenderAddress"]
            : message.SenderAddress;
        if (string.IsNullOrWhiteSpace(sender))
        {
            throw new EmailSendFailureException(
                "ACS Email sender address is not configured.",
                outcomeUnknown: false,
                errorCode: "EmailSenderMissing");
        }

        try
        {
            var operation = await _client.Value.SendAsync(
                WaitUntil.Started,
                sender,
                message.RecipientAddress,
                message.Subject,
                message.PlainTextContent,
                cancellationToken: cancellationToken);
            if (string.IsNullOrWhiteSpace(operation.Id))
            {
                throw new EmailSendFailureException(
                    "ACS Email did not return a provider message ID.",
                    outcomeUnknown: true,
                    errorCode: "EmailProviderMessageIdMissing");
            }

            return operation.Id;
        }
        catch (EmailSendFailureException)
        {
            throw;
        }
        catch (RequestFailedException exception)
        {
            throw new EmailSendFailureException(
                "ACS Email request failed.",
                outcomeUnknown: exception.Status >= 500 || exception.Status == 408,
                errorCode: exception.ErrorCode ?? $"HttpStatus{exception.Status}");
        }
    }
}
