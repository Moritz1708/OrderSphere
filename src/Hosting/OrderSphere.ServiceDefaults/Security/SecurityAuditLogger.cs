using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.Compliance;
using OrderSphere.BuildingBlocks.Security;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// ISecurityAuditLogger implementation that writes structured log entries via the standard
/// ILogger pipeline. OpenTelemetry (wired in ServiceDefaults) carries these entries to the
/// configured telemetry sink with the originating trace_id attached.
/// <para>
/// Each field is its own structured property, so an investigation can filter on
/// <c>audit_event</c> or <c>audit_session_id</c> directly instead of parsing a delimited string.
/// </para>
/// </summary>
internal sealed partial class SecurityAuditLogger(ILogger<SecurityAuditLogger> logger)
    : ISecurityAuditLogger
{
    public void Log(SecurityAuditEvent evt)
    {
        var level = evt.Type switch
        {
            SecurityAuditEventType.LoginFailure => LogLevel.Warning,
            SecurityAuditEventType.RefreshTokenRevoked => LogLevel.Warning,
            SecurityAuditEventType.TokenValidationFailed => LogLevel.Warning,
            SecurityAuditEventType.AntiforgeryValidationFailed => LogLevel.Warning,
            SecurityAuditEventType.AuthorizationDenied => LogLevel.Information,
            _ => LogLevel.Information,
        };

        SecurityAudit(
            logger,
            level,
            evt.Type,
            evt.UserId ?? "-",
            evt.SessionId ?? "-",
            evt.IpAddress ?? "-",
            evt.Details ?? "-",
            evt.RequestMethod ?? "-",
            evt.RequestPath ?? "-",
            evt.OccurredAt);
    }

    /// <summary>
    /// EventId 1201 (ServiceDefaults range 1200-1299, see docs/logging.md). The level is a
    /// parameter because it is derived from the event type.
    /// <para>
    /// The session id and IP are hashed: they identify a person via a join and have no
    /// operational value in plaintext, while the hash still groups repeated attempts from one
    /// source. The user id stays readable so an investigation can pivot to that user's other
    /// records, which carry the same value as the <c>user_id</c> enrichment tag.
    /// </para>
    /// <para>
    /// <c>details</c> is written by OrderSphere code, never by request input; it must not be
    /// used to carry user-supplied text. It is therefore deliberately unclassified — classifying
    /// it would erase the one field whose whole purpose is to say what happened. Request context
    /// has its own fields (<c>auditRequestMethod</c>, <c>auditRequestPath</c>) so callers are not
    /// tempted to interpolate it into <c>details</c>; the path is a route, not a query string,
    /// and carries no caller-supplied values.
    /// </para>
    /// </summary>
    [LoggerMessage(
        EventId = 1201,
        Message = "Security audit: {auditEvent}.")]
    private static partial void SecurityAudit(
        ILogger logger,
        LogLevel level,
        SecurityAuditEventType auditEvent,
        string auditUserId,
        [PseudonymousId] string auditSessionId,
        [PseudonymousId] string auditIpAddress,
        string auditDetails,
        string auditRequestMethod,
        string auditRequestPath,
        DateTimeOffset auditOccurredAt);
}

/// <summary>
/// Extension method to register <see cref="ISecurityAuditLogger"/>.
/// Call from each service's composition root (APIs, BFF) after AddServiceDefaults.
/// </summary>
public static class SecurityAuditLoggerExtensions
{
    /// <summary>
    /// Registers <see cref="ISecurityAuditLogger"/> as a singleton backed by
    /// <see cref="SecurityAuditLogger"/>. Safe to call multiple times (idempotent).
    /// </summary>
    public static IServiceCollection AddSecurityAuditLogger(this IServiceCollection services)
    {
        services.AddSingleton<ISecurityAuditLogger, SecurityAuditLogger>();
        return services;
    }
}
