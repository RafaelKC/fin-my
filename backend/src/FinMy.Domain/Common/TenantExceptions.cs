namespace FinMy.Domain.Common;

/// <summary>Gravação de entidade de Espaço sem Espaço no contexto (docs/arquitetura.md §3.4).</summary>
public sealed class MissingTenantContextException()
    : InvalidOperationException("Não há Espaço no contexto atual; a gravação de dados de Espaço foi recusada.");

/// <summary>Tentativa de gravar, alterar ou excluir dado de um Espaço diferente do contexto (docs/arquitetura.md §3.4).</summary>
public sealed class CrossTenantWriteException(string message) : InvalidOperationException(message);

/// <summary>Gravação auditada sem usuário (nem escopo de sistema) no contexto.</summary>
public sealed class MissingUserContextException()
    : InvalidOperationException("Não há usuário no contexto atual; a gravação foi recusada.");
