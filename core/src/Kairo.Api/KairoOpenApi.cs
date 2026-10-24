using System.Text.RegularExpressions;
using Microsoft.OpenApi;

namespace Kairo.Api;

public static partial class KairoOpenApi
{
    public static IServiceCollection AddKairoOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Servers = [];
                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, _) =>
            {
                if (context.Description.RelativePath?.EndsWith("/media-accessibility/described", StringComparison.Ordinal) == true)
                {
                    operation.RequestBody = new OpenApiRequestBody
                    {
                        Required = true,
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["video/mp4"] = new() { Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" } },
                        },
                    };
                }

                foreach (Match match in RouteParameter().Matches(context.Description.RelativePath ?? ""))
                {
                    var name = match.Groups["name"].Value;
                    operation.Parameters ??= [];
                    if (operation.Parameters.Any(parameter => parameter.Name == name && parameter.In == ParameterLocation.Path))
                    {
                        continue;
                    }

                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = name,
                        In = ParameterLocation.Path,
                        Required = true,
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    });
                }

                return Task.CompletedTask;
            });
        });

    [GeneratedRegex(@"\{(?<name>[A-Za-z0-9_]+)")]
    private static partial Regex RouteParameter();
}
