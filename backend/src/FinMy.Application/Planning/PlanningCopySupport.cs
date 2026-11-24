using FinMy.Domain.Common;
using FinMy.Domain.Entries;

namespace FinMy.Application.Planning;

/// <summary>Regras compartilhadas por <see cref="GetPlanningCopyCandidatesHandler"/> e <see cref="CopyPlanningHandler"/> (007 research R4).</summary>
internal static class PlanningCopySupport
{
    private const int FirstYear = 2000;
    private const int LastYear = 2100;

    /// <summary>O ano da rota tem de estar em 2000–2100 (como a grade); o anterior é só leitura e pode ser 1999.</summary>
    public static int ParseYear(string? text)
    {
        if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var year)
            || year is < FirstYear or > LastYear)
        {
            throw new DomainException(PlanningErrors.Field("year.invalid", "year", "Use um ano entre 2000 e 2100"));
        }

        return year;
    }

    /// <summary>Chave da linha candidata: tipo, descrição normalizada e, nas partes de divisão, a tag de acerto (research R4).</summary>
    public static string Key(EntryKind kind, string descriptionNormalized, Guid? settlementTagId) =>
        $"{PlanningKinds.Text(kind)}:{descriptionNormalized}" + (settlementTagId is { } tag ? $":{tag}" : string.Empty);

    public static string Key(PlanningEntry e) => Key(e.Kind, e.DescriptionNormalized, e.SettlementTagId);

    /// <summary>Lançamentos do ano anterior que podem ser copiados: as ocorrências de recorrência ficam de fora (US3.3).</summary>
    public static IEnumerable<PlanningEntry> Copyable(PlanningData previous) => previous.Entries.Where(e => e.RecurrenceId is null);
}
