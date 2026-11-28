using FinMy.Api.Composition;
using FinMy.Application.Widget;

namespace FinMy.Api.Endpoints.Widget;

/// <summary>
/// Rotas somente leitura do widget Android (009 contracts/api.md): só aceitam o token do cliente <c>finmy-android</c>
/// (recurso <c>&lt;APP_URL&gt;/api/v1/widget</c>, FR-004); o JWT do app e o token do Claude recebem 401.
/// </summary>
public sealed class WidgetEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup(AuthenticationSetup.WidgetPath).WithTags("widget").RequireAuthorization(AuthenticationSetup.WidgetResourcePolicy).ExcludeFromDescription();

        group.MapGet("/credit-cards", async (ListWidgetCardsHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(WidgetCardsResponse.From(await handler.HandleAsync(cancellationToken))));

        group.MapGet("/credit-cards/{id:guid}/current-invoice", async (Guid id, GetWidgetCurrentInvoiceHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(WidgetCurrentInvoiceResponse.From(await handler.HandleAsync(id, cancellationToken))));

        // Widgets "Mês atual", "Próximos vencimentos" e "Acertos" (010).
        group.MapGet("/overview", async (GetWidgetOverviewHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(WidgetOverviewResponse.From(await handler.HandleAsync(cancellationToken))));

        // Relógio (010): as duas únicas escritas do token do app Android — lançamento rápido e marcar como pago/recebido.
        group.MapPost("/entries", async (WidgetCreateEntryRequest body, CreateWidgetEntryHandler handler, CancellationToken cancellationToken) =>
        {
            var created = await handler.HandleAsync(body.Kind, body.Description, body.Amount, cancellationToken);
            return Results.Created($"/api/v1/entries/{created.Id}", WidgetCreatedEntryResponse.From(created));
        });
        // Notificações de banco lidas no celular (010) viram sugestões de lançamento.
        group.MapPost("/suggestions", async (WidgetSuggestionRequest body, FinMy.Application.Suggestions.SubmitSuggestionHandler handler, CancellationToken cancellationToken) =>
        {
            var (view, created) = await handler.HandleAsync(body.ToInput(), cancellationToken);
            var response = FinMy.Api.Endpoints.Suggestions.SuggestionResponse.From(view);
            return created ? Results.Created($"/api/v1/suggestions/{view.Id}", response) : Results.Ok(response);
        });
        group.MapPost("/entries/{id:guid}/settle", async (Guid id, SettleWidgetEntryHandler handler, CancellationToken cancellationToken) =>
        {
            await handler.HandleAsync(id, cancellationToken);
            return Results.NoContent();
        });
    }
}
