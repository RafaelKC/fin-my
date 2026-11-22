namespace FinMy.Domain.Common;

/// <summary>Entidade com identificador UUID v7 gerado pela aplicação.</summary>
public interface IEntity
{
    Guid Id { get; }
}
