using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Cards;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Tags;
using FinMy.Domain.Wallets;
using FinMy.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Dinheiro = FinMy.Domain.Monetary.Money;

namespace FinMy.Api.Tests.Isolation;

/// <summary>Persistência das tabelas de ligação e das colunas de acerto da 004 (data-model.md): Espaço, FKs compostas e unicidade.</summary>
public class TagLinksPersistenceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
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

    private static Entry NewEntry(Guid walletId) =>
        Entry.Create(EntryKind.Expense, "Presente Bruna", Dinheiro.FromCents(4000), Hoje, walletId, null, null, null, Hoje);

    [Fact]
    public async Task Grava_ligacao_com_o_Espaco_do_contexto_e_tag_de_acerto_nas_colunas()
    {
        var tenant = await NewTenantAsync();
        var bruna = Tag.Create("Bruna", "#E53935", isSettlement: true);
        var viagem = Tag.Create("Viagem", "#1E88E5");
        var wallet = Wallet.Create("Azul", WalletType.Checking, "#1E88E5", Dinheiro.Zero, Hoje);
        var entry = NewEntry(wallet.Id);
        entry.ChangeSettlementTag(bruna.Id);
        var splitGroup = Guid.CreateVersion7();
        entry.ChangeSplitGroup(splitGroup);

        await using (var db = postgres.CreateContext(tenant))
        {
            db.AddRange(bruna, viagem, wallet, entry, new EntryTag(entry.Id, viagem.Id));
            await db.SaveChangesAsync();
        }

        await using var read = postgres.CreateContext(tenant);
        var link = await read.Set<EntryTag>().SingleAsync();
        link.TenantId.ShouldBe(tenant);
        link.TagId.ShouldBe(viagem.Id);
        var saved = await read.Set<Entry>().SingleAsync();
        saved.SettlementTagId.ShouldBe(bruna.Id);
        saved.SplitGroupId.ShouldBe(splitGroup);
    }

    [Fact]
    public async Task Ligacao_e_invisivel_a_outro_Espaco()
    {
        var tenantA = await NewTenantAsync();
        var tenantB = await NewTenantAsync();
        var tag = Tag.Create("Viagem", "#1E88E5");
        var wallet = Wallet.Create("Azul", WalletType.Checking, "#1E88E5", Dinheiro.Zero, Hoje);
        var entry = NewEntry(wallet.Id);

        await using (var db = postgres.CreateContext(tenantA))
        {
            db.AddRange(tag, wallet, entry, new EntryTag(entry.Id, tag.Id));
            await db.SaveChangesAsync();
        }

        await using var asB = postgres.CreateContext(tenantB);
        (await asB.Set<EntryTag>().CountAsync()).ShouldBe(0);
        await using var semEspaco = postgres.CreateContext(new CurrentContext());
        (await semEspaco.Set<EntryTag>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Banco_recusa_ligar_lancamento_a_tag_de_outro_Espaco()
    {
        var tenantA = await NewTenantAsync();
        var tenantB = await NewTenantAsync();
        var tagB = Tag.Create("Viagem", "#1E88E5");
        await using (var db = postgres.CreateContext(tenantB))
        {
            db.Add(tagB);
            await db.SaveChangesAsync();
        }

        var wallet = Wallet.Create("Azul", WalletType.Checking, "#1E88E5", Dinheiro.Zero, Hoje);
        var entry = NewEntry(wallet.Id);
        await using var asA = postgres.CreateContext(tenantA);
        asA.AddRange(wallet, entry, new EntryTag(entry.Id, tagB.Id));

        await Should.ThrowAsync<DbUpdateException>(() => asA.SaveChangesAsync());
    }

    [Fact]
    public async Task Ligacao_de_compra_nao_repete_a_mesma_tag()
    {
        var tenant = await NewTenantAsync();
        var tag = Tag.Create("Viagem", "#1E88E5");
        var wallet = Wallet.Create("Azul", WalletType.Checking, "#1E88E5", Dinheiro.Zero, Hoje);
        var card = CreditCard.Create("Verde", "#43A047", 5, 15, wallet.Id);
        var invoice = Invoice.Create(card.Id, YearMonth.Of(2026, 3), new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 15));
        var purchase = CardPurchase.CreateManual(card.Id, invoice.Id, "Uber", Dinheiro.FromCents(1272), PurchaseNature.Debit, Hoje, null);

        await using (var db = postgres.CreateContext(tenant))
        {
            db.AddRange(tag, wallet, card, invoice, purchase, new CardPurchaseTag(purchase.Id, tag.Id));
            await db.SaveChangesAsync();
        }

        await using var again = postgres.CreateContext(tenant);
        again.Add(new CardPurchaseTag(purchase.Id, tag.Id));
        await Should.ThrowAsync<DbUpdateException>(() => again.SaveChangesAsync());
    }

    [Fact]
    public async Task Nome_de_tag_e_unico_no_Espaco_sem_diferenciar_caixa_e_acento()
    {
        var tenant = await NewTenantAsync();
        await using (var db = postgres.CreateContext(tenant))
        {
            db.Add(Tag.Create("Léo", "#1E88E5"));
            await db.SaveChangesAsync();
        }

        await using var again = postgres.CreateContext(tenant);
        again.Add(Tag.Create("leo", "#43A047"));
        await Should.ThrowAsync<DbUpdateException>(() => again.SaveChangesAsync());
    }
}
