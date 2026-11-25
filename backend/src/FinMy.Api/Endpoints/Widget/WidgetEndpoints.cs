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
    }
}
