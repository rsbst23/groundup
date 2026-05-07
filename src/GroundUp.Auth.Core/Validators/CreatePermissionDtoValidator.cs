using FluentValidation;
using GroundUp.Auth.Core.Dtos;

namespace GroundUp.Auth.Core.Validators;

/// <summary>
/// Validates <see cref="CreatePermissionDto"/> input for permission creation.
/// </summary>
public sealed class CreatePermissionDtoValidator : AbstractValidator<CreatePermissionDto>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreatePermissionDtoValidator"/> class.
    /// </summary>
    public CreatePermissionDtoValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(200)
            .Matches(@"^[a-z][a-z0-9]*(?:\.[a-z][a-z0-9]*)*$")
            .WithMessage("Permission key must be dot-separated lowercase segments (e.g., 'settings.read').");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
        RuleFor(x => x.Module).NotEmpty().MaximumLength(100);
    }
}
