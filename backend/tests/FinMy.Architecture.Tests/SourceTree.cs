using System.Text.RegularExpressions;

namespace FinMy.Architecture.Tests;

/// <summary>Acesso aos arquivos-fonte de produção (<c>backend/src</c>) para as regras baseadas em texto.</summary>
internal static partial class SourceTree
{
    public static string BackendRoot { get; } = FindBackendRoot();

    public static string SrcRoot => Path.Combine(BackendRoot, "src");

    /// <summary>Arquivos <c>.cs</c> de produção (sem <c>bin/</c> e <c>obj/</c>), com caminho relativo a <c>backend/src</c> usando <c>/</c>.</summary>
    public static IEnumerable<(string RelativePath, string Code)> ProductionFiles()
    {
        foreach (var file in Directory.EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(SrcRoot, file).Replace('\\', '/');
            if (relative.Contains("/obj/") || relative.Contains("/bin/"))
            {
                continue;
            }

            yield return (relative, StripComments(File.ReadAllText(file)));
        }
    }

    /// <summary>Remove comentários de linha e de bloco, para que a documentação possa citar os símbolos proibidos.</summary>
    public static string StripComments(string code) => CommentRegex().Replace(code, " ");

    private static string FindBackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FinMy.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("backend/FinMy.slnx não encontrado a partir de " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"//[^\r\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();
}
