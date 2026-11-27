namespace GroundUp.Auth.Services;

/// <summary>
/// Rules for the return URL a browser client passes when it starts a sign-in (GU-87). After the
/// callback, the browser is redirected to it with the outcome. Only a path on the same host is
/// accepted, so the redirect can never send the user to another site.
/// </summary>
public static class ReturnUrlRules
{
    /// <summary>The longest accepted return URL (the <c>AuthFlowState.ReturnUrl</c> column).</summary>
    public const int MaxLength = 2048;

    /// <summary>
    /// Checks a return URL. Null, empty or whitespace means no return URL and is accepted with
    /// <paramref name="normalized"/> null. Otherwise the value is accepted unchanged when it is a
    /// path on this host: it starts with a single <c>/</c> (not <c>//</c> or <c>/\</c>), has no
    /// backslash, fragment or control character, is at most <see cref="MaxLength"/> characters, parses
    /// as a relative URI, and is not the callback path or under it (which would loop).
    /// </summary>
    /// <param name="returnUrl">The value the client sent.</param>
    /// <param name="callbackPath">The sign-in callback path (<c>AuthOptions.CallbackPath</c>).</param>
    /// <param name="normalized">The value to store, or null when there is none.</param>
    /// <returns>False when the value must be refused.</returns>
    public static bool TryNormalize(string? returnUrl, string callbackPath, out string? normalized)
    {
        normalized = null;

        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return true;
        }

        if (returnUrl.Length > MaxLength)
        {
            return false;
        }

        // A single leading slash. "//host" and "/\host" are read by browsers as another host.
        if (returnUrl[0] != '/' || (returnUrl.Length > 1 && (returnUrl[1] == '/' || returnUrl[1] == '\\')))
        {
            return false;
        }

        foreach (var c in returnUrl)
        {
            if (c == '\\' || c == '#' || c < ' ' || c == '\u007f')
            {
                return false;
            }
        }

        if (!Uri.TryCreate(returnUrl, UriKind.Relative, out _))
        {
            return false;
        }

        if (IsCallbackPath(returnUrl, callbackPath))
        {
            return false;
        }

        normalized = returnUrl;
        return true;
    }

    private static bool IsCallbackPath(string returnUrl, string callbackPath)
    {
        if (string.IsNullOrEmpty(callbackPath))
        {
            return false;
        }

        var queryStart = returnUrl.IndexOf('?');
        var path = queryStart < 0 ? returnUrl : returnUrl[..queryStart];
        var callback = callbackPath.TrimEnd('/');

        return path.Equals(callback, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(callback + "/", StringComparison.OrdinalIgnoreCase);
    }
}
