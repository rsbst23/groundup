using FluentValidation;
using GroundUp.Auth.Core.Dtos;

namespace GroundUp.Auth.Core.Validators;

/// <summary>
/// Validates <see cref="CreateRoleDto"/> input for role creation.
/// </summary>
public sealed class CreateRoleDtoValidator : AbstractValidator<CreateRoleDto>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreateRoleDtoValidator"/> class.
    /// </summary>
    public CreateRoleDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
        RuleFor(x => x.RoleType).IsInEnum();
    }
}
