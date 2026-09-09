namespace OrderSphere.BuildingBlocks.Security;

/// <summary>
/// Emits security-relevant events to the structured audit log.
/// Implementations write to the standard ILogger pipeline; the
/// OpenTelemetry exporter (ServiceDefaults) forwards the entries
/// to the configured telemetry sink.
/// </summary>
public interface ISecurityAuditLogger
{
    void Log(SecurityAuditEvent evt);
}

/// <summary>
/// An immutable record describing one security-relevant occurrence.
/// All fields except <see cref="Type"/> are optional so callers include
/// only the context that is available at the call site.
/// </summary>
/// <param name="RequestMethod">HTTP method of the request that triggered the event, when it
/// originated from one. Its own field rather than part of <paramref name="Details"/> so an
/// investigation can filter on it.</param>
/// <param name="RequestPath">Route template or path of the triggering request. Carries no query
/// string: query values are caller-supplied and may contain personal data.</param>
/// <param name="Details">
/// Short, OrderSphere-authored description of the occurrence. It must be a value chosen from the
/// call site's own vocabulary — never request input, never an exception message, never
/// user-supplied text. Exception detail belongs on the accompanying
/// <c>logger.LogWarning(ex, ...)</c> call, which carries the type and stack trace; request context
/// belongs in <paramref name="RequestMethod"/> and <paramref name="RequestPath"/>.
/// </param>
public sealed record SecurityAuditEvent(
    SecurityAuditEventType Type,
    string? UserId = null,
    string? SessionId = null,
    string? IpAddress = null,
    string? Details = null,
    string? RequestMethod = null,
    string? RequestPath = null)
{
    /// <summary>UTC timestamp of the event. Defaults to now if not supplied.</summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public enum SecurityAuditEventType
{
    LoginSuccess,
    LoginFailure,
    LogoutInitiated,
    BackchannelLogoutReceived,
    BackchannelLogoutRevoked,
    RefreshTokenRotated,
    RefreshTokenRevoked,
    AuthorizationDenied,
    TokenValidationFailed,
    AntiforgeryValidationFailed,
}
