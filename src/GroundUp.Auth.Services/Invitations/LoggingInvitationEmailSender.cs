using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.Invitations;

/// <summary>
/// Default <see cref="IInvitationEmailSender"/>: logs instead of sending. The accept link is logged
/// only in the Development environment, so a developer can follow it; elsewhere only the recipient
/// and invitation ID are logged, because the link is a credential.
/// </summary>
public sealed class LoggingInvitationEmailSender : IInvitationEmailSender
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<LoggingInvitationEmailSender> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="LoggingInvitationEmailSender"/>.
    /// </summary>
    public LoggingInvitationEmailSender(IHostEnvironment environment, ILogger<LoggingInvitationEmailSender> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task SendAsync(InvitationEmail email, CancellationToken cancellationToken = default)
    {
        if (_environment.IsDevelopment())
        {
            _logger.LogInformation(
                "Invitation {InvitationId} for {Recipient} (not sent: no email sender registered). Accept link: {AcceptUrl}",
                email.InvitationId, email.To, email.AcceptUrl);
        }
        else
        {
            _logger.LogWarning(
                "Invitation {InvitationId} for {Recipient} was not emailed: no IInvitationEmailSender is registered",
                email.InvitationId, email.To);
        }

        return Task.CompletedTask;
    }
}
