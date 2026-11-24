using System.Net;
using System.Net.Http.Json;
using FinMy.Api.Tests.Support;
using FinMy.Domain.Common;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Api.Tests.Cards;

internal sealed record InvoiceSummaryDto(
    Guid CreditCardId, string DueMonth, DateOnly ClosingDate, DateOnly DueDate, Guid? Id, string Status,
    long TotalCents, long PaidCents, long RemainingCents, long? EstimateCents = null, bool EstimateActive = false);

internal sealed record CreditCardDto(
    Guid Id, string Name, int ClosingDay, int DueDay, Guid PaymentWalletId, string Color, long? LimitCents,
    bool Archived, bool HasPurchases, long Version,
    InvoiceSummaryDto? CurrentInvoice, InvoiceSummaryDto? NextInvoice, List<InvoiceSummaryDto>? OpenPastInvoices, long? AvailableLimitCents);

internal sealed record WalletRefDto(Guid Id, string Name);

internal sealed record CardProblemDto(string? Code, int Status, Dictionary<string, string[]>? Errors);

/// <summary>Atalhos HTTP e de banco compartilhados pelos testes de cartões (003).</summary>
internal static class CardTestSupport
{
    public const string CardsUrl = "/api/v1/credit-cards";

    public static object CardBody(
        string name, Guid walletId, int closingDay = 18, int dueDay = 25, string color = "#1E88E5", string? limit = null) =>
        new { name, closingDay, dueDay, paymentWalletId = walletId, color, limit };

    public static async Task<(HttpClient Http, TestAccount Account)> NewSpaceAsync(ApiFactory factory, string name = "Lia")
    {
        using var anonymous = factory.CreateClient();
        var account = await anonymous.SignUpAsync(name: name);
        return (factory.CreateClient().WithBearer(account.AccessToken), account);
    }

    public static async Task<WalletRefDto> CreateWalletAsync(HttpClient http, string name)
    {
        var response = await http.PostAsJsonAsync(
            "/api/v1/wallets",
            new { name, type = "checking", color = "#1E88E5", initialBalance = "0", initialBalanceDate = "2026-01-01" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<WalletRefDto>(JsonDefaults.Options))!;
    }

    public static async Task<CreditCardDto> CreateCardAsync(
        HttpClient http, string name, Guid walletId, int closingDay = 18, int dueDay = 25, string? limit = null)
    {
        var response = await http.PostAsJsonAsync(CardsUrl, CardBody(name, walletId, closingDay, dueDay, limit: limit));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreditCardDto>(JsonDefaults.Options))!;
    }

    public static async Task<CreditCardDto> GetCardAsync(HttpClient http, Guid id) =>
        (await http.GetFromJsonAsync<CreditCardDto>($"{CardsUrl}/{id}", JsonDefaults.Options))!;

    public static async Task<List<CreditCardDto>> ListCardsAsync(HttpClient http, string query = "") =>
        (await http.GetFromJsonAsync<List<CreditCardDto>>($"{CardsUrl}{query}", JsonDefaults.Options))!;

    public static async Task<CardProblemDto> ProblemAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<CardProblemDto>(JsonDefaults.Options))!;

    /// <summary>Roda <paramref name="work"/> no Espaço da conta, com o mesmo contexto de uma requisição do app.</summary>
    public static async Task<T> WithDbAsync<T>(ApiFactory factory, TestAccount account, Func<FinMyDbContext, IServiceProvider, Task<T>> work)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentContext>().Populate(account.Session.User.Id, account.Session.Tenant.Id, Channel.App);
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();
        return await work(db, scope.ServiceProvider);
    }
}
