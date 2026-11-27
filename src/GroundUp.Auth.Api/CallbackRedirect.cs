using GroundUp.Auth.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace GroundUp.Auth.Api;

/// <summary>
/// Builds the address the sign-in callback redirects to (GU-87): the client's return URL with the
/// outcome added to its query string. Never adds the token or the error message.
/// </summary>
internal static class CallbackRedirect
{
    /// <summary>Query keys the outcome owns; values already in the return URL are replaced.</summary>
    private static readonly string[] OutcomeKeys = ["result", "error", "alreadyMember", "idpError"];

    /// <summary>
    /// The return URL with <c>result=signed_in</c> (and <c>alreadyMember=true</c>),
    /// <c>result=tenant_picker</c>, <c>result=no_tenant</c>, or <c>error=CODE</c> (and
    /// <c>idpError=...</c>) added.
    /// </summary>
    /// <param name="returnUrl">The validated return URL from the flow state (a path on this site).</param>
    /// <param name="result">The callback's outcome.</param>
    public static string Build(string returnUrl, FlowResult result)
    {
        var queryStart = returnUrl.IndexOf('?');
        var path = queryStart < 0 ? returnUrl : returnUrl[..queryStart];
        var existing = queryStart < 0
            ? new Dictionary<string, StringValues>()
            : QueryHelpers.ParseQuery(returnUrl[queryStart..]);

        var query = new List<KeyValuePair<string, string?>>();
        foreach (var (key, values) in existing)
        {
            if (OutcomeKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            query.AddRange(values.Select(value => new KeyValuePair<string, string?>(key, value)));
        }

        query.AddRange(Outcome(result));
        return QueryHelpers.AddQueryString(path, query);
    }

    private static IEnumerable<KeyValuePair<string, string?>> Outcome(FlowResult result)
    {
        // Tenantless results are also IsSuccess, so they are checked first
        if (result.IsSuccess && result.IsTenantless)
        {
            yield return new("result", result.RequiresTenantSelection ? "tenant_picker" : "no_tenant");
            yield break;
        }

        if (result.IsSuccess)
        {
            yield return new("result", "signed_in");
            if (result.AlreadyMember)
            {
                yield return new("alreadyMember", "true");
            }

            yield break;
        }

        yield return new("error", result.ErrorCode ?? "UNKNOWN_ERROR");
        if (result.IdpError is not null)
        {
            yield return new("idpError", result.IdpError);
        }
    }
}
