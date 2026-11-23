namespace FinMy.Application.Abstractions;

/// <summary>
/// Executa trabalho em segundo plano uma vez por Espaço (docs/arquitetura.md §3.1): para cada Espaço abre um
/// escopo de DI próprio e entra em <see cref="ICurrentContext.BeginSystemScope"/>, de modo que filtros e
/// interceptors se comportam como numa requisição (<c>*_via = system</c>, <c>*_by = SystemUser.Id</c>).
/// </summary>
public interface ITenantJobRunner
{
    /// <summary>
    /// Chama <paramref name="work"/> para cada Espaço, com o <see cref="IServiceProvider"/> do escopo daquele Espaço.
    /// A falha de um Espaço não impede os demais; as falhas são reunidas num <see cref="AggregateException"/> ao final.
    /// </summary>
    Task ForEachTenantAsync(Func<IServiceProvider, CancellationToken, Task> work, CancellationToken cancellationToken = default);
}
