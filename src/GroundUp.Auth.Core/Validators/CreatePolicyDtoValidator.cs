using FluentValidation;
using GroundUp.Auth.Core.Dtos;

namespace GroundUp.Auth.Core.Validators;

/// <summary>
/// Validates <see cref="CreatePolicyDto"/> input for policy creation.
/// </summary>
public sealed class CreatePolicyDtoValidator : AbstractValidator<CreatePolicyDto>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreatePolicyDtoValidator"/> class.
    /// </summary>
    public CreatePolicyDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
    }
}
