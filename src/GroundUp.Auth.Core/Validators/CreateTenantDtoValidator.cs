using FluentValidation;
using GroundUp.Auth.Core.Dtos;

namespace GroundUp.Auth.Core.Validators;

/// <summary>
/// Validates <see cref="CreateTenantDto"/> input for tenant creation.
/// </summary>
public sealed class CreateTenantDtoValidator : AbstractValidator<CreateTenantDto>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreateTenantDtoValidator"/> class.
    /// </summary>
    public CreateTenantDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(100)
            .Matches(@"^[a-z0-9]+(?:-[a-z0-9]+)*$")
            .WithMessage("Slug must be lowercase alphanumeric with hyphens.");
        RuleFor(x => x.TenantType).IsInEnum();
        RuleFor(x => x.OnboardingMode).IsInEnum();
        RuleFor(x => x.RealmName).MaximumLength(200).When(x => x.RealmName is not null);
        RuleFor(x => x.CustomDomain).MaximumLength(500).When(x => x.CustomDomain is not null);
    }
}
