using System.Text.Json;
using FinMy.Application.Cards;
using FinMy.Domain.Common;
using ModelContextProtocol.Protocol;

namespace FinMy.Api.Mcp;

/// <summary>
/// Objeto de resposta de ferramenta com valores em centavos <b>e</b> formatados (008 contracts, FR-009):
/// <c>Money("total", 3290)</c> grava <c>totalCents</c> e <c>totalText</c> (<c>"R$ 32,90"</c>), para o Claude não errar a formatação.
/// </summary>
public sealed class McpObject : Dictionary<string, object?>
{
    public McpObject Set(string name, object? value)
    {
        this[name] = value;
        return this;
    }

    public McpObject Money(string name, long cents)
    {
        this[name + "Cents"] = cents;
        this[name + "Text"] = InvoiceErrors.FormatBrl(cents);
        return this;
    }
}

/// <summary>Resultado das ferramentas: JSON no texto; erro de regra de negócio vira <c>isError</c> com <c>{ code, message, ... }</c> (research R4).</summary>
public static class McpResult
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static CallToolResult Ok(object value) => new() { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(value, Json) }] };

    public static CallToolResult Error(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var body = new McpObject().Set("code", error.Code).Set("message", error.Message);
        if (error.Extensions is { } extensions)
        {
            foreach (var (key, value) in extensions)
            {
                body.Set(key, value);
            }
        }

        if (!body.ContainsKey("field") && error.FieldErrors is { Count: > 0 })
        {
            body.Set("field", error.FieldErrors.Keys.First());
        }

        return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = JsonSerializer.Serialize(body, Json) }] };
    }

    /// <summary>Executa a ferramenta, traduzindo <see cref="DomainException"/> no erro de ferramenta.</summary>
    public static async Task<CallToolResult> RunAsync(Func<Task<object>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        try
        {
            return Ok(await work());
        }
        catch (DomainException ex)
        {
            return Error(ex.Error);
        }
    }
}
