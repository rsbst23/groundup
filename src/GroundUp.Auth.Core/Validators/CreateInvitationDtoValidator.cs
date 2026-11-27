using FluentValidation;
using GroundUp.Auth.Core.Dtos;

namespace GroundUp.Auth.Core.Validators;

/// <summary>
/// Validates <see cref="CreateInvitationDto"/>: a required, well-formed email of at most 320
/// characters (checked after trimming), a role, and an expiry in the future when one is given.
/// The upper limit on the expiry comes from settings and is applied by the service.
/// </summary>
public sealed class CreateInvitationDtoValidator : AbstractValidator<CreateInvitationDto>
{
    /// <summary>Maximum length of an email address (RFC 5321).</summary>
    public const int MaxEmailLength = 320;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateInvitationDtoValidator"/> class.
    /// </summary>
    public CreateInvitationDtoValidator()
    {
        RuleFor(x => x.Email)
            .Must(email => !string.IsNullOrWhiteSpace(email))
            .WithMessage("Email is required.")
            .DependentRules(() =>
                RuleFor(x => x.Email!.Trim())
                    .MaximumLength(MaxEmailLength)
                    .WithMessage($"Email must not exceed {MaxEmailLength} characters.")
                    .EmailAddress()
                    .WithMessage("Email is not a valid email address.")
                    .OverridePropertyName(nameof(CreateInvitationDto.Email)));

        RuleFor(x => x.RoleId)
            .NotEmpty()
            .WithMessage("A role is required.");

        RuleFor(x => x.ExpiresAt)
            .Must(expiresAt => expiresAt is null || expiresAt.Value.ToUniversalTime() > DateTime.UtcNow)
            .WithMessage("The expiry must be in the future.");
    }
}
