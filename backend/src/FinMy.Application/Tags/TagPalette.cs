namespace FinMy.Application.Tags;

/// <summary>Paleta de 10 cores das tags (research R2); a cor padrão é a próxima da sequência.</summary>
public static class TagPalette
{
    public static readonly IReadOnlyList<string> Colors =
    [
        "#E53935", "#1E88E5", "#43A047", "#FB8C00", "#8E24AA",
        "#00ACC1", "#F4511E", "#6D4C41", "#3949AB", "#C0CA33",
    ];

    public static string Next(int existingCount) => Colors[existingCount % Colors.Count];
}
