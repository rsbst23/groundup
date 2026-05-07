using System.Collections;
using System.Reflection;
using FluentAssertions;
using GroundUp.Auth.Core.Entities;
using GroundUp.Core.Entities;

namespace GroundUp.Tests.Unit.Authentication;

/// <summary>
/// Reflection-based tests verifying the structural correctness of all auth entities.
/// </summary>
public sealed class AuthEntityStructureTests
{
    private static readonly Type[] AllEntityTypes =
    [
        typeof(User),
        typeof(Tenant),
        typeof(UserTenant),
        typeof(Role),
        typeof(Policy),
        typeof(Permission),
        typeof(RolePolicy),
        typeof(PolicyPermission),
        typeof(UserRole)
    ];

    [Theory]
    [MemberData(nameof(AllEntities))]
    public void Entity_IsSealed(Type entityType)
    {
        entityType.IsSealed.Should().BeTrue($"{entityType.Name} should be sealed");
    }

    [Theory]
    [MemberData(nameof(AllEntities))]
    public void Entity_ExtendsBaseEntity(Type entityType)
    {
        entityType.Should().BeDerivedFrom<BaseEntity>($"{entityType.Name} should extend BaseEntity");
    }

    [Theory]
    [InlineData(typeof(User))]
    [InlineData(typeof(Tenant))]
    [InlineData(typeof(UserTenant))]
    [InlineData(typeof(Role))]
    [InlineData(typeof(Policy))]
    [InlineData(typeof(RolePolicy))]
    [InlineData(typeof(PolicyPermission))]
    [InlineData(typeof(UserRole))]
    public void Entity_ImplementsIAuditable(Type entityType)
    {
        entityType.Should().Implement<IAuditable>($"{entityType.Name} should implement IAuditable");
    }

    [Fact]
    public void Permission_DoesNotImplementIAuditable()
    {
        typeof(Permission).Should().NotImplement<IAuditable>("Permission is a code-seeded definition and should not be auditable");
    }

    [Fact]
    public void OnlyTenant_ImplementsISoftDeletable()
    {
        typeof(Tenant).Should().Implement<ISoftDeletable>("Tenant should implement ISoftDeletable");

        var nonSoftDeletable = AllEntityTypes.Where(t => t != typeof(Tenant));
        foreach (var type in nonSoftDeletable)
        {
            type.Should().NotImplement<ISoftDeletable>($"{type.Name} should NOT implement ISoftDeletable");
        }
    }

    [Theory]
    [InlineData(typeof(Role))]
    [InlineData(typeof(Policy))]
    [InlineData(typeof(UserRole))]
    public void Entity_ImplementsITenantEntity(Type entityType)
    {
        entityType.Should().Implement<ITenantEntity>($"{entityType.Name} should implement ITenantEntity");
    }

    [Theory]
    [InlineData(typeof(User))]
    [InlineData(typeof(Tenant))]
    [InlineData(typeof(UserTenant))]
    [InlineData(typeof(Permission))]
    [InlineData(typeof(RolePolicy))]
    [InlineData(typeof(PolicyPermission))]
    public void Entity_DoesNotImplementITenantEntity(Type entityType)
    {
        entityType.Should().NotImplement<ITenantEntity>($"{entityType.Name} should NOT implement ITenantEntity");
    }

    [Fact]
    public void User_NavigationCollections_AreInitialized()
    {
        var user = new User();
        user.UserTenants.Should().NotBeNull().And.BeEmpty();
        user.UserRoles.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Tenant_NavigationCollections_AreInitialized()
    {
        var tenant = new Tenant();
        tenant.Children.Should().NotBeNull().And.BeEmpty();
        tenant.UserTenants.Should().NotBeNull().And.BeEmpty();
        tenant.Roles.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Role_NavigationCollections_AreInitialized()
    {
        var role = new Role();
        role.RolePolicies.Should().NotBeNull().And.BeEmpty();
        role.UserRoles.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Policy_NavigationCollections_AreInitialized()
    {
        var policy = new Policy();
        policy.RolePolicies.Should().NotBeNull().And.BeEmpty();
        policy.PolicyPermissions.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Permission_NavigationCollections_AreInitialized()
    {
        var permission = new Permission();
        permission.PolicyPermissions.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void User_IsActive_DefaultsToTrue()
    {
        var user = new User();
        user.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Tenant_IsActive_DefaultsToTrue()
    {
        var tenant = new Tenant();
        tenant.IsActive.Should().BeTrue();
    }

    [Fact]
    public void UserTenant_IsActive_DefaultsToTrue()
    {
        var userTenant = new UserTenant();
        userTenant.IsActive.Should().BeTrue();
    }

    public static IEnumerable<object[]> AllEntities()
    {
        return AllEntityTypes.Select(t => new object[] { t });
    }
}
