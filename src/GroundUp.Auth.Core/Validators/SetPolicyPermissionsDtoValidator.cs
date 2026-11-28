using FluentValidation;
using GroundUp.Auth.Core.Dtos;

namespace GroundUp.Auth.Core.Validators;

/// <summary>
/// Validates <see cref="SetPolicyPermissionsDto"/> input (GU-96). An empty list is allowed: it
/// removes every permission from the policy.
/// </summary>
public sealed class SetPolicyPermissionsDtoValidator : AbstractValidator<SetPolicyPermissionsDto>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SetPolicyPermissionsDtoValidator"/> class.
    /// </summary>
    public SetPolicyPermissionsDtoValidator()
    {
        RuleFor(x => x.PermissionKeys).NotNull().WithMessage("permissionKeys is required (an empty list removes all permissions).");
        PermissionKeyListRules.Apply(RuleFor(x => x.PermissionKeys)!);
    }
}
