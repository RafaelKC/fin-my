using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FinMy.Infrastructure.Persistence.Configurations;

/// <summary>Grava enums como texto em <c>snake_case</c> (ex.: <c>ReuseDetected</c> → <c>reuse_detected</c>).</summary>
internal sealed partial class SnakeCaseEnumConverter<TEnum>()
    : ValueConverter<TEnum, string>(v => ToText(v), v => Parse(v))
    where TEnum : struct, Enum
{
    /// <summary>Valores aceitos no banco, para as restrições <c>CHECK</c>.</summary>
    public static IReadOnlyList<string> Values { get; } = Enum.GetValues<TEnum>().Select(ToText).ToList();

    /// <summary>Lista SQL <c>'a', 'b'</c> dos valores aceitos.</summary>
    public static string SqlList => string.Join(", ", Values.Select(v => $"'{v}'"));

    private static string ToText(TEnum value) => Separator().Replace(value.ToString(), "$1_$2").ToLowerInvariant();

    private static TEnum Parse(string text) =>
        Enum.GetValues<TEnum>().First(v => ToText(v) == text);

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex Separator();
}
