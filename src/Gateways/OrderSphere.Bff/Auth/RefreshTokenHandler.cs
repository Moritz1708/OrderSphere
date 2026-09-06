using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using OrderSphere.BuildingBlocks.Security;

namespace OrderSphere.Bff.Auth;

/// <summary>
/// CookieAuthenticationEvents implementation that performs refresh-token rotation.
/// When the stored access token is within 60 seconds of expiry, this handler calls
/// the OIDC token endpoint (resolved via discovery) with the current refresh token
/// and replaces both tokens in the authentication ticket (which triggers a Redis
/// store update via ShouldRenew). On refresh failure the principal is rejected,
/// forcing re-authentication. Security events are emitted via ISecurityAuditLogger.
/// </summary>
public sealed class RefreshTokenHandler : CookieAuthenticationEvents
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfigurationManager<OpenIdConnectConfiguration> _oidcConfigManager;
    private readonly ISecurityAuditLogger _auditLogger;
    private readonly ILogger<RefreshTokenHandler> _logger;

    public RefreshTokenHandler(
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        IConfigurationManager<OpenIdConnectConfiguration> oidcConfigManager,
        ISecurityAuditLogger auditLogger,
        ILogger<RefreshTokenHandler> logger)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
        _oidcConfigManager = oidcConfigManager;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var accessToken = context.Properties.GetTokenValue("access_token");
        var refreshToken = context.Properties.GetTokenValue("refresh_token");
        var expiresAt = context.Properties.GetTokenValue("expires_at");

        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
            return;

        if (!DateTimeOffset.TryParse(expiresAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expiry))
            return;

        if (expiry > DateTimeOffset.UtcNow.AddSeconds(60))
            return;

        _logger.LogInformation("Access token expiry within 60 s. Initiating refresh token rotation.");

        try
        {
            var tokenResponse = await ExchangeRefreshTokenAsync(refreshToken);

            if (tokenResponse is null)
            {
                _logger.LogWarning("Refresh token exchange failed. Rejecting principal and signing out.");
                var sub = context.Principal?.FindFirst("sub")?.Value;
                var sid = context.Principal?.FindFirst("session_state")?.Value
                       ?? context.Principal?.FindFirst("sid")?.Value;
                _auditLogger.Log(new SecurityAuditEvent(
                    SecurityAuditEventType.RefreshTokenRevoked,
                    UserId: sub,
                    SessionId: sid,
                    IpAddress: context.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    Details: "Refresh token exchange returned an error response."));
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            context.Properties.UpdateTokenValue("access_token", tokenResponse.AccessToken);
            context.Properties.UpdateTokenValue("refresh_token", tokenResponse.RefreshToken);
            context.Properties.UpdateTokenValue("expires_at",
                DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn).ToString("o"));

            context.ShouldRenew = true;

            var rotatedSub = context.Principal?.FindFirst("sub")?.Value;
            var rotatedSid = context.Principal?.FindFirst("session_state")?.Value
                          ?? context.Principal?.FindFirst("sid")?.Value;
            _auditLogger.Log(new SecurityAuditEvent(
                SecurityAuditEventType.RefreshTokenRotated,
                UserId: rotatedSub,
                SessionId: rotatedSid,
                IpAddress: context.HttpContext.Connection.RemoteIpAddress?.ToString()));

            _logger.LogInformation("Refresh token rotation completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during refresh token rotation.");
            var sub = context.Principal?.FindFirst("sub")?.Value;
            _auditLogger.Log(new SecurityAuditEvent(
                SecurityAuditEventType.RefreshTokenRevoked,
                UserId: sub,
                IpAddress: context.HttpContext.Connection.RemoteIpAddress?.ToString(),
                Details: "Unhandled exception: " + ex.Message));
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    private async Task<TokenResponse?> ExchangeRefreshTokenAsync(string refreshToken)
    {
        var clientId = _config["Oidc:ClientId"]
            ?? throw new InvalidOperationException("Oidc:ClientId not configured.");
        var clientSecret = _config["Oidc:ClientSecret"]
            ?? throw new InvalidOperationException("Oidc:ClientSecret not configured.");

        var oidcConfig = await _oidcConfigManager.GetConfigurationAsync(CancellationToken.None);
        var tokenEndpoint = oidcConfig.TokenEndpoint
            ?? throw new InvalidOperationException("OIDC discovery did not return a token_endpoint.");

        var client = _httpClientFactory.CreateClient("oidc-token");
        var body = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("refresh_token", refreshToken),
        ]);

        var response = await client.PostAsync(tokenEndpoint, body);
        if (!response.IsSuccessStatusCode)
        {
            var error = await OAuthErrorReader.ReadAsync(response.Content);
            _logger.LogWarning("Token endpoint returned {StatusCode}: {Error}", response.StatusCode, error);
            return null;
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new TokenResponse(
            json.GetProperty("access_token").GetString()!,
            json.GetProperty("refresh_token").GetString()!,
            json.GetProperty("expires_in").GetInt32());
    }

    private sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn);
}
