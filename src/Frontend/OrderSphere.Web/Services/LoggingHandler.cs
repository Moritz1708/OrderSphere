using System.Diagnostics;

namespace OrderSphere.Web.Services;

/// <summary>
/// Outermost HTTP pipeline handler. Logs failed and slow requests for client-side
/// observability. Streaming requests (advisor SSE) are unaffected: timing is measured
/// up to the response headers only, and caller cancellation is not treated as an error.
/// </summary>
public sealed class LoggingHandler(ILogger<LoggingHandler> logger) : DelegatingHandler
{
    private const long SlowThresholdMs = 2000;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // AbsolutePath, not PathAndQuery: the query string can carry identifiers or filter
        // values that are personal data, and it adds nothing to a client-side diagnosis.
        var path = request.RequestUri?.AbsolutePath;
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                // The gateway echoes X-Request-Id, which is the trace id and the correlation_id
                // on every server-side record for this call. Surfacing it here is what lets a
                // user-reported browser error be traced to the server logs, given that WASM logs
                // themselves never leave the browser (see docs/logging.md).
                logger.LogWarning("HTTP {Method} {Path} -> {Status} in {Elapsed}ms [correlation {CorrelationId}]",
                    request.Method, path, (int)response.StatusCode, sw.ElapsedMilliseconds,
                    ReadCorrelationId(response));
            }
            else if (sw.ElapsedMilliseconds > SlowThresholdMs)
            {
                logger.LogInformation("Slow HTTP {Method} {Path} -> {Status} in {Elapsed}ms [correlation {CorrelationId}]",
                    request.Method, path, (int)response.StatusCode, sw.ElapsedMilliseconds,
                    ReadCorrelationId(response));
            }

            return response;
        }
        catch (OperationCanceledException)
        {
            // Caller/navigation cancellation — not a fault.
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "HTTP {Method} {Path} failed after {Elapsed}ms",
                request.Method, path, sw.ElapsedMilliseconds);
            throw;
        }
    }

    private static string ReadCorrelationId(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Request-Id", out var values)
            ? values.FirstOrDefault() ?? "-"
            : "-";
}
