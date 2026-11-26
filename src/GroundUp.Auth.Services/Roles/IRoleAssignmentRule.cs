using GroundUp.Core.Results;

namespace GroundUp.Auth.Services.Roles;

/// <summary>
/// An application-defined rule on who may grant or remove a role, for example "no self-assignment".
/// Register implementations in DI (<c>services.AddScoped&lt;IRoleAssignmentRule, MyRule&gt;()</c>);
/// none are registered by default.
/// <para>
/// Every registered rule runs, in registration order, after the built-in rules (SuperAdmin only by a
/// SuperAdmin, TenantAdmin only by a TenantAdmin or SuperAdmin, other roles by holders of
/// <c>auth.roles.assign</c>). The first failure refuses the change and its message is returned to
/// the caller. Rules run for every change a person makes (admin changes, invitation and join-link
/// creation, and the re-check when one is accepted), not for system grants such as organization
/// provisioning or the default role.
/// </para>
/// </summary>
public interface IRoleAssignmentRule
{
    /// <summary>
    /// Returns success to allow the change, or a failure (usually 403) to refuse it.
    /// </summary>
    /// <param name="context">The change being checked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OperationResult> EvaluateAsync(RoleAssignmentContext context, CancellationToken cancellationToken = default);
}
