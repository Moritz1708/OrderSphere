namespace OrderSphere.BuildingBlocks.Security;

/// <summary>
/// Ambient tenant slot for worker/background processes, which have no
/// <see cref="Microsoft.AspNetCore.Http.HttpContext"/> (ADR 0012). The consuming message loop must
/// call <see cref="BeginScope"/> with the <c>TenantId</c> carried on the integration event being
/// processed before invoking any persistence code; disposing the scope restores the previous value
/// so it never leaks onto an unrelated message on the same thread/continuation.
/// <see cref="Ambient"/> is read by the shared <c>ITenantContext</c> implementation (see
/// <c>HttpContextTenantContext</c> in ServiceDefaults) in preference to any HTTP claim, so the same
/// registration works for both API requests and worker message loops.
/// </summary>
public static class AmbientTenantContext
{
    private static readonly AsyncLocal<Guid?> Current = new();

    /// <summary>The ambient tenant set by the innermost open <see cref="BeginScope"/>, if any.</summary>
    public static Guid? Ambient => Current.Value;

    public static IDisposable BeginScope(Guid tenantId)
    {
        var previous = Current.Value;
        Current.Value = tenantId;
        return new Scope(previous);
    }

    private sealed class Scope(Guid? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
