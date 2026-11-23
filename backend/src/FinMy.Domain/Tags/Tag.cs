using System.Text.RegularExpressions;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Domain.Tags;

/// <summary>
/// Tag (<c>tags</c>, 004 FR-001): rótulo livre. Quando <see cref="IsSettlement"/> é verdadeiro é uma Tag de acerto
/// (uma pessoa que deve ou recebe dinheiro). Nome único no Espaço sem diferenciar maiúsculas/minúsculas e acentos
/// (garantido por índice sobre <see cref="NameNormalized"/>); a regra de "uma só Tag de acerto por lançamento"
/// e a movimentação das ligações ao marcar/desmarcar ficam na aplicação (research R2, R7).
/// </summary>
public sealed partial class Tag : TenantEntity, IArchivable
{
    public const int NameMaxLength = 30;

    // Construtor para o EF Core.
    private Tag()
    {
    }

    private Tag(string name, string color, bool isSettlement)
    {
        SetName(name);
        SetColor(color);
        IsSettlement = isSettlement;
    }

    public string Name { get; private set; } = string.Empty;

    public string NameNormalized { get; private set; } = string.Empty;

    /// <summary>Cor no formato <c>#RRGGBB</c>.</summary>
    public string Color { get; private set; } = string.Empty;

    public bool IsSettlement { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    public static Tag Create(string name, string color, bool isSettlement = false) => new(name, color, isSettlement);

    public void Rename(string name) => SetName(name);

    public void ChangeColor(string color) => SetColor(color);

    public void MarkSettlement() => IsSettlement = true;

    public void UnmarkSettlement() => IsSettlement = false;

    public void Archive(DateTimeOffset now) => ArchivedAt ??= now;

    public void Unarchive() => ArchivedAt = null;

    private void SetName(string name)
    {
        Name = Rules.RequiredText(name, NameMaxLength, "name", "tag.name", "o nome da tag");
        NameNormalized = TextNormalizer.Normalize(Name);
    }

    private void SetColor(string color)
    {
        if (color is null || !ColorPattern().IsMatch(color))
        {
            throw Rules.Invalid("tag.color_invalid", "color", "Cor inválida. Use o formato #RRGGBB");
        }

        Color = color;
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorPattern();
}
