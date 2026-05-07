using FluentValidation;
using GroundUp.Auth.Core.Dtos;

namespace GroundUp.Auth.Core.Validators;

/// <summary>
/// Validates <see cref="UpdateRoleDto"/> input for role updates.
/// </summary>
public sealed class UpdateRoleDtoValidator : AbstractValidator<UpdateRoleDto>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateRoleDtoValidator"/> class.
    /// </summary>
    public UpdateRoleDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
    }
}
