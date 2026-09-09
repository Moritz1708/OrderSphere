using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.Diagnostics;
using OrderSphere.BuildingBlocks.Primitives;

namespace OrderSphere.BuildingBlocks.Behaviors;

public sealed partial class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        RequestStarting(logger, requestName);

        using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(requestName);
        var sw = Stopwatch.StartNew();
        var outcome = "success";
        try
        {
            var response = await next();
            sw.Stop();

            if (response is Result { IsFailure: true } failure)
            {
                outcome = "failure";
                activity?.SetTag("request.outcome", outcome);
                activity?.SetTag("error.code", failure.Error.Code);
                RequestFailed(logger, requestName, sw.ElapsedMilliseconds, failure.Error.Code, failure.Error.Description);
            }
            else
            {
                RequestCompleted(logger, requestName, sw.ElapsedMilliseconds);
            }

            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            outcome = "exception";
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            RequestThrew(logger, ex, requestName, sw.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            ApplicationDiagnostics.RequestDuration.Record(
                sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("request", requestName),
                new KeyValuePair<string, object?>("outcome", outcome));
        }
    }

    // EventId range 1000-1099 (BuildingBlocks, see docs/logging.md).

    [LoggerMessage(EventId = 1001, Level = LogLevel.Debug, Message = "Handling {requestName}.")]
    private static partial void RequestStarting(ILogger logger, string requestName);

    // Debug, not Information: this fires for every query on every API, and the duration it
    // reports is already carried by the request span and the ordersphere.mediatr.request.duration
    // histogram. An Information record per read would be pure duplication at the highest volume
    // point in the system. Failures below stay at Warning/Error, where the level is the signal.
    [LoggerMessage(EventId = 1002, Level = LogLevel.Debug, Message = "{requestName} completed in {elapsedMs}ms.")]
    private static partial void RequestCompleted(ILogger logger, string requestName, long elapsedMs);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Warning,
        Message = "{requestName} failed in {elapsedMs}ms: [{errorCode}] {errorDescription}")]
    private static partial void RequestFailed(
        ILogger logger, string requestName, long elapsedMs, string errorCode, string errorDescription);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Error, Message = "{requestName} threw after {elapsedMs}ms.")]
    private static partial void RequestThrew(ILogger logger, Exception exception, string requestName, long elapsedMs);
}
