using System.Text.Json.Nodes;

namespace FinMy.Api.Tests.Isolation;

/// <summary>Gera um valor plausível a partir de um esquema OpenAPI; campos <c>uuid</c> recebem o id informado (de A).</summary>
public static class SchemaSampler
{
    public static JsonNode? Sample(JsonObject root, JsonNode? schema, Guid idOfA, string name = "")
    {
        schema = OpenApiOperations.Resolve(root, schema);
        if (schema is null)
        {
            return "x";
        }

        var type = TypeOf(schema);
        var format = schema["format"]?.GetValue<string>();

        switch (type)
        {
            case "object":
                var result = new JsonObject();
                foreach (var (property, child) in schema["properties"]?.AsObject() ?? [])
                {
                    result[property] = Sample(root, child, idOfA, property);
                }

                return result;
            case "array":
                return new JsonArray(Sample(root, schema["items"], idOfA, name));
            case "integer":
            case "number":
                return 1;
            case "boolean":
                return false;
            default:
                if (format == "uuid" || name.EndsWith("Id", StringComparison.Ordinal))
                {
                    return idOfA.ToString();
                }

                return name.Contains("mail", StringComparison.OrdinalIgnoreCase) ? SampleContext.NewEmail() : "x";
        }
    }

    /// <summary>O OpenAPI 3.1 permite <c>type</c> como lista (<c>["null","string"]</c>): fica o primeiro que não é nulo.</summary>
    private static string TypeOf(JsonNode schema) => schema["type"] switch
    {
        JsonArray list => list.Select(t => t!.GetValue<string>()).FirstOrDefault(t => t != "null") ?? "string",
        JsonValue value => value.GetValue<string>(),
        _ => schema["properties"] is not null ? "object" : "string",
    };
}
