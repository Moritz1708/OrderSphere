using System.Net;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Simmy;
using Polly.Simmy.Latency;
using Polly.Simmy.Outcomes;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Opt-in chaos-engineering configuration for a single named HttpClient, bound from the
/// "Chaos" section. Disabled by default so DEV/Staging/Prod are unaffected unless a service
/// explicitly enables it (e.g. for a resilience drill or a chaos-driven test).
/// </summary>
public sealed class ChaosOptions
{
    public const string SectionName = "Chaos";

    public bool Enabled { get; set; }

    /// <summary>Fraction of requests (0.0-1.0) that receive a synthetic HTTP 409 Conflict response.</summary>
    public double FaultInjectionRate { get; set; } = 0.2;

    /// <summary>Fraction of requests (0.0-1.0) that receive injected extra latency.</summary>
    public double LatencyInjectionRate { get; set; } = 0.2;

    public TimeSpan Latency { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Adds a Polly chaos pipeline (via the Simmy strategies built into Polly.Core) to an
/// HttpClient, downstream of the standard resilience handler added by
/// <c>ConfigureHttpClientDefaults</c> in <see cref="Extensions.AddServiceDefaults"/>. A no-op when
/// "Chaos:Enabled" is not set, so calling it unconditionally on a client is safe.
/// </summary>
public static class ChaosExtensions
{
    public static IHttpClientBuilder AddOrderSphereChaos(
        this IHttpClientBuilder httpClientBuilder, IConfiguration configuration)
    {
        var options = configuration.GetSection(ChaosOptions.SectionName).Get<ChaosOptions>() ?? new ChaosOptions();
        if (!options.Enabled)
            return httpClientBuilder;

        httpClientBuilder.AddResilienceHandler("chaos", builder =>
        {
            builder.AddChaosLatency(new ChaosLatencyStrategyOptions
            {
                InjectionRate = options.LatencyInjectionRate,
                Latency = options.Latency
            });

            // A synthesized 409 exercises the same conflict path a real Catalog confirm-conflict
            // would (HttpCatalogClient.ConfirmReservationAsync maps HTTP 409 -> ErrorType.Conflict),
            // proving the saga compensates correctly when the fault arrives through the real
            // resilience pipeline rather than a hand-rolled mock.
            builder.AddChaosOutcome(new ChaosOutcomeStrategyOptions<HttpResponseMessage>
            {
                InjectionRate = options.FaultInjectionRate,
                OutcomeGenerator = static _ => new ValueTask<Outcome<HttpResponseMessage>?>(
                    Outcome.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)))
            });
        });

        return httpClientBuilder;
    }
}
