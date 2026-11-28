using System.Reflection;
using GroundUp.Core;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Attributes;
using GroundUp.Core.Results;

namespace GroundUp.Auth.Services.Authorization;

/// <summary>
/// DispatchProxy-based interceptor that enforces <see cref="RequiresPermissionAttribute"/> and
/// <see cref="RequiresRoleAttribute"/> attributes on service interface methods.
/// <para>
/// Attributed methods must return <c>Task&lt;OperationResult&gt;</c> or <c>Task&lt;OperationResult&lt;T&gt;&gt;</c>
/// (checked when the proxy is registered or created). A refusal is a 403 result:
/// <list type="bullet">
/// <item><c>PERMISSION_REQUIRED</c> with the permission keys the caller lacks in <c>errors</c>.</item>
/// <item><c>ROLE_REQUIRED</c> with the listed roles in <c>errors</c> (any one is enough).</item>
/// <item><c>FORBIDDEN</c> for an unauthenticated caller.</item>
/// </list>
/// </para>
/// </summary>
/// <typeparam name="TInterface">The service interface type being proxied.</typeparam>
public class AuthorizationInterceptor<TInterface> : DispatchProxy
    where TInterface : class
{
    private TInterface _target = null!;
    private IPermissionService _permissionService = null!;
    private ICurrentUser _currentUser = null!;

    /// <summary>Why a call was refused: the error code, the message and the missing keys or roles.</summary>
    private sealed record Refusal(string Message, string ErrorCode, List<string>? Errors);

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
        {
            throw new InvalidOperationException("Target method cannot be null.");
        }

        // Read attributes from the INTERFACE method, not the implementation
        var interfaceMethod = GetInterfaceMethod(targetMethod);
        var requiresPermission = interfaceMethod?.GetCustomAttribute<RequiresPermissionAttribute>();
        var requiresRole = interfaceMethod?.GetCustomAttribute<RequiresRoleAttribute>();

        // If neither attribute is present, pass through
        if (requiresPermission is null && requiresRole is null)
        {
            return targetMethod.Invoke(_target, args);
        }

        if (!TryGetOperationResultInfo(targetMethod.ReturnType, out var isGenericResult, out var innerType))
        {
            // Cannot happen: Create and AddAuthorized refuse such interfaces
            throw new InvalidOperationException(ReturnTypeMessage(targetMethod));
        }

        if (isGenericResult && innerType is not null)
        {
            var helperMethod = typeof(AuthorizationInterceptor<TInterface>)
                .GetMethod(nameof(ExecuteGenericAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(innerType);

            return helperMethod.Invoke(this, [targetMethod, args, requiresPermission, requiresRole])!;
        }

        return ExecuteNonGenericAsync(targetMethod, args, requiresPermission, requiresRole);
    }

    /// <summary>
    /// Creates a proxy wrapping the target implementation with authorization enforcement.
    /// </summary>
    /// <param name="target">The real service implementation.</param>
    /// <param name="permissionService">The permission service for authorization checks.</param>
    /// <param name="currentUser">The current user context.</param>
    /// <returns>A proxy instance implementing <typeparamref name="TInterface"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// An attributed method of <typeparamref name="TInterface"/> does not return an <c>OperationResult</c>.
    /// </exception>
    public static TInterface Create(TInterface target, IPermissionService permissionService, ICurrentUser currentUser)
    {
        EnsureAttributedMethodsReturnOperationResult();

        var proxy = Create<TInterface, AuthorizationInterceptor<TInterface>>();
        var interceptor = (AuthorizationInterceptor<TInterface>)(object)proxy;
        interceptor._target = target;
        interceptor._permissionService = permissionService;
        interceptor._currentUser = currentUser;
        return proxy;
    }

    /// <summary>
    /// Throws when a method of <typeparamref name="TInterface"/> has <see cref="RequiresPermissionAttribute"/>
    /// or <see cref="RequiresRoleAttribute"/> but does not return <c>Task&lt;OperationResult&gt;</c> or
    /// <c>Task&lt;OperationResult&lt;T&gt;&gt;</c>: a refusal is reported as a result, never as an exception.
    /// </summary>
    /// <exception cref="InvalidOperationException">Such a method exists; the message names it.</exception>
    public static void EnsureAttributedMethodsReturnOperationResult()
    {
        foreach (var method in typeof(TInterface).GetMethods())
        {
            var attributed = method.IsDefined(typeof(RequiresPermissionAttribute), inherit: false)
                || method.IsDefined(typeof(RequiresRoleAttribute), inherit: false);
            if (attributed && !TryGetOperationResultInfo(method.ReturnType, out _, out _))
            {
                throw new InvalidOperationException(ReturnTypeMessage(method));
            }
        }
    }

    private static string ReturnTypeMessage(MethodInfo method)
        => $"{typeof(TInterface).Name}.{method.Name} has [RequiresPermission] or [RequiresRole] but returns " +
           $"{method.ReturnType.Name}; it must return Task<OperationResult> or Task<OperationResult<T>>.";

    private async Task<OperationResult<T>> ExecuteGenericAsync<T>(
        MethodInfo targetMethod,
        object?[]? args,
        RequiresPermissionAttribute? requiresPermission,
        RequiresRoleAttribute? requiresRole)
    {
        var refusal = await CheckAsync(requiresPermission, requiresRole);
        if (refusal is not null)
        {
            return OperationResult<T>.Fail(refusal.Message, 403, refusal.ErrorCode, refusal.Errors);
        }

        var result = targetMethod.Invoke(_target, args);
        if (result is Task<OperationResult<T>> typedTask)
        {
            return await typedTask.ConfigureAwait(false);
        }

        throw new InvalidOperationException($"Expected Task<OperationResult<{typeof(T).Name}>> but got {result?.GetType().Name ?? "null"}.");
    }

    private async Task<OperationResult> ExecuteNonGenericAsync(
        MethodInfo targetMethod,
        object?[]? args,
        RequiresPermissionAttribute? requiresPermission,
        RequiresRoleAttribute? requiresRole)
    {
        var refusal = await CheckAsync(requiresPermission, requiresRole);
        if (refusal is not null)
        {
            return OperationResult.Fail(refusal.Message, 403, refusal.ErrorCode, refusal.Errors);
        }

        var result = targetMethod.Invoke(_target, args);
        if (result is Task<OperationResult> typedTask)
        {
            return await typedTask.ConfigureAwait(false);
        }

        throw new InvalidOperationException($"Expected Task<OperationResult> but got {result?.GetType().Name ?? "null"}.");
    }

    /// <summary>
    /// The authorization checks, in order: authenticated, then <c>[RequiresRole]</c> (any of the roles,
    /// system roles only), then <c>[RequiresPermission]</c> (all of the keys). Null when allowed.
    /// Every key is checked so the refusal names all the missing ones. <see cref="IPermissionService.HasPermissionAsync"/>
    /// applies the SuperAdmin and TenantAdmin bypass, the same as everywhere else in the framework.
    /// </summary>
    private async Task<Refusal?> CheckAsync(RequiresPermissionAttribute? requiresPermission, RequiresRoleAttribute? requiresRole)
    {
        var userId = _currentUser.UserId;
        if (userId == Guid.Empty)
        {
            return new Refusal("Forbidden", ErrorCodes.Forbidden, null);
        }

        if (requiresRole is not null && !await _permissionService.HasAnySystemRoleAsync(userId, requiresRole.Roles))
        {
            var roles = requiresRole.Roles.ToList();
            var message = roles.Count == 1
                ? $"You need the role {roles[0]}."
                : $"You need one of the roles {string.Join(", ", roles)}.";
            return new Refusal(message, ErrorCodes.RoleRequired, roles);
        }

        if (requiresPermission is not null)
        {
            var missing = new List<string>();
            foreach (var permission in requiresPermission.Permissions)
            {
                if (!await _permissionService.HasPermissionAsync(userId, permission))
                {
                    missing.Add(permission);
                }
            }

            if (missing.Count > 0)
            {
                var message = missing.Count == 1
                    ? $"You need the permission {missing[0]}."
                    : $"You need the permissions {string.Join(", ", missing)}.";
                return new Refusal(message, ErrorCodes.PermissionRequired, missing);
            }
        }

        return null;
    }

    /// <summary>
    /// Determines if the return type is Task&lt;OperationResult&lt;T&gt;&gt; or Task&lt;OperationResult&gt;.
    /// </summary>
    private static bool TryGetOperationResultInfo(Type returnType, out bool isGenericResult, out Type? innerType)
    {
        isGenericResult = false;
        innerType = null;

        // Must be Task<T> (generic Task)
        if (!returnType.IsGenericType || returnType.GetGenericTypeDefinition() != typeof(Task<>))
        {
            return false;
        }

        var taskArgument = returnType.GetGenericArguments()[0];

        // Check for non-generic OperationResult
        if (taskArgument == typeof(OperationResult))
        {
            return true;
        }

        // Check for generic OperationResult<T>
        if (taskArgument.IsGenericType && taskArgument.GetGenericTypeDefinition() == typeof(OperationResult<>))
        {
            isGenericResult = true;
            innerType = taskArgument.GetGenericArguments()[0];
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the corresponding method on the interface type TInterface.
    /// </summary>
    private static MethodInfo? GetInterfaceMethod(MethodInfo implementationMethod)
    {
        var interfaceMethods = typeof(TInterface).GetMethods();

        // Find the method with matching name and parameter types
        return Array.Find(interfaceMethods, m =>
            m.Name == implementationMethod.Name &&
            ParameterTypesMatch(m.GetParameters(), implementationMethod.GetParameters()));
    }

    private static bool ParameterTypesMatch(ParameterInfo[] a, ParameterInfo[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            if (a[i].ParameterType != b[i].ParameterType)
            {
                return false;
            }
        }

        return true;
    }
}
