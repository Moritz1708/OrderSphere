using FluentAssertions;
using OrderSphere.BuildingBlocks.Security;
using Xunit;

namespace OrderSphere.Domain.Tests.Security;

public sealed class AmbientCorrelationContextTests
{
    [Fact]
    public void Ambient_is_null_outside_any_scope()
    {
        AmbientCorrelationContext.Ambient.Should().BeNull();
    }

    [Fact]
    public void Scope_sets_and_restores_the_ambient_value()
    {
        using (AmbientCorrelationContext.BeginScope("outer"))
        {
            AmbientCorrelationContext.Ambient.Should().Be("outer");
        }

        AmbientCorrelationContext.Ambient.Should().BeNull();
    }

    [Fact]
    public void Nested_scope_restores_the_previous_value_not_null()
    {
        // Matters for a worker handling a message that itself triggers a nested operation:
        // the inner scope must not leak, and must not erase the outer one either.
        using (AmbientCorrelationContext.BeginScope("outer"))
        {
            using (AmbientCorrelationContext.BeginScope("inner"))
            {
                AmbientCorrelationContext.Ambient.Should().Be("inner");
            }

            AmbientCorrelationContext.Ambient.Should().Be("outer");
        }
    }

    [Fact]
    public async Task Scope_does_not_leak_into_a_parallel_flow()
    {
        var observed = "unset";

        var other = Task.Run(async () =>
        {
            await Task.Yield();
            observed = AmbientCorrelationContext.Ambient ?? "none";
        });

        using (AmbientCorrelationContext.BeginScope("mine"))
        {
            await other;
        }

        observed.Should().Be("none");
    }
}
