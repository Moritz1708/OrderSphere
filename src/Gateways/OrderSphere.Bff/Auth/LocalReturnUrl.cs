namespace OrderSphere.Bff.Auth;

/// <summary>
/// Restricts the post-login redirect of <c>/bff/login</c> to paths on this origin.
/// <c>returnUrl</c> is a query parameter any link can set; accepting an absolute or
/// protocol-relative URL would send the user to a foreign site after a genuine sign-in.
/// </summary>
public static class LocalReturnUrl
{
    /// <summary>Returns <paramref name="returnUrl"/> when it is a local path, otherwise <c>/</c>.</summary>
    public static string Sanitize(string? returnUrl) => IsLocal(returnUrl) ? returnUrl : "/";

    // Same rule as ASP.NET Core's IsLocalUrl: one leading '/', not followed by '/' or '\'
    // (browsers treat "//host" and "/\host" as protocol-relative), and no control
    // characters (browsers strip e.g. a tab, so "/\t/host" collapses to "//host").
    private static bool IsLocal([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? url) =>
        !string.IsNullOrEmpty(url)
        && url[0] == '/'
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
        && !url.Any(char.IsControl);
}
