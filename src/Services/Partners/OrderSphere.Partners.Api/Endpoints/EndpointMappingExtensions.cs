using Asp.Versioning;

namespace OrderSphere.Partners.Api.Endpoints;

public static class EndpointMappingExtensions
{
    public static void MapPartnersEndpoints(this WebApplication app)
    {
        var versionSet = app.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1, 0))
            .ReportApiVersions()
            .Build();

        var v1 = app.MapGroup("api/v{version:apiVersion}")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(1.0)
            .MapToApiVersion(1.0);

        v1.MapAdminPartnerEndpoints();

        // Internal endpoints are mounted outside the versioned group — no gateway route
        // exposes them; only reachable from within the cluster.
        app.MapInternalPartnerEndpoints();
    }
}
