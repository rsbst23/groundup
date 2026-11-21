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
/// Development tooling for the sample only. No GroundUp package depends on Swagger.
/// </para>
/// </summary>
public static class SwaggerCsrfInterceptors
{
    /// <summary>
    /// Swagger UI request interceptor. Swashbuckle turns the string into a function; it returns a
    /// promise of the request, which Swagger UI awaits.
    /// </summary>
    public const string Request = """
        function (req) {
          var unsafeMethod = !/^(GET|HEAD|OPTIONS|TRACE)$/i.test(req.method || 'GET');
          if (!unsafeMethod || /\/auth\/csrf(\?|$)/.test(req.url)) {
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
        """;

    /// <summary>
    /// Swagger UI response interceptor: a 403 or a sign-out drops the cached token.
    /// </summary>
    public const string Response = """
        function (res) {
          if (res.status === 403 || /\/auth\/logout(\?|$)/.test(res.url || '')) {
            window.groundupCsrfToken = null;
          }
          return res;
        }
        """;
}
