namespace OrderSphere.Partners.Api;

/// <summary>
/// Public entry-point marker for <c>WebApplicationFactory&lt;ApiMarker&gt;</c> in the integration tests.
/// The generated top-level <c>Program</c> is internal and ambiguous across the referenced API
/// assemblies, so each API exposes its own marker to identify its host assembly unambiguously.
/// </summary>
public sealed class ApiMarker;
