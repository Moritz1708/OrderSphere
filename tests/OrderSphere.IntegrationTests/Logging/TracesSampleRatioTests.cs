using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace OrderSphere.IntegrationTests.Logging;

/// <summary>
/// The sampler ratio is read from configuration in ConfigureOpenTelemetry. Configuration values
/// are culture-invariant, so parsing them with the ambient culture is wrong: on a de-DE host
/// "1.0" parses as 10 and "0.1" as 1, both outside the sampler's [0,1] range, which throws at
/// startup. This was latent until the key was actually set in appsettings.
/// </summary>
public sealed class TracesSampleRatioTests
{
    private static IHost BuildHost(CultureInfo culture, string? ratio)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            var builder = Host.CreateApplicationBuilder();
            if (ratio is not null)
            {
                builder.Configuration.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["OpenTelemetry:TracesSampleRatio"] = ratio });
            }

            builder.AddServiceDefaults();
            return builder.Build();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("de-DE", "1.0")]
    [InlineData("de-DE", "0.1")]
    [InlineData("en-US", "0.1")]
    [InlineData("fr-FR", "0.05")]
    public void Ratio_is_parsed_culture_invariantly(string cultureName, string ratio)
    {
        var act = () => BuildHost(CultureInfo.GetCultureInfo(cultureName), ratio).Dispose();

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("2.5")]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    public void An_out_of_range_or_unparsable_ratio_does_not_crash_the_host(string ratio)
    {
        // A typo in configuration must degrade sampling, not take the service down at startup.
        var act = () => BuildHost(CultureInfo.InvariantCulture, ratio).Dispose();

        act.Should().NotThrow();
    }
}
