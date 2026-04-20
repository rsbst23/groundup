using FluentValidation;

namespace GroundUp.Tests.Unit.Services.TestHelpers;

public class ServiceTestDtoValidator : AbstractValidator<ServiceTestDto>
{
    public ServiceTestDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
    }
}
