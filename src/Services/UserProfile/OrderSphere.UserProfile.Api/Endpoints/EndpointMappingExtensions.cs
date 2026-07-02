using Asp.Versioning;

namespace OrderSphere.UserProfile.Api.Endpoints;

public static class EndpointMappingExtensions
{
    public static void MapUserProfileEndpoints(this WebApplication app)
    {
        var versionSet = app.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1, 0))
            .ReportApiVersions()
            .Build();

        var v1 = app.MapGroup("api/v{version:apiVersion}")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(1.0)
            .MapToApiVersion(1.0);

        v1.MapProfileEndpoints();
        v1.MapAdminProfileEndpoints();
        v1.MapAdminTenantEndpoints();

        // Internal endpoints are mounted outside the versioned group — no auth required
        // since they are only reachable from within the cluster (not exposed via the gateway).
        app.MapInternalProfileEndpoints();
    }
}
