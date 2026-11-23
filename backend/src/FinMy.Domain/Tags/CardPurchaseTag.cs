using FinMy.Domain.Common;

namespace FinMy.Domain.Tags;

/// <summary>Ligação Compra ↔ Tag comum (<c>card_purchase_tags</c>). Só aceita tags com <c>is_settlement = false</c> (validado na aplicação).</summary>
public sealed class CardPurchaseTag : ITenantLink
{
    // Construtor para o EF Core.
    private CardPurchaseTag()
    {
    }

    public CardPurchaseTag(Guid cardPurchaseId, Guid tagId)
    {
        CardPurchaseId = cardPurchaseId;
        TagId = tagId;
    }

    /// <summary>Preenchido pelo interceptor de gravação.</summary>
    public Guid TenantId { get; internal set; }

    public Guid CardPurchaseId { get; private set; }

    public Guid TagId { get; private set; }
}
