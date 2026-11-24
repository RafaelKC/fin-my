using FinMy.Application.Abstractions;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Persistence;

/// <summary>Faturas do Espaço do contexto; a linha nasce sob demanda por um upsert atômico (research R2).</summary>
public sealed class InvoiceRepository(FinMyDbContext db, IClock clock) : IInvoiceRepository
{
    public async Task<Invoice> GetOrCreateAsync(Guid creditCardId, YearMonth dueMonth, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(creditCardId, dueMonth, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var card = await db.Set<CreditCard>().AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == creditCardId, cancellationToken)
            ?? throw new InvalidOperationException($"O cartão {creditCardId} não existe neste Espaço.");

        var context = db.CurrentContext;
        var tenantId = context.TenantId ?? throw new MissingTenantContextException();
        var by = context.UserId ?? (context.IsSystem ? SystemUser.Id : throw new MissingUserContextException());
        var via = context.Channel.ToString().ToLowerInvariant();
        var now = clock.UtcNow;
        var closing = InvoiceCycle.ClosingDate(dueMonth, card.ClosingDay, card.DueDay);
        var due = InvoiceCycle.DueDate(dueMonth, card.DueDay);
        var month = dueMonth.FirstDay;

        // O alvo do ON CONFLICT inclui tenant_id (data-model.md); perder a corrida não é erro.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO invoices (id, tenant_id, credit_card_id, due_month, closing_date, due_date,
                created_at, created_by, created_via, updated_at, updated_by, updated_via)
            VALUES ({Guid.CreateVersion7()}, {tenantId}, {creditCardId}, {month}, {closing}, {due},
                {now}, {by}, {via}, {now}, {by}, {via})
            ON CONFLICT (tenant_id, credit_card_id, due_month) DO NOTHING
            """,
            cancellationToken);

        return await FindAsync(creditCardId, dueMonth, cancellationToken)
            ?? throw new InvalidOperationException("A fatura não foi encontrada depois de criada.");
    }

    public Task<Invoice?> FindAsync(Guid creditCardId, YearMonth dueMonth, CancellationToken cancellationToken)
    {
        var month = dueMonth.FirstDay;
        return db.Set<Invoice>().SingleOrDefaultAsync(
            i => i.CreditCardId == creditCardId && i.DueMonth == month,
            cancellationToken);
    }
}
