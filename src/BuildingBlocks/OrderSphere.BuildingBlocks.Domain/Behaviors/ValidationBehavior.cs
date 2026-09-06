using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.Primitives;

namespace OrderSphere.BuildingBlocks.Behaviors;

/// <summary>
/// MediatR pipeline behavior that runs all registered FluentValidation validators for a request.
/// For handlers that return <see cref="Result"/> or <see cref="Result{T}"/>, validation failures
/// are returned as <c>Result.Failure</c> rather than thrown as <see cref="ValidationException"/>.
/// Non-Result response types fall back to throwing so that existing exception handlers still apply.
/// </summary>
public sealed partial class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators,
    ILogger<ValidationBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next(cancellationToken);

        var context = new ValidationContext<TRequest>(request);

        var failures = (await Task.WhenAll(
                validators.Select(v => v.ValidateAsync(context, cancellationToken))))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count == 0)
            return await next(cancellationToken);

        var error = Error.ValidationFailure(
            string.Join("; ", failures.Select(f => f.ErrorMessage)));

        // Validation failures previously left no trace at all: the request became a Result
        // failure, and only the LoggingBehavior's generic warning showed anything. Logging the
        // offending property names (never their values, which are user input) makes a spike of
        // client-side validation errors diagnosable.
        ValidationFailed(logger, typeof(TRequest).Name, string.Join(", ",
            failures.Select(f => f.PropertyName).Distinct()));

        // Non-generic Result — return Result.Failure(error).
        if (typeof(TResponse) == typeof(Result))
            return (TResponse)(object)Result.Failure(error);

        // Generic Result<T> — call Result<T>.Failure(error) via reflection.
        if (typeof(TResponse).IsGenericType &&
            typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var innerType = typeof(TResponse).GetGenericArguments()[0];
            var failureMethod = typeof(Result<>)
                .MakeGenericType(innerType)
                .GetMethod(nameof(Result.Failure), BindingFlags.Public | BindingFlags.Static)!;
            return (TResponse)failureMethod.Invoke(null, [error])!;
        }

        // Fallback for any non-Result handler: preserve existing exception-based behaviour.
        throw new ValidationException(failures);
    }

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Debug,
        Message = "{requestName} failed validation on: {properties}")]
    private static partial void ValidationFailed(ILogger logger, string requestName, string properties);
}
