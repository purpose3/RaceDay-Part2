using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace RaceDay.Api.Swagger;

/// <summary>
/// Marks only the protected endpoints with the Bearer lock in Swagger UI and makes sure
/// they document the 401 (no/invalid token) and 403 (wrong role) responses.
/// Public endpoints stay lock-free so it is obvious which ones need the Authorize button.
/// </summary>
public class AuthOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        var requiresAuth = metadata.OfType<IAuthorizeData>().Any();
        var allowsAnonymous = metadata.OfType<IAllowAnonymous>().Any();
        if (!requiresAuth || allowsAnonymous) return;

        operation.Security = new List<OpenApiSecurityRequirement>
        {
            new()
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                }] = Array.Empty<string>()
            }
        };

        operation.Responses.TryAdd("401", new OpenApiResponse
        {
            Description = "Missing, invalid or expired token. Log in and use the Authorize button."
        });
        operation.Responses.TryAdd("403", new OpenApiResponse
        {
            Description = "Authenticated, but your role (or ownership of the resource) does not allow this."
        });
    }
}
