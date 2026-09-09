using System.Reflection;
using Microsoft.Extensions.Diagnostics.Enrichment;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Attaches process-lifetime constants to every log record. Evaluated once at startup rather
/// than per record.
/// <para>
/// These same values are already on the OpenTelemetry <c>Resource</c> (see
/// <c>ConfigureOpenTelemetry</c>), which covers OTLP and Azure Monitor. They are repeated on the
/// record itself so that a log line stays self-describing in sinks that do not carry resource
/// attributes — the console during local debugging, and captured log output in tests.
/// </para>
/// </summary>
internal sealed class OrderSphereStaticLogEnricher(string serviceInstanceId, string buildVersion)
    : IStaticLogEnricher
{
    public OrderSphereStaticLogEnricher()
        : this(
            Environment.MachineName,
            Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion
            ?? "unknown")
    {
    }

    public void Enrich(IEnrichmentTagCollector collector)
    {
        collector.Add("service_instance_id", serviceInstanceId);
        collector.Add("build_version", buildVersion);
    }
}
