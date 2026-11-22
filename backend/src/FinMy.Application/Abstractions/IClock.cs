namespace FinMy.Application.Abstractions;

/// <summary>Relógio da aplicação. Único ponto de acesso a "agora"; nunca usar <c>DateTime.Now</c>.</summary>
public interface IClock
{
    /// <summary>Instante atual em UTC.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Data de hoje em America/Sao_Paulo (RT-03).</summary>
    DateOnly Today { get; }
}
