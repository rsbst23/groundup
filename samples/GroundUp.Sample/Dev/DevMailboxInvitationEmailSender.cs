using GroundUp.Auth.Services.Invitations;

namespace GroundUp.Sample.Dev;

/// <summary>
/// Development only (GU-90): an <see cref="IInvitationEmailSender"/> that keeps invitation emails in
/// the <see cref="DevMailbox"/> instead of sending them, and logs them like the framework's default
/// sender. It replaces the default only in Development (<see cref="SampleDevToolsServiceCollectionExtensions"/>);
/// elsewhere the framework's logging sender stays until a real sender is registered.
/// </summary>
public sealed class DevMailboxInvitationEmailSender : IInvitationEmailSender
{
    private readonly DevMailbox _mailbox;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DevMailboxInvitationEmailSender> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="DevMailboxInvitationEmailSender"/>.
    /// </summary>
    public DevMailboxInvitationEmailSender(
        DevMailbox mailbox,
        TimeProvider timeProvider,
        ILogger<DevMailboxInvitationEmailSender> logger)
    {
        _mailbox = mailbox;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task SendAsync(InvitationEmail email, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);

        _mailbox.Add(new DevEmail(
            email.InvitationId,
            email.To,
            email.OrganizationName,
            email.RoleName,
            email.AcceptUrl,
            email.ExpiresAt,
            _timeProvider.GetUtcNow().UtcDateTime));

        _logger.LogInformation(
            "Invitation {InvitationId} for {Recipient} kept in the dev mailbox (not sent). Accept link: {AcceptUrl}",
            email.InvitationId, email.To, email.AcceptUrl);
        return Task.CompletedTask;
    }
}
