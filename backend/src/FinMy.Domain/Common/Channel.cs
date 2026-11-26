namespace FinMy.Domain.Common;

/// <summary>Canal pelo qual uma alteração foi feita (gravado em <c>created_via</c>/<c>updated_via</c>).</summary>
public enum Channel
{
    App,
    Claude,
    System,

    /// <summary>App Android (009/010): widgets leem; o relógio só faz lançamento rápido e marca como pago/recebido.</summary>
    AndroidWidget,
}

/// <summary>Texto estável de cada canal (<c>app</c>, <c>claude</c>, <c>system</c>, <c>android_widget</c>), usado em claims, colunas e respostas.</summary>
public static class ChannelCodes
{
    public static string ToCode(this Channel channel) => channel switch
    {
        Channel.AndroidWidget => "android_widget",
        _ => channel.ToString().ToLowerInvariant(),
    };

    /// <summary>Lê o texto gravado (<see cref="ToCode"/>); desconhecido → <c>false</c>.</summary>
    public static bool TryParse(string? code, out Channel channel)
    {
        foreach (var candidate in Enum.GetValues<Channel>())
        {
            if (string.Equals(candidate.ToCode(), code, StringComparison.OrdinalIgnoreCase))
            {
                channel = candidate;
                return true;
            }
        }

        channel = default;
        return false;
    }

    /// <summary>Como <see cref="TryParse"/>, mas lança para um texto desconhecido (leitura da coluna).</summary>
    public static Channel Parse(string code)
        => TryParse(code, out var channel) ? channel : throw new ArgumentException($"Canal desconhecido: {code}", nameof(code));
}
