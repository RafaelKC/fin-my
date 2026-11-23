using System.Text.Json.Nodes;

namespace FinMy.Api.Tests.Isolation;

/// <summary>Parâmetro de uma operação do OpenAPI gerado.</summary>
public sealed record OperationParameter(string Name, string In, bool Required, JsonNode? Schema);

/// <summary>Uma operação (método + rota) do documento OpenAPI gerado pela API.</summary>
public sealed record OpenApiOperation(string Method, string Path, IReadOnlyList<OperationParameter> Parameters, JsonNode? BodySchema)
{
    /// <summary>Chave usada em <see cref="OperationSamples"/>, p.ex. <c>POST /api/v1/auth/login</c>.</summary>
    public string Key => $"{Method} {Path}";

    public override string ToString() => Key;
}

/// <summary>Lê as operações do documento OpenAPI (<c>/openapi/v1.json</c>) que a própria API gera.</summary>
public static class OpenApiOperations
{
    private static readonly string[] Methods = ["get", "put", "post", "delete", "patch"];

    public static IReadOnlyList<OpenApiOperation> Read(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        var operations = new List<OpenApiOperation>();

        foreach (var (path, item) in root["paths"]!.AsObject())
        {
            var shared = ParametersOf(root, item!["parameters"]);

            foreach (var method in Methods)
            {
                if (item[method] is not JsonObject operation)
                {
                    continue;
                }

                var parameters = shared.Concat(ParametersOf(root, operation["parameters"])).ToList();
                var body = operation["requestBody"]?["content"]?["application/json"]?["schema"];
                operations.Add(new OpenApiOperation(method.ToUpperInvariant(), path, parameters, Resolve(root, body)));
            }
        }

        return operations;
    }

    /// <summary>Segue <c>$ref</c> locais (<c>#/components/schemas/X</c>) até o esquema concreto.</summary>
    public static JsonNode? Resolve(JsonObject root, JsonNode? schema)
    {
        while (schema?["$ref"]?.GetValue<string>() is { } reference)
        {
            JsonNode? target = root;
            foreach (var part in reference.TrimStart('#', '/').Split('/'))
            {
                target = target?[part];
            }

            schema = target;
        }

        return schema;
    }

    private static List<OperationParameter> ParametersOf(JsonObject root, JsonNode? array) =>
        (array?.AsArray() ?? [])
            .Select(p => Resolve(root, p)!)
            .Select(p => new OperationParameter(
                p["name"]!.GetValue<string>(),
                p["in"]!.GetValue<string>(),
                p["required"]?.GetValue<bool>() ?? false,
                Resolve(root, p["schema"])))
            .ToList();
}
