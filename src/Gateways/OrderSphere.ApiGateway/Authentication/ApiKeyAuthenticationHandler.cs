using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OrderSphere.ApiGateway.Authentication;

public sealed class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions;

/// <summary>
/// Authenticates B2B partner requests carrying an <c>X-API-Key</c> header (B6). The raw
/// key is hashed and looked up against the Partners service via <see cref="IPartnerLookupClient"/>;
/// a successful, active lookup produces a <see cref="ClaimsPrincipal"/> carrying
/// <c>partner_id</c>/<c>partner_name</c>/<c>quota_tier</c> claims, which the "partner"
/// authorization policy and the gateway's per-partner rate limiter both key off.
///
/// Lookups are cached briefly (<see cref="CacheDuration"/>) so a partner sending many requests
/// per second does not add a Partners-service round trip to every single one.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IPartnerLookupClient partnerLookupClient,
    IMemoryCache cache) : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-API-Key";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var headerValues))
            return AuthenticateResult.NoResult();

        var apiKey = headerValues.ToString();
        if (string.IsNullOrWhiteSpace(apiKey))
            return AuthenticateResult.Fail("Missing API key.");

        var keyHash = ApiKeyHasher.Hash(apiKey);
        var cacheKey = $"partner-lookup:{keyHash}";

        if (!cache.TryGetValue(cacheKey, out PartnerLookupResult? partner))
        {
            partner = await partnerLookupClient.FindByKeyHashAsync(keyHash, Context.RequestAborted);
            cache.Set(cacheKey, partner, CacheDuration);
        }

        if (partner is null || !string.Equals(partner.Status, "Active", StringComparison.Ordinal))
            return AuthenticateResult.Fail("Invalid or revoked API key.");

        var claims = new[]
        {
            new Claim("partner_id", partner.PartnerId.ToString()),
            new Claim("partner_name", partner.Name),
            new Claim("quota_tier", partner.QuotaTier),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }
}
