using FluentValidation;
using GroundUp.Auth.Core.Dtos;

namespace GroundUp.Auth.Core.Validators;

/// <summary>
/// Validates <see cref="InitiateAuthFlowRequest"/> input for flow initiation.
/// </summary>
public sealed class InitiateAuthFlowRequestValidator : AbstractValidator<InitiateAuthFlowRequest>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InitiateAuthFlowRequestValidator"/> class.
    /// </summary>
    public InitiateAuthFlowRequestValidator()
    {
        RuleFor(x => x.Nonce).NotEmpty().MaximumLength(128);
        RuleFor(x => x.FlowType).IsInEnum();
        RuleFor(x => x.Realm).MaximumLength(128).When(x => x.Realm is not null);
        RuleFor(x => x.ReturnUrl).MaximumLength(2048).When(x => x.ReturnUrl is not null);
        RuleFor(x => x.CreatedByIp).MaximumLength(64).When(x => x.CreatedByIp is not null);
        RuleFor(x => x.CreatedByUserAgent).MaximumLength(512).When(x => x.CreatedByUserAgent is not null);
        RuleFor(x => x.Lifetime)
            .Must(lt => lt is null || lt.Value > TimeSpan.Zero)
            .WithMessage("Lifetime must be positive if specified.");
    }
}
