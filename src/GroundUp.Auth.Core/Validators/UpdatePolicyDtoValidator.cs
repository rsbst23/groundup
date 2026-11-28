using FluentValidation;
using GroundUp.Auth.Core.Dtos;

namespace GroundUp.Auth.Core.Validators;

/// <summary>
/// Validates <see cref="UpdatePolicyDto"/> input for renaming a policy (GU-96).
/// </summary>
public sealed class UpdatePolicyDtoValidator : AbstractValidator<UpdatePolicyDto>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdatePolicyDtoValidator"/> class.
    /// </summary>
    public UpdatePolicyDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
    }
}
