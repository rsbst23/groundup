using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Core.Validators;

namespace GroundUp.Tests.Unit.Authentication;

/// <summary>
/// Property-based tests for Name/Description validation across role and policy validators.
/// Feature: phase-9a-auth-entities, Property 3: Name/Description validation
/// Validates: Requirements 18.3, 18.4, 18.5
/// </summary>
public sealed class NameDescriptionValidatorPropertyTests
{
    private static readonly CreateRoleDtoValidator CreateRoleValidator = new();
    private static readonly UpdateRoleDtoValidator UpdateRoleValidator = new();
    private static readonly CreatePolicyDtoValidator CreatePolicyValidator = new();

    /// <summary>
    /// Property 3: Valid Name (1–200 chars) and Description (≤1000 or null) produce zero errors
    /// on CreateRoleDtoValidator.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ValidNameDescArbitrary) })]
    public Property CreateRoleDto_ValidNameAndDescription_ProducesNoErrors(ValidNameDesc input)
    {
        var dto = new CreateRoleDto(
            Name: input.Name,
            Description: input.Description,
            RoleType: RoleType.Application);

        var result = CreateRoleValidator.Validate(dto);

        return result.IsValid.ToProperty()
            .Label($"Name length={input.Name.Length}, Desc length={input.Description?.Length ?? 0}");
    }

    /// <summary>
    /// Property 3: Valid Name and Description produce zero errors on UpdateRoleDtoValidator.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ValidNameDescArbitrary) })]
    public Property UpdateRoleDto_ValidNameAndDescription_ProducesNoErrors(ValidNameDesc input)
    {
        var dto = new UpdateRoleDto(
            Name: input.Name,
            Description: input.Description);

        var result = UpdateRoleValidator.Validate(dto);

        return result.IsValid.ToProperty()
            .Label($"Name length={input.Name.Length}, Desc length={input.Description?.Length ?? 0}");
    }

    /// <summary>
    /// Property 3: Valid Name and Description produce zero errors on CreatePolicyDtoValidator.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ValidNameDescArbitrary) })]
    public Property CreatePolicyDto_ValidNameAndDescription_ProducesNoErrors(ValidNameDesc input)
    {
        var dto = new CreatePolicyDto(
            Name: input.Name,
            Description: input.Description);

        var result = CreatePolicyValidator.Validate(dto);

        return result.IsValid.ToProperty()
            .Label($"Name length={input.Name.Length}, Desc length={input.Description?.Length ?? 0}");
    }

    /// <summary>
    /// Property 3: Empty Name produces at least one error on CreateRoleDtoValidator.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CreateRoleDto_EmptyName_ProducesError(bool useNull)
    {
        var name = useNull ? "" : "   ";
        var dto = new CreateRoleDto(
            Name: name,
            Description: null,
            RoleType: RoleType.Application);

        var result = CreateRoleValidator.Validate(dto);
        var nameErrors = result.Errors.Where(e => e.PropertyName == "Name").ToList();

        return (nameErrors.Count > 0).ToProperty();
    }

    /// <summary>
    /// Property 3: Name exceeding 200 chars produces at least one error.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OverLengthNameArbitrary) })]
    public Property CreateRoleDto_NameTooLong_ProducesError(OverLengthName input)
    {
        var dto = new CreateRoleDto(
            Name: input.Value,
            Description: null,
            RoleType: RoleType.Application);

        var result = CreateRoleValidator.Validate(dto);
        var nameErrors = result.Errors.Where(e => e.PropertyName == "Name").ToList();

        return (nameErrors.Count > 0).ToProperty()
            .Label($"Name length={input.Value.Length} should be rejected");
    }

    /// <summary>
    /// Property 3: Description exceeding 1000 chars produces at least one error.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OverLengthDescArbitrary) })]
    public Property CreateRoleDto_DescriptionTooLong_ProducesError(OverLengthDesc input)
    {
        var dto = new CreateRoleDto(
            Name: "ValidName",
            Description: input.Value,
            RoleType: RoleType.Application);

        var result = CreateRoleValidator.Validate(dto);
        var descErrors = result.Errors.Where(e => e.PropertyName == "Description").ToList();

        return (descErrors.Count > 0).ToProperty()
            .Label($"Description length={input.Value.Length} should be rejected");
    }

    // --- Custom types and generators ---

    public record ValidNameDesc(string Name, string? Description);
    public record OverLengthName(string Value);
    public record OverLengthDesc(string Value);

    public static class ValidNameDescArbitrary
    {
        public static Arbitrary<ValidNameDesc> ValidNameDescArb()
        {
            var nameGen = Gen.Choose(1, 200)
                .SelectMany(len =>
                    Gen.ArrayOf(len, Gen.Elements("abcdefghijklmnopqrstuvwxyz ".ToCharArray()))
                        .Select(chars => new string(chars).TrimEnd())
                        .Where(s => s.Length >= 1 && s.Length <= 200));

            var descGen = Gen.OneOf(
                Gen.Constant<string?>(null),
                Gen.Choose(1, 1000)
                    .SelectMany(len =>
                        Gen.ArrayOf(len, Gen.Elements("abcdefghijklmnopqrstuvwxyz0123456789 ".ToCharArray()))
                            .Select(chars => (string?)new string(chars))));

            return Arb.From(
                nameGen.SelectMany(n => descGen.Select(d => new ValidNameDesc(n, d))));
        }
    }

    public static class OverLengthNameArbitrary
    {
        public static Arbitrary<OverLengthName> OverLengthNameArb()
        {
            var gen = Gen.Choose(201, 500)
                .Select(len => new OverLengthName(new string('a', len)));
            return Arb.From(gen);
        }
    }

    public static class OverLengthDescArbitrary
    {
        public static Arbitrary<OverLengthDesc> OverLengthDescArb()
        {
            var gen = Gen.Choose(1001, 2000)
                .Select(len => new OverLengthDesc(new string('a', len)));
            return Arb.From(gen);
        }
    }
}
