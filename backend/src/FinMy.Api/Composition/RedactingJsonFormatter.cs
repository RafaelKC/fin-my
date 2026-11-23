using System.Text.RegularExpressions;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;

namespace FinMy.Api.Composition;

/// <summary>
/// JSON compacto de uma linha por evento, com endereços de e-mail substituídos por <c>[email]</c> em qualquer
/// parte (mensagem, propriedades, exceção). Defesa em profundidade: o código também não deve logá-los.
/// </summary>
public sealed partial class RedactingJsonFormatter : ITextFormatter
{
    public const string Replacement = "[email]";

    private readonly RenderedCompactJsonFormatter _inner = new();

    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(output);

        using var buffer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        _inner.Format(logEvent, buffer);
        output.Write(Redact(buffer.ToString()));
    }

    public static string Redact(string text) => EmailRegex().Replace(text, Replacement);

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)+")]
    private static partial Regex EmailRegex();
}
