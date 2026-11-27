using FluentValidation;
using GroundUp.Auth.Core.Dtos;

namespace GroundUp.Auth.Core.Validators;

/// <summary>
/// Validates <see cref="CreateJoinLinkDto"/>: a role, an expiry in the future when given, and at
/// least one use when a maximum is given.
/// </summary>
public sealed class CreateJoinLinkDtoValidator : AbstractValidator<CreateJoinLinkDto>
{
    /// <summary>The largest allowed maximum number of uses.</summary>
    public const int MaxUsesLimit = 1_000_000;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateJoinLinkDtoValidator"/> class.
    /// </summary>
    public CreateJoinLinkDtoValidator()
    {
        RuleFor(x => x.RoleId)
            .NotEmpty()
            .WithMessage("A role is required.");

        RuleFor(x => x.ExpiresAt)
            .Must(expiresAt => expiresAt is null || expiresAt.Value.ToUniversalTime() > DateTime.UtcNow)
            .WithMessage("The expiry must be in the future.");

        RuleFor(x => x.MaxUses)
            .InclusiveBetween(1, MaxUsesLimit)
            .When(x => x.MaxUses is not null)
            .WithMessage($"The maximum number of uses must be between 1 and {MaxUsesLimit}.");
    }
}
