using System.Text.Json;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;
using FinMy.Domain.Wallets;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Api.Tests.Entries;

/// <summary>Relógio com "agora" real (os tokens são validados nele) e "hoje" fixo, para as regras de data serem determinísticas.</summary>
public sealed class FixedTodayClock(DateOnly today) : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly Today { get; } = today;
}

/// <summary>Apoio dos testes de lançamentos: carteiras criadas direto no banco (a API de carteiras é outra tarefa) e leitura de respostas.</summary>
internal static class EntryTestSupport
{
    public static async Task<Guid> SeedWalletAsync(this ApiFactory factory, TestAccount owner, string name, bool archived = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        Populate(scope, owner);
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();

        var wallet = Wallet.Create(name, WalletType.Checking, "#1E88E5", Money.Zero, new DateOnly(2026, 1, 1));
        if (archived)
        {
            wallet.Archive(DateTimeOffset.UtcNow);
        }

        db.Add(wallet);
        await db.SaveChangesAsync();
        return wallet.Id;
    }

    public static async Task ArchiveWalletAsync(this ApiFactory factory, TestAccount owner, Guid walletId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        Populate(scope, owner);
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();

        (await db.Set<Wallet>().FindAsync(walletId))!.Archive(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    private static void Populate(AsyncServiceScope scope, TestAccount owner) =>
        scope.ServiceProvider.GetRequiredService<CurrentContext>().Populate(owner.Session.User.Id, owner.Session.Tenant.Id, Channel.App);

    /// <summary>Lê o corpo como JSON; pode ser chamado mais de uma vez na mesma resposta.</summary>
    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    public static async Task<string?> CodeAsync(this HttpResponseMessage response) =>
        (await response.JsonAsync()).GetProperty("code").GetString();
}
