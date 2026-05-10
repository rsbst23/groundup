using System.Reflection;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Attributes;
using GroundUp.Core.Exceptions;
using GroundUp.Core.Results;

namespace GroundUp.Auth.Services.Authorization;

/// <summary>
/// DispatchProxy-based interceptor that enforces <see cref="RequiresPermissionAttribute"/> and
/// <see cref="RequiresRoleAttribute"/> attributes on service interface methods.
/// For methods returning <c>Task&lt;OperationResult&lt;T&gt;&gt;</c> or <c>Task&lt;OperationResult&gt;</c>,
/// returns <c>OperationResult.Forbidden()</c> when authorization fails.
/// For all other return types, throws <see cref="ForbiddenAccessException"/> when authorization fails.
/// </summary>
/// <typeparam name="TInterface">The service interface type being proxied.</typeparam>
public class AuthorizationInterceptor<TInterface> : DispatchProxy
    where TInterface : class
{
    private TInterface _target = null!;
    private IPermissionService _permissionService = null!;
    private ICurrentUser _currentUser = null!;

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

        // Check if the return type is an OperationResult (Task<OperationResult<T>> or Task<OperationResult>)
        if (TryGetOperationResultInfo(targetMethod.ReturnType, out var isGenericResult, out var innerType))
        {
            // OperationResult return type — return Forbidden() on auth failure
            return ExecuteWithAuthorizationAsync(targetMethod, args, requiresPermission, requiresRole, isGenericResult, innerType);
        }

        // Non-OperationResult return type — throw ForbiddenAccessException on auth failure
        return ExecuteWithExceptionAuthorizationAsync(targetMethod, args, requiresPermission, requiresRole);
    }

    /// <summary>
    /// Creates a proxy wrapping the target implementation with authorization enforcement.
    /// </summary>
    /// <param name="target">The real service implementation.</param>
    /// <param name="permissionService">The permission service for authorization checks.</param>
    /// <param name="currentUser">The current user context.</param>
    /// <returns>A proxy instance implementing <typeparamref name="TInterface"/>.</returns>
    public static TInterface Create(TInterface target, IPermissionService permissionService, ICurrentUser currentUser)
    {
        var proxy = Create<TInterface, AuthorizationInterceptor<TInterface>>();
        var interceptor = (AuthorizationInterceptor<TInterface>)(object)proxy;
        interceptor._target = target;
        interceptor._permissionService = permissionService;
        interceptor._currentUser = currentUser;
        return proxy;
    }

    private object ExecuteWithAuthorizationAsync(
        MethodInfo targetMethod,
        object?[]? args,
        RequiresPermissionAttribute? requiresPermission,
        RequiresRoleAttribute? requiresRole,
        bool isGenericResult,
        Type? innerType)
    {
        // We need to return the correctly typed Task<OperationResult<T>> or Task<OperationResult>
        // Use a generic helper method via reflection to get the correct return type
        if (isGenericResult && innerType is not null)
        {
            var helperMethod = typeof(AuthorizationInterceptor<TInterface>)
                .GetMethod(nameof(ExecuteGenericAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(innerType);

            return helperMethod.Invoke(this, [targetMethod, args, requiresPermission, requiresRole])!;
        }

        return ExecuteNonGenericAsync(targetMethod, args, requiresPermission, requiresRole);
    }

    private async Task<OperationResult<T>> ExecuteGenericAsync<T>(
        MethodInfo targetMethod,
        object?[]? args,
        RequiresPermissionAttribute? requiresPermission,
        RequiresRoleAttribute? requiresRole)
    {
        var userId = _currentUser.UserId;

        // Unauthenticated user (Guid.Empty) is always forbidden
        if (userId == Guid.Empty)
        {
            return OperationResult<T>.Forbidden();
        }

        // Check [RequiresRole] — OR semantics, system roles only
        if (requiresRole is not null)
        {
            var hasRole = await _permissionService.HasAnySystemRoleAsync(userId, requiresRole.Roles);
            if (!hasRole)
            {
                return OperationResult<T>.Forbidden();
            }
        }

        // Check [RequiresPermission] — AND semantics
        if (requiresPermission is not null)
        {
            var userPermissions = await _permissionService.GetUserPermissionsAsync(userId);
            var allSatisfied = requiresPermission.Permissions.All(p => userPermissions.Contains(p));
            if (!allSatisfied)
            {
                return OperationResult<T>.Forbidden();
            }
        }

        // Authorized — invoke the target method
        var result = targetMethod.Invoke(_target, args);
        if (result is Task<OperationResult<T>> typedTask)
        {
            return await typedTask.ConfigureAwait(false);
        }

        // Fallback: shouldn't happen if types are correct
        throw new InvalidOperationException($"Expected Task<OperationResult<{typeof(T).Name}>> but got {result?.GetType().Name ?? "null"}.");
    }

    private async Task<OperationResult> ExecuteNonGenericAsync(
        MethodInfo targetMethod,
        object?[]? args,
        RequiresPermissionAttribute? requiresPermission,
        RequiresRoleAttribute? requiresRole)
    {
        var userId = _currentUser.UserId;

        // Unauthenticated user (Guid.Empty) is always forbidden
        if (userId == Guid.Empty)
        {
            return OperationResult.Forbidden();
        }

        // Check [RequiresRole] — OR semantics, system roles only
        if (requiresRole is not null)
        {
            var hasRole = await _permissionService.HasAnySystemRoleAsync(userId, requiresRole.Roles);
            if (!hasRole)
            {
                return OperationResult.Forbidden();
            }
        }

        // Check [RequiresPermission] — AND semantics
        if (requiresPermission is not null)
        {
            var userPermissions = await _permissionService.GetUserPermissionsAsync(userId);
            var allSatisfied = requiresPermission.Permissions.All(p => userPermissions.Contains(p));
            if (!allSatisfied)
            {
                return OperationResult.Forbidden();
            }
        }

        // Authorized — invoke the target method
        var result = targetMethod.Invoke(_target, args);
        if (result is Task<OperationResult> typedTask)
        {
            return await typedTask.ConfigureAwait(false);
        }

        // Fallback: shouldn't happen if types are correct
        throw new InvalidOperationException($"Expected Task<OperationResult> but got {result?.GetType().Name ?? "null"}.");
    }

    /// <summary>
    /// Handles authorization for methods with non-OperationResult return types.
    /// Throws <see cref="ForbiddenAccessException"/> when authorization fails.
    /// </summary>
    private object? ExecuteWithExceptionAuthorizationAsync(
        MethodInfo targetMethod,
        object?[]? args,
        RequiresPermissionAttribute? requiresPermission,
        RequiresRoleAttribute? requiresRole)
    {
        // For async methods returning Task<T>, we need to return the correctly typed Task<T>
        var returnType = targetMethod.ReturnType;

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            // Task<T> — use a generic helper to preserve the return type
            var resultType = returnType.GetGenericArguments()[0];
            var helperMethod = typeof(AuthorizationInterceptor<TInterface>)
                .GetMethod(nameof(ExecuteWithExceptionGenericTaskAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(resultType);

            return helperMethod.Invoke(this, [targetMethod, args, requiresPermission, requiresRole])!;
        }

        if (typeof(Task).IsAssignableFrom(returnType))
        {
            // Non-generic Task (void async)
            return ExecuteWithExceptionTaskAsync(targetMethod, args, requiresPermission, requiresRole);
        }

        // For synchronous methods, perform the check synchronously
        // Note: This blocks on async permission checks — acceptable for sync methods on decorated interfaces
        var userId = _currentUser.UserId;

        if (userId == Guid.Empty)
        {
            throw new ForbiddenAccessException();
        }

        if (requiresRole is not null)
        {
            var hasRole = _permissionService.HasAnySystemRoleAsync(userId, requiresRole.Roles).GetAwaiter().GetResult();
            if (!hasRole)
            {
                throw new ForbiddenAccessException();
            }
        }

        if (requiresPermission is not null)
        {
            var userPermissions = _permissionService.GetUserPermissionsAsync(userId).GetAwaiter().GetResult();
            var allSatisfied = requiresPermission.Permissions.All(p => userPermissions.Contains(p));
            if (!allSatisfied)
            {
                throw new ForbiddenAccessException();
            }
        }

        return targetMethod.Invoke(_target, args);
    }

    private async Task ExecuteWithExceptionTaskAsync(
        MethodInfo targetMethod,
        object?[]? args,
        RequiresPermissionAttribute? requiresPermission,
        RequiresRoleAttribute? requiresRole)
    {
        await CheckAuthorizationOrThrowAsync(requiresPermission, requiresRole);

        // Authorized — invoke the target method
        var result = targetMethod.Invoke(_target, args);
        if (result is Task task)
        {
            await task.ConfigureAwait(false);
        }
    }

    private async Task<TResult> ExecuteWithExceptionGenericTaskAsync<TResult>(
        MethodInfo targetMethod,
        object?[]? args,
        RequiresPermissionAttribute? requiresPermission,
        RequiresRoleAttribute? requiresRole)
    {
        await CheckAuthorizationOrThrowAsync(requiresPermission, requiresRole);

        // Authorized — invoke the target method
        var result = targetMethod.Invoke(_target, args);
        if (result is Task<TResult> typedTask)
        {
            return await typedTask.ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"Expected Task<{typeof(TResult).Name}> but got {result?.GetType().Name ?? "null"}.");
    }

    /// <summary>
    /// Performs authorization checks and throws <see cref="ForbiddenAccessException"/> on failure.
    /// Shared by all exception-based authorization paths.
    /// </summary>
    private async Task CheckAuthorizationOrThrowAsync(
        RequiresPermissionAttribute? requiresPermission,
        RequiresRoleAttribute? requiresRole)
    {
        var userId = _currentUser.UserId;

        if (userId == Guid.Empty)
        {
            throw new ForbiddenAccessException();
        }

        if (requiresRole is not null)
        {
            var hasRole = await _permissionService.HasAnySystemRoleAsync(userId, requiresRole.Roles);
            if (!hasRole)
            {
                throw new ForbiddenAccessException();
            }
        }

        if (requiresPermission is not null)
        {
            var userPermissions = await _permissionService.GetUserPermissionsAsync(userId);
            var allSatisfied = requiresPermission.Permissions.All(p => userPermissions.Contains(p));
            if (!allSatisfied)
            {
                throw new ForbiddenAccessException();
            }
        }
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
            isGenericResult = false;
            innerType = null;
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
