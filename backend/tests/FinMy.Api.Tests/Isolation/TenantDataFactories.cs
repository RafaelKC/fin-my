using FinMy.Application.Tests.Infrastructure;

namespace FinMy.Api.Tests.Isolation;

/// <summary>
/// Fábricas de dados por tipo <c>ITenantEntity</c> (docs/arquitetura.md §3.7). A suíte descobre por reflexão
/// todos os tipos de Espaço mapeados e <b>falha</b> se algum não tiver fábrica aqui — quem criar uma entidade
/// de Espaço nas próximas features acrescenta a fábrica dela neste dicionário.
/// Nesta feature (001) só existem as sondas do contexto de teste (research R1).
/// </summary>
public static class TenantDataFactories
{
    /// <summary>
    /// Para cada tipo, devolve as instâncias a gravar (o próprio tipo e o que ele exige, como o pai de uma FK composta).
    /// O <c>label</c> distingue os dados de cada Espaço.
    /// </summary>
    public static IReadOnlyDictionary<Type, Func<string, IReadOnlyList<object>>> ByType { get; } =
        new Dictionary<Type, Func<string, IReadOnlyList<object>>>
        {
            [typeof(ProbeParent)] = label => [new ProbeParent { Name = label }],
            [typeof(ProbeNamed)] = label => [new ProbeNamed { Name = label }],
            [typeof(ProbeItem)] = label =>
            {
                var parent = new ProbeParent { Name = label };
                return [parent, new ProbeItem { ParentId = parent.Id, Label = label }];
            },
        };
}
