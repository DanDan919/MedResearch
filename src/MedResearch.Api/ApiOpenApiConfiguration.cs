using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MedResearch.Api.Research;
using MedResearch.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MedResearch.Api;

internal static class ApiOpenApiConfiguration
{
    public static void Configure(OpenApiOptions options)
    {
        options.AddSchemaTransformer((schema, context, cancellationToken) =>
        {
            var type = context.JsonTypeInfo.Type;
            var numeric = Nullable.GetUnderlyingType(type) ?? type;
            // ASP.NET accepts quoted numbers on input, but these numeric response values serialize as JSON numbers.
            if (numeric == typeof(int) || numeric == typeof(long) || numeric == typeof(double)
                || numeric == typeof(float) || numeric == typeof(decimal))
                schema.Type &= ~JsonSchemaType.String;
            if (schema.Properties is not null && type.Namespace?.StartsWith("MedResearch", StringComparison.Ordinal) == true)
            {
                // Response serializers emit nullable fields too; absent is not the wire contract.
                schema.Required ??= new HashSet<string>();
                schema.Required.UnionWith(schema.Properties.Keys);
                if ((type == typeof(CreateResearchResponse) || type == typeof(ResearchRunResponse)
                    || type == typeof(ResearchRunSummaryResponse) || type == typeof(ResearchRunProgressResponse)
                    || type == typeof(ResearchProvenanceResponse)) && schema.Properties.TryGetValue("status", out var status))
                {
                    if (status is OpenApiSchema mutableStatus)
                        mutableStatus.Enum = Enum.GetNames<ResearchRunStatus>().Select(name => (JsonNode)JsonValue.Create(name)!).ToList();
                }
            }
            return Task.CompletedTask;
        });
        options.AddDocumentTransformer((document, context, cancellationToken) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
            { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" };
            foreach (var description in context.DescriptionGroups.SelectMany(group => group.Items))
            {
                if (!description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any()) continue;
                var route = Regex.Replace("/" + description.RelativePath?.TrimEnd('/'), @"\{([^}:]+):[^}]+\}", "{$1}");
                if (!document.Paths.TryGetValue(route, out var path)) continue;
                if (path.Operations is null) continue;
                foreach (var operation in path.Operations.Where(item => item.Key.Method.Equals(description.HttpMethod, StringComparison.OrdinalIgnoreCase)))
                {
                    operation.Value.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
                    operation.Value.Responses ??= new OpenApiResponses();
                    operation.Value.Responses.TryAdd("401", new OpenApiResponse { Description = "Authentication required" });
                    operation.Value.Responses.TryAdd("403", new OpenApiResponse { Description = "Invalid authenticated subject" });
                    operation.Value.Responses.TryAdd("503", new OpenApiResponse { Description = "Research database temporarily unavailable" });
                }
            }
            return Task.CompletedTask;
        });
    }
}
