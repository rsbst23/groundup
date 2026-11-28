namespace GroundUp.Sample.Dev;

/// <summary>
/// One invitation email kept by the <see cref="DevMailbox"/>.
/// </summary>
/// <param name="InvitationId">The invitation.</param>
/// <param name="To">The invited address.</param>
/// <param name="OrganizationName">The organization's name.</param>
/// <param name="RoleName">The role the invitation grants.</param>
/// <param name="AcceptUrl">The link in the email. It contains the invitation token, which works like a password.</param>
/// <param name="ExpiresAt">When the invitation expires (UTC).</param>
/// <param name="SentAt">When the email was "sent" (UTC).</param>
public sealed record DevEmail(
    Guid InvitationId,
    string To,
    string OrganizationName,
    string RoleName,
    string AcceptUrl,
    DateTime ExpiresAt,
    DateTime SentAt);

/// <summary>
/// Development only (GU-90): the last <see cref="Capacity"/> invitation emails, kept in memory
/// instead of being sent, so a tester can open the links from the web app's Mailbox page. Lost on
/// restart. Registered as a singleton by <see cref="SampleDevToolsServiceCollectionExtensions"/>.
/// </summary>
public sealed class DevMailbox
{
    /// <summary>How many emails are kept; older ones are dropped.</summary>
    public const int Capacity = 50;

    private readonly object _gate = new();
    private readonly LinkedList<DevEmail> _emails = new();

    /// <summary>Keeps an email, dropping the oldest beyond <see cref="Capacity"/>.</summary>
    public void Add(DevEmail email)
    {
        ArgumentNullException.ThrowIfNull(email);
        lock (_gate)
        {
            _emails.AddFirst(email);
            while (_emails.Count > Capacity)
            {
                _emails.RemoveLast();
            }
        }
    }

    /// <summary>The kept emails, newest first.</summary>
    public IReadOnlyList<DevEmail> List()
    {
        lock (_gate)
        {
            return _emails.ToList();
        }
    }
}
