using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Cards;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Recurrences;
using FinMy.Domain.Tags;
using FinMy.Domain.Wallets;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Dinheiro = FinMy.Domain.Monetary.Money;

namespace FinMy.Api.Tests.Isolation;

/// <summary>Persistência da migração <c>Recurrences</c> (005 data-model.md): colunas, FKs compostas, CHECKs e unicidade da ocorrência.</summary>
public class RecurrencePersistenceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly DateOnly Hoje = new(2026, 3, 10);

    private async Task<Guid> NewTenantAsync()
    {
        var tenantId = Guid.CreateVersion7();
        await using var db = postgres.CreateContext(tenantId);
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO tenants (id, name, onboarding_steps, created_at, created_by, created_via, updated_at, updated_by, updated_via)
            VALUES ({tenantId}, 'Espaço de teste', jsonb_build_object(), now(), {tenantId}, 'system', now(), {tenantId}, 'system')
            """);
        return tenantId;
    }

    private static Wallet NewWallet() => Wallet.Create("Azul", WalletType.Checking, "#1E88E5", Dinheiro.Zero, Hoje);

    private static Recurrence NewExpense(Guid walletId) =>
        Recurrence.Create(
            RecurrenceKind.Expense, "Aluguel", Dinheiro.FromCents(150_000), 5, walletId, null, null,
            YearMonth.Of(2026, 3), RecurrenceEndType.AfterCount, null, 12, ReferenceMonthRule.NextMonth);

    private static Entry Occurrence(Guid walletId, Recurrence rec, YearMonth month)
    {
        var entry = Entry.Create(EntryKind.Expense, "Aluguel", Dinheiro.FromCents(150_000), month.FirstDay.AddDays(4), walletId, EntryStatus.Planned, month.Next(), null, Hoje);
        entry.LinkToRecurrence(rec.Id, month);
        return entry;
    }

    [Fact]
    public async Task Grava_recorrencia_tags_e_ocorrencia_com_o_Espaco_do_contexto()
    {
        var tenant = await NewTenantAsync();
        var wallet = NewWallet();
        var tag = Tag.Create("Casa", "#1E88E5");
        var rec = NewExpense(wallet.Id);
        rec.AdvanceGeneratedUntil(YearMonth.Of(2026, 5));
        var entry = Occurrence(wallet.Id, rec, YearMonth.Of(2026, 3));

        await using (var db = postgres.CreateContext(tenant))
        {
            db.AddRange(wallet, tag, rec, new RecurrenceTag(rec.Id, tag.Id), entry);
            await db.SaveChangesAsync();
        }

        await using var read = postgres.CreateContext(tenant);
        var saved = await read.Set<Recurrence>().SingleAsync();
        saved.TenantId.ShouldBe(tenant);
        saved.Kind.ShouldBe(RecurrenceKind.Expense);
        saved.EndYearMonth.ShouldBe(YearMonth.Of(2027, 2));
        saved.GeneratedUntil.ShouldBe(YearMonth.Of(2026, 5));
        saved.ReferenceMonthRule.ShouldBe(ReferenceMonthRule.NextMonth);
        (await read.Set<RecurrenceTag>().SingleAsync()).TenantId.ShouldBe(tenant);
        var occurrence = await read.Set<Entry>().SingleAsync();
        occurrence.RecurrenceId.ShouldBe(rec.Id);
        occurrence.RecurrenceMonth.ShouldBe(new DateOnly(2026, 3, 1));
        occurrence.RecurrenceModified.ShouldBeFalse();
    }

    [Fact]
    public async Task Recorrencia_e_invisivel_a_outro_Espaco()
    {
        var tenantA = await NewTenantAsync();
        var tenantB = await NewTenantAsync();
        var wallet = NewWallet();

        await using (var db = postgres.CreateContext(tenantA))
        {
            db.AddRange(wallet, NewExpense(wallet.Id));
            await db.SaveChangesAsync();
        }

        await using var asB = postgres.CreateContext(tenantB);
        (await asB.Set<Recurrence>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Ocorrencia_duplicada_no_mes_e_recusada_mas_excluida_libera_o_mes()
    {
        var tenant = await NewTenantAsync();
        var wallet = NewWallet();
        var rec = NewExpense(wallet.Id);
        var first = Occurrence(wallet.Id, rec, YearMonth.Of(2026, 3));

        await using (var db = postgres.CreateContext(tenant))
        {
            db.AddRange(wallet, rec, first);
            await db.SaveChangesAsync();
        }

        await using (var dup = postgres.CreateContext(tenant))
        {
            dup.Add(Occurrence(wallet.Id, rec, YearMonth.Of(2026, 3)));
            var ex = await Should.ThrowAsync<DbUpdateException>(() => dup.SaveChangesAsync());
            ((PostgresException)ex.InnerException!).SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        }

        await using (var del = postgres.CreateContext(tenant))
        {
            var toDelete = await del.Set<Entry>().SingleAsync();
            del.Remove(toDelete);
            await del.SaveChangesAsync();
        }

        await using var again = postgres.CreateContext(tenant);
        again.Add(Occurrence(wallet.Id, rec, YearMonth.Of(2026, 3)));
        await again.SaveChangesAsync();
    }

    [Fact]
    public async Task Compra_de_recorrencia_grava_origem_recurrence_e_a_ligacao()
    {
        var tenant = await NewTenantAsync();
        var wallet = NewWallet();
        var card = CreditCard.Create("Verde", "#43A047", 5, 15, wallet.Id);
        var invoice = Invoice.Create(card.Id, YearMonth.Of(2026, 4), new DateOnly(2026, 4, 5), new DateOnly(2026, 4, 15));
        var rec = Recurrence.Create(
            RecurrenceKind.CardPurchase, "Streaming", Dinheiro.FromCents(3_990), 20, null, card.Id, null,
            YearMonth.Of(2026, 3), RecurrenceEndType.None, null, null, null);
        var purchase = CardPurchase.CreateFromRecurrence(card.Id, invoice.Id, "Streaming", Dinheiro.FromCents(3_990), new DateOnly(2026, 3, 20), rec.Id, YearMonth.Of(2026, 3));

        await using (var db = postgres.CreateContext(tenant))
        {
            db.AddRange(wallet, card, invoice, rec, purchase);
            await db.SaveChangesAsync();
        }

        await using var read = postgres.CreateContext(tenant);
        var saved = await read.Set<CardPurchase>().SingleAsync();
        saved.Origin.ShouldBe(PurchaseOrigin.Recurrence);
        saved.RecurrenceId.ShouldBe(rec.Id);
        saved.RecurrenceMonth.ShouldBe(new DateOnly(2026, 3, 1));
    }

    [Fact]
    public async Task Check_recusa_recorrencia_de_entrada_sem_carteira()
    {
        var tenant = await NewTenantAsync();
        var wallet = NewWallet();
        var rec = NewExpense(wallet.Id);

        await using var db = postgres.CreateContext(tenant);
        db.AddRange(wallet, rec);
        await db.SaveChangesAsync();

        var ex = await Should.ThrowAsync<PostgresException>(() =>
            db.Database.ExecuteSqlAsync($"UPDATE recurrences SET wallet_id = NULL WHERE id = {rec.Id}"));
        ex.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }
}
