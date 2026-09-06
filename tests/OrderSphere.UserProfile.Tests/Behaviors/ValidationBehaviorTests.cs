using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using OrderSphere.BuildingBlocks.Behaviors;
using OrderSphere.BuildingBlocks.Primitives;
using Xunit;

namespace OrderSphere.UserProfile.Tests.Behaviors;


/// <summary>Command whose response is the non-generic <see cref="Result"/>.</summary>
public sealed record VoidCommand(string Value) : IRequest<Result>;

/// <summary>Command whose response is <see cref="Result{T}"/> (reflection branch).</summary>
public sealed record DtoCommand(string Value) : IRequest<Result<string>>;

/// <summary>Command whose response is a plain string (non-Result fallback path).</summary>
public sealed record PlainCommand(string Value) : IRequest<string>;


public sealed class VoidCommandValidator : AbstractValidator<VoidCommand>
{
    public VoidCommandValidator() => RuleFor(x => x.Value).NotEmpty();
}

public sealed class DtoCommandValidator : AbstractValidator<DtoCommand>
{
    public DtoCommandValidator() => RuleFor(x => x.Value).NotEmpty();
}

public sealed class PlainCommandValidator : AbstractValidator<PlainCommand>
{
    public PlainCommandValidator() => RuleFor(x => x.Value).NotEmpty();
}


/// <summary>
/// Covers all three code paths inside <see cref="ValidationBehavior{TRequest,TResponse}"/>:
/// <list type="bullet">
///   <item>No validators registered → next() is called.</item>
///   <item>Validation passes → next() is called.</item>
///   <item>Validation fails, TResponse = Result → returns Result.Failure without throwing.</item>
///   <item>Validation fails, TResponse = Result&lt;T&gt; → returns Result&lt;T&gt;.Failure via reflection.</item>
///   <item>Validation fails, TResponse is not a Result type → throws ValidationException.</item>
/// </list>
/// </summary>
public sealed class ValidationBehaviorTests
{

    /// <summary>
    /// A next delegate that records whether it was invoked.
    /// </summary>
    private sealed class CallTracker<TResponse>
    {
        public bool WasCalled { get; private set; }
        private readonly TResponse _returnValue;

        public CallTracker(TResponse returnValue) => _returnValue = returnValue;

        public RequestHandlerDelegate<TResponse> Delegate =>
            _ => { WasCalled = true; return Task.FromResult(_returnValue); };
    }


    [Fact]
    public async Task Handle_NoValidators_InvokesNext()
    {
        var tracker = new CallTracker<Result>(Result.Success());
        var behavior = new ValidationBehavior<VoidCommand, Result>([], NullLogger<ValidationBehavior<VoidCommand, Result>>.Instance);

        await behavior.Handle(new VoidCommand("x"), tracker.Delegate, CancellationToken.None);

        tracker.WasCalled.Should().BeTrue();
    }


    [Fact]
    public async Task Handle_ValidationPasses_InvokesNext()
    {
        var tracker = new CallTracker<Result>(Result.Success());
        var behavior = new ValidationBehavior<VoidCommand, Result>([new VoidCommandValidator()], NullLogger<ValidationBehavior<VoidCommand, Result>>.Instance);

        await behavior.Handle(new VoidCommand("non-empty"), tracker.Delegate, CancellationToken.None);

        tracker.WasCalled.Should().BeTrue();
    }


    [Fact]
    public async Task Handle_ValidationFails_NonGenericResult_ReturnsFailureWithoutThrowing()
    {
        var tracker = new CallTracker<Result>(Result.Success());
        var behavior = new ValidationBehavior<VoidCommand, Result>([new VoidCommandValidator()], NullLogger<ValidationBehavior<VoidCommand, Result>>.Instance);

        var result = await behavior.Handle(new VoidCommand(""), tracker.Delegate, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        tracker.WasCalled.Should().BeFalse("next must not be called when validation fails");
    }

    [Fact]
    public async Task Handle_ValidationFails_NonGenericResult_ErrorCodeIsValidationInvalid()
    {
        var behavior = new ValidationBehavior<VoidCommand, Result>([new VoidCommandValidator()], NullLogger<ValidationBehavior<VoidCommand, Result>>.Instance);
        RequestHandlerDelegate<Result> next = _ => Task.FromResult(Result.Success());

        var result = await behavior.Handle(new VoidCommand(""), next, CancellationToken.None);

        result.Error.Code.Should().Be("Validation.Invalid");
    }


    [Fact]
    public async Task Handle_ValidationFails_GenericResultT_ReturnsFailureWithoutThrowing()
    {
        var tracker = new CallTracker<Result<string>>(Result<string>.Success("ok"));
        var behavior = new ValidationBehavior<DtoCommand, Result<string>>([new DtoCommandValidator()], NullLogger<ValidationBehavior<DtoCommand, Result<string>>>.Instance);

        var result = await behavior.Handle(new DtoCommand(""), tracker.Delegate, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        tracker.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ValidationFails_GenericResultT_ErrorCodeIsValidationInvalid()
    {
        var behavior = new ValidationBehavior<DtoCommand, Result<string>>([new DtoCommandValidator()], NullLogger<ValidationBehavior<DtoCommand, Result<string>>>.Instance);
        RequestHandlerDelegate<Result<string>> next = _ => Task.FromResult(Result<string>.Success("ok"));

        var result = await behavior.Handle(new DtoCommand(""), next, CancellationToken.None);

        result.Error.Code.Should().Be("Validation.Invalid");
    }

    [Fact]
    public async Task Handle_ValidationFails_GenericResultT_ValueAccessThrows()
    {
        var behavior = new ValidationBehavior<DtoCommand, Result<string>>([new DtoCommandValidator()], NullLogger<ValidationBehavior<DtoCommand, Result<string>>>.Instance);
        RequestHandlerDelegate<Result<string>> next = _ => Task.FromResult(Result<string>.Success("ok"));

        var result = await behavior.Handle(new DtoCommand(""), next, CancellationToken.None);

        result.Invoking(r => _ = r.Value)
              .Should().Throw<InvalidOperationException>("failure result has no value");
    }


    [Fact]
    public async Task Handle_ValidationFails_NonResultResponseType_ThrowsValidationException()
    {
        var behavior = new ValidationBehavior<PlainCommand, string>([new PlainCommandValidator()], NullLogger<ValidationBehavior<PlainCommand, string>>.Instance);
        RequestHandlerDelegate<string> next = _ => Task.FromResult("ok");

        await behavior.Invoking(b => b.Handle(new PlainCommand(""), next, CancellationToken.None))
                      .Should().ThrowAsync<ValidationException>();
    }


    [Fact]
    public async Task Handle_ValidationFails_ErrorDescriptionContainsValidatorMessage()
    {
        var behavior = new ValidationBehavior<VoidCommand, Result>([new VoidCommandValidator()], NullLogger<ValidationBehavior<VoidCommand, Result>>.Instance);
        RequestHandlerDelegate<Result> next = _ => Task.FromResult(Result.Success());

        var result = await behavior.Handle(new VoidCommand(""), next, CancellationToken.None);

        result.Error.Description.Should().NotBeNullOrEmpty();
    }
}
