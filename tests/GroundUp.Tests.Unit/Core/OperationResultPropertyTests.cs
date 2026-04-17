using FsCheck;
using FsCheck.Xunit;
using GroundUp.Core;
using GroundUp.Core.Results;

namespace GroundUp.Tests.Unit.Core;

/// <summary>
/// Property-based tests for <see cref="OperationResult{T}"/>.
/// Validates correctness properties from the Phase 1 design document.
/// </summary>
public sealed class OperationResultPropertyTests
{
    /// <summary>
    /// Property 1: Ok factory preserves data and marks success.
    /// For any value of type T, Ok(data, message, statusCode) produces
    /// Success == true with all inputs preserved.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Ok_PreservesDataAndMarksSuccess(string data, NonNull<string> message, PositiveInt statusCode)
    {
        var result = OperationResult<string>.Ok(data, message.Get, statusCode.Get);

        return (result.Success == true
            && result.Data == data
            && result.Message == message.Get
            && result.StatusCode == statusCode.Get)
            .ToProperty();
    }

    /// <summary>
    /// Property 2: Fail factory preserves error details and marks failure.
    /// For any message, status code, optional error code, and optional error list,
    /// Fail produces Success == false with all inputs preserved.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Fail_PreservesErrorDetailsAndMarksFailure(
        NonNull<string> message,
        PositiveInt statusCode,
        string? errorCode)
    {
        var errors = new List<string> { "error1", "error2" };
        var result = OperationResult<string>.Fail(message.Get, statusCode.Get, errorCode, errors);

        return (result.Success == false
            && result.Message == message.Get
            && result.StatusCode == statusCode.Get
            && result.ErrorCode == errorCode
            && result.Errors == errors)
            .ToProperty();
    }

    /// <summary>
    /// Property 3: Failure shorthand factories produce correct status codes.
    /// NotFound → 404, BadRequest → 400, Unauthorized → 401, Forbidden → 403.
    /// All have Success == false.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NotFound_Returns404(NonNull<string> message)
    {
        var result = OperationResult<string>.NotFound(message.Get);
        return (result.Success == false
            && result.StatusCode == 404
            && result.Message == message.Get
            && result.ErrorCode == ErrorCodes.NotFound)
            .ToProperty();
    }

    [Property(MaxTest = 100)]
    public Property BadRequest_Returns400(NonNull<string> message)
    {
        var errors = new List<string> { "field is required" };
        var result = OperationResult<string>.BadRequest(message.Get, errors);
        return (result.Success == false
            && result.StatusCode == 400
            && result.Message == message.Get
            && result.ErrorCode == ErrorCodes.ValidationFailed
            && result.Errors == errors)
            .ToProperty();
    }

    [Property(MaxTest = 100)]
    public Property Unauthorized_Returns401(NonNull<string> message)
    {
        var result = OperationResult<string>.Unauthorized(message.Get);
        return (result.Success == false
            && result.StatusCode == 401
            && result.Message == message.Get
            && result.ErrorCode == ErrorCodes.Unauthorized)
            .ToProperty();
    }

    [Property(MaxTest = 100)]
    public Property Forbidden_Returns403(NonNull<string> message)
    {
        var result = OperationResult<string>.Forbidden(message.Get);
        return (result.Success == false
            && result.StatusCode == 403
            && result.Message == message.Get
            && result.ErrorCode == ErrorCodes.Forbidden)
            .ToProperty();
    }
}
