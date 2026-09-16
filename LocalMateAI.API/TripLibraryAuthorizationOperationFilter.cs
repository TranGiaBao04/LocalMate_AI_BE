using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LocalMateAI.API;

public sealed class TripLibraryAuthorizationOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var route = context.ApiDescription.RelativePath;
        if (!string.Equals(route, "api/trips/save", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(route, "api/trips/my-trips", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
            }
        ];
    }
}
