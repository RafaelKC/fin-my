namespace FinMy.Domain.Common;

/// <summary>Usuário de sistema usado em jobs e escopos internos. Não existe na tabela <c>users</c>.</summary>
public static class SystemUser
{
    public static readonly Guid Id = new("00000000-0000-0000-0000-000000000001");
}
