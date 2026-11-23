using FinMy.Api.Tests.Support;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Transfers;
using FinMy.Domain.Wallets;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Api.Tests.Months;

/// <summary>
/// Cria carteiras, lançamentos e transferências direto pelo <c>DbContext</c> no Espaço de uma conta de teste
/// (as APIs de escrita são de outras tarefas). Os objetos de domínio passam pelas mesmas regras de sempre.
/// </summary>
internal static class MonthSeeding
{
    public static async Task SeedAsync(this ApiFactory factory, TestAccount account, params object[] rows)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentContext>()
            .Populate(account.Session.User.Id, account.Session.Tenant.Id, Channel.App);
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();

        // Um SaveChanges por linha preserva a ordem de criação (RT-12).
        foreach (var row in rows)
        {
            db.Add(row);
            await db.SaveChangesAsync();
        }
    }

    public static Wallet NewWallet(string name, long initialCents, DateOnly initialDate, bool primary = false) =>
        Wallet.Create(name, WalletType.Checking, "#1E88E5", Money.FromCents(initialCents), initialDate, primary);

    public static Entry NewEntry(
        EntryKind kind,
        string description,
        long cents,
        DateOnly date,
        Guid walletId,
        EntryStatus status,
        YearMonth? referenceMonth = null) =>
        Entry.Create(kind, description, Money.FromCents(cents), date, walletId, status, referenceMonth, null, date);

    public static Transfer NewTransfer(Guid from, Guid to, long cents, DateOnly date, string? description = null) =>
        Transfer.Create(from, to, Money.FromCents(cents), date, description, null);
}
