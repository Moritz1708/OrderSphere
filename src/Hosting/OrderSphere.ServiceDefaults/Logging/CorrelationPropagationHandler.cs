using OrderSphere.BuildingBlocks.Security;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Carries the ambient log-correlation id onto every outgoing HTTP call, so that a request
/// entering at the gateway keeps one id across service hops (Ordering to Catalog, Basket to
/// Catalog, and so on).
/// <para>
/// Registered on <c>ConfigureHttpClientDefaults</c> in <c>AddServiceDefaults</c>, which means it
/// applies to every typed client without per-client wiring. W3C trace context is propagated
/// separately by the OpenTelemetry HTTP instrumentation; this header is the human-quotable id
/// that also survives sampling.
/// </para>
/// </summary>
internal sealed class CorrelationPropagationHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (AmbientCorrelationContext.Ambient is { Length: > 0 } correlationId
            && !request.Headers.Contains(RequestContextEnrichmentMiddleware.CorrelationHeader))
        {
            request.Headers.TryAddWithoutValidation(
                RequestContextEnrichmentMiddleware.CorrelationHeader, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
