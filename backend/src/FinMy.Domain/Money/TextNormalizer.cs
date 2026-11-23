using System.Globalization;
using System.Text;

namespace FinMy.Domain.Monetary;

/// <summary>Normalização de texto para comparação e busca: sem acento, minúsculo, sem espaços nas pontas.</summary>
public static class TextNormalizer
{
    /// <summary>
    /// Remove espaços nas pontas, passa para minúsculas (cultura invariante) e remove diacríticos
    /// (FormD). Espaços internos são preservados. Ex.: "  Ação Ç " → "acao c".
    /// </summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var decomposed = text.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
