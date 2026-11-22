using FluentValidation;
using GroundUp.Auth.Core.Dtos;

namespace GroundUp.Auth.Core.Validators;

/// <summary>
/// Validates <see cref="CreateOrganizationRequestDto"/>. The name is checked after trimming:
/// required, at most 200 characters.
/// </summary>
public sealed class CreateOrganizationRequestDtoValidator : AbstractValidator<CreateOrganizationRequestDto>
{
    /// <summary>Maximum length of an organization name.</summary>
    public const int MaxNameLength = 200;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateOrganizationRequestDtoValidator"/> class.
    /// </summary>
    public CreateOrganizationRequestDtoValidator()
    {
        RuleFor(x => x.OrganizationName)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Organization name is required.")
            .DependentRules(() =>
                RuleFor(x => x.OrganizationName!.Trim())
                    .MaximumLength(MaxNameLength)
                    .WithMessage($"Organization name must not exceed {MaxNameLength} characters.")
                    .OverridePropertyName(nameof(CreateOrganizationRequestDto.OrganizationName)));
    }
}
