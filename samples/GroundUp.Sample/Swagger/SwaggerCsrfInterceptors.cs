namespace GroundUp.Sample.Swagger;

/// <summary>
/// Swagger UI interceptors that let "Try it out" pass CSRF protection with the auth cookie,
/// the same way a browser application does:
/// <list type="number">
///   <item>Before a POST, PUT, PATCH or DELETE, fetch the token from <c>GET /auth/csrf</c> once
///   (the browser sends the auth cookie) and keep it in memory.</item>
///   <item>Send it as the <c>X-CSRF-Token</c> header.</item>
///   <item>On a 403 response, forget the token so the next request fetches a fresh one (for
///   example after signing in as a different user).</item>
/// </list>
/// When nobody is signed in, <c>/auth/csrf</c> returns 401 and requests go out without the header,
/// which is fine: CSRF protection only applies to cookie-authenticated requests.
/// <para>
/// The function text must be a single line with no backslashes: Swashbuckle embeds it in the page
/// as <c>JSON.parse('...')</c> inside a JavaScript string literal, which consumes one level of
/// escaping. A backslash (<c>\/</c>, <c>\?</c>) or a line break then becomes invalid JSON and
/// Swagger UI renders blank. So the code uses string methods instead of regular expressions, and
/// the source is joined into one line.
/// </para>
/// <para>
/// Development tooling for the sample only. No GroundUp package depends on Swagger.
/// </para>
/// </summary>
public static class SwaggerCsrfInterceptors
{
    /// <summary>
    /// Swagger UI request interceptor. Swashbuckle turns the string into a function; it returns a
    /// promise of the request, which Swagger UI awaits.
    /// </summary>
    public static readonly string Request = OneLine("""
        function (req) {
          var method = (req.method || 'GET').toUpperCase();
          var safe = ['GET', 'HEAD', 'OPTIONS', 'TRACE'].indexOf(method) >= 0;
          var path = (req.url || '').split('?')[0];
          if (safe || path.slice(-10) === '/auth/csrf') {
            return req;
          }
          var ready = window.groundupCsrfToken
            ? Promise.resolve(window.groundupCsrfToken)
            : fetch('/auth/csrf', { credentials: 'same-origin' })
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(function (body) {
                  window.groundupCsrfToken = body && body.token ? body.token : null;
                  return window.groundupCsrfToken;
                })
                .catch(function () { return null; });
          return ready.then(function (token) {
            if (token) {
              req.headers['X-CSRF-Token'] = token;
            }
            return req;
          });
        }
        """);

    /// <summary>
    /// Swagger UI response interceptor: a 403 or a sign-out drops the cached token.
    /// </summary>
    public static readonly string Response = OneLine("""
        function (res) {
          var path = (res.url || '').split('?')[0];
          if (res.status === 403 || path.slice(-12) === '/auth/logout') {
            window.groundupCsrfToken = null;
          }
          return res;
        }
        """);

    /// <summary>
    /// Joins the lines into one. A line break would reach the browser as a raw newline inside the
    /// <c>JSON.parse('...')</c> string, which is invalid JSON. The code uses semicolons and braces
    /// everywhere, so joining lines does not change its meaning.
    /// </summary>
    private static string OneLine(string code) =>
        string.Join(" ", code.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));
}
