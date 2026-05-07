using FsCheck;
using FsCheck.Xunit;
using GroundUp.Core.Attributes;

namespace GroundUp.Tests.Unit.Authentication;

/// <summary>
/// Property-based tests for security attributes.
/// Feature: phase-9a-auth-entities, Property 1: Security attribute constructor round-trip
/// Validates: Requirements 14.2, 14.3, 15.2, 15.3
/// </summary>
public sealed class SecurityAttributePropertyTests
{
    /// <summary>
    /// Property 1: Security attribute constructor round-trip.
    /// For any array of non-null strings passed to RequiresPermissionAttribute,
    /// the Permissions list contains exactly the same strings in the same order.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RequiresPermissionAttribute_RoundTrips_AllStrings(NonNull<string>[] inputs)
    {
        var strings = inputs.Select(x => x.Get).ToArray();
        var attr = new RequiresPermissionAttribute(strings);

        return (attr.Permissions.Count == strings.Length
            && attr.Permissions.SequenceEqual(strings))
            .ToProperty();
    }

    /// <summary>
    /// Property 1: Security attribute constructor round-trip.
    /// For any array of non-null strings passed to RequiresRoleAttribute,
    /// the Roles list contains exactly the same strings in the same order.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RequiresRoleAttribute_RoundTrips_AllStrings(NonNull<string>[] inputs)
    {
        var strings = inputs.Select(x => x.Get).ToArray();
        var attr = new RequiresRoleAttribute(strings);

        return (attr.Roles.Count == strings.Length
            && attr.Roles.SequenceEqual(strings))
            .ToProperty();
    }

    /// <summary>
    /// Property 1: Count equals input array length for RequiresPermissionAttribute.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RequiresPermissionAttribute_Count_EqualsInputLength(NonNull<string>[] inputs)
    {
        var strings = inputs.Select(x => x.Get).ToArray();
        var attr = new RequiresPermissionAttribute(strings);

        return (attr.Permissions.Count == strings.Length).ToProperty();
    }

    /// <summary>
    /// Property 1: Count equals input array length for RequiresRoleAttribute.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RequiresRoleAttribute_Count_EqualsInputLength(NonNull<string>[] inputs)
    {
        var strings = inputs.Select(x => x.Get).ToArray();
        var attr = new RequiresRoleAttribute(strings);

        return (attr.Roles.Count == strings.Length).ToProperty();
    }
}
