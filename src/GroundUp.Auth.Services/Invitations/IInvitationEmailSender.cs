namespace GroundUp.Auth.Services.Invitations;

/// <summary>
/// Delivers invitation emails. The default <see cref="LoggingInvitationEmailSender"/> only logs;
/// applications register their own implementation (Notifications, GU-64, will provide one).
/// <para>
/// <see cref="InvitationEmail.AcceptUrl"/> contains the invitation token, which is a credential:
/// implementations must not log it outside development.
/// </para>
/// </summary>
public interface IInvitationEmailSender
{
    /// <summary>Sends the invitation email.</summary>
    Task SendAsync(InvitationEmail email, CancellationToken cancellationToken = default);
}

/// <summary>
/// The content of an invitation email.
/// </summary>
/// <param name="To">The invited email address.</param>
/// <param name="OrganizationName">The inviting organization's name.</param>
/// <param name="RoleName">The role being offered.</param>
/// <param name="AcceptUrl">The link to the accept page, containing the token.</param>
/// <param name="ExpiresAt">When the invitation stops working (UTC).</param>
/// <param name="InvitationId">The invitation identifier (safe to log).</param>
public sealed record InvitationEmail(
    string To,
    string OrganizationName,
    string RoleName,
    string AcceptUrl,
    DateTime ExpiresAt,
    Guid InvitationId);
