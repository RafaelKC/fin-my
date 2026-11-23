using System.Text.Json;

namespace FinMy.Domain.Tests.Money;

/// <summary>Lê os vetores compartilhados de <c>shared/test-vectors/</c> (caminho relativo à raiz do repositório).</summary>
internal static class TestVectors
{
    public sealed record Case(string Id, JsonElement Input, JsonElement Expected);

    public static IReadOnlyList<Case> Load(string fileName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(VectorsDirectory(), fileName)));
        var cases = document.RootElement.GetProperty("cases").EnumerateArray()
            .Select(c => new Case(
                c.GetProperty("id").GetString()!,
                c.GetProperty("input").Clone(),
                c.GetProperty("expected").Clone()))
            .ToList();

        cases.ShouldNotBeEmpty();
        cases.Select(c => c.Id).ShouldBeUnique($"ids duplicados em {fileName}");
        return cases;
    }

    public static TheoryData<string> Ids(string fileName)
    {
        var data = new TheoryData<string>();
        foreach (var c in Load(fileName))
        {
            data.Add(c.Id);
        }

        return data;
    }

    public static Case Get(string fileName, string id) => Load(fileName).Single(c => c.Id == id);

    private static string VectorsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "shared", "test-vectors");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("shared/test-vectors não encontrado acima de " + AppContext.BaseDirectory);
    }

    private static void ShouldBeUnique(this IEnumerable<string> ids, string message)
    {
        var list = ids.ToList();
        list.Distinct().Count().ShouldBe(list.Count, message);
    }
}
