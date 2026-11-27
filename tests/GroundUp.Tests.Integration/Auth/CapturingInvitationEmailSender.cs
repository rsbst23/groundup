using System.Collections.Concurrent;
using GroundUp.Auth.Services.Invitations;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Records invitation emails instead of sending them, so tests can follow the link.
/// </summary>
public sealed class CapturingInvitationEmailSender : IInvitationEmailSender
{
    private readonly ConcurrentQueue<InvitationEmail> _sent = new();

    /// <summary>Every email sent, oldest first.</summary>
    public IReadOnlyList<InvitationEmail> Sent => _sent.ToList();

    /// <inheritdoc />
    public Task SendAsync(InvitationEmail email, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(email);
        return Task.CompletedTask;
    }

    /// <summary>The latest email for <paramref name="invitationId"/>.</summary>
    public InvitationEmail LatestFor(Guid invitationId) => Sent.Last(e => e.InvitationId == invitationId);

    /// <summary>The token in the latest link for <paramref name="invitationId"/>.</summary>
    public string TokenFor(Guid invitationId)
    {
        var url = new Uri(LatestFor(invitationId).AcceptUrl);
        return Uri.UnescapeDataString(url.AbsolutePath["/invite/".Length..]);
    }
}
