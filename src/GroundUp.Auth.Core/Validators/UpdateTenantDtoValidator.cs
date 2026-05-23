using FluentValidation;
using GroundUp.Auth.Core.Dtos;

namespace GroundUp.Auth.Core.Validators;

/// <summary>
/// Validates <see cref="UpdateTenantDto"/> input for tenant updates.
/// </summary>
public sealed class UpdateTenantDtoValidator : AbstractValidator<UpdateTenantDto>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateTenantDtoValidator"/> class.
    /// </summary>
    public UpdateTenantDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(100)
            .Matches(@"^[a-z0-9]+(?:-[a-z0-9]+)*$")
            .WithMessage("Slug must be lowercase alphanumeric with hyphens.");
        RuleFor(x => x.TenantType).IsInEnum();
        RuleFor(x => x.OnboardingMode).IsInEnum();
        RuleFor(x => x.RealmName).MaximumLength(200).When(x => x.RealmName is not null);
    }
}
