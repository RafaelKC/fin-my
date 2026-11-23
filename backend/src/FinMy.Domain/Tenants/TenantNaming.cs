namespace FinMy.Domain.Tenants;

/// <summary>Nome padrão do Espaço a partir do nome da pessoa (001 FR-004, research R10).</summary>
public static class TenantNaming
{
    public const string Prefix = "Finanças de ";

    /// <summary>Limite de caracteres do nome do Espaço (data-model: 1–60 após trim).</summary>
    public const int MaxLength = 60;

    /// <summary>
    /// "Finanças de &lt;primeiro nome&gt;": primeiro nome = trecho antes do primeiro espaço, após trim;
    /// se o total passar de 60 caracteres, o primeiro nome é truncado para caber.
    /// </summary>
    public static string DefaultName(string personName)
    {
        ArgumentNullException.ThrowIfNull(personName);

        var trimmed = personName.Trim();
        var end = trimmed.AsSpan().IndexOfAnyWhiteSpace();
        var firstName = end < 0 ? trimmed : trimmed[..end];

        var room = MaxLength - Prefix.Length;
        if (firstName.Length > room)
        {
            // Não corta no meio de um par substituto (emoji, por exemplo).
            var cut = char.IsHighSurrogate(firstName[room - 1]) ? room - 1 : room;
            firstName = firstName[..cut];
        }

        return Prefix + firstName;
    }

    private static int IndexOfAnyWhiteSpace(this ReadOnlySpan<char> text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
