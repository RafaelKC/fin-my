using FinMy.Api.Endpoints.Transfers;
using FinMy.Api.Idempotency;
using FinMy.Application.Cards;

namespace FinMy.Api.Endpoints.Cards;

/// <summary>
/// Fatura, pagamentos e "levar restante" (003 US5, US6.2, US7.1): <c>/credit-cards/{id}/invoices/{month}*</c> e
/// <c>/invoice-payments/{id}</c> de contracts/openapi.yaml. A fatura é identificada por cartão + mês de vencimento (research R2).
/// </summary>
public sealed class InvoiceEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // `month` chega como texto para o cartão ser conferido antes do formato (id de outro Espaço → 404).
        var invoices = app.MapGroup("/api/v1/credit-cards/{id:guid}/invoices/{month}").WithTags("invoices").RequireAuthorization();

        invoices.MapGet("/", async (Guid id, string month, string? tagIds, GetInvoiceHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(InvoiceResponse.From(await handler.HandleAsync(id, month, tagIds, cancellationToken))));

        invoices.MapPut("/estimate", async (Guid id, string month, InvoiceEstimateRequest? body, SetInvoiceEstimateHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(InvoiceResponse.From(await handler.HandleAsync((body ?? new InvoiceEstimateRequest(null)).ToCommand(id, month), cancellationToken))));

        invoices.MapPost("/payments", async (Guid id, string month, InvoicePaymentCreateRequest? body, RegisterInvoicePaymentHandler handler, CancellationToken cancellationToken) =>
        {
            var invoice = await handler.HandleAsync((body ?? new InvoicePaymentCreateRequest(null, null, null)).ToCommand(id, month), cancellationToken);
            return Results.Created($"/api/v1/credit-cards/{id}/invoices/{month}", InvoiceResponse.From(invoice));
        }).WithIdempotency();

        invoices.MapDelete("/payments", async (Guid id, string month, DeleteInvoicePaymentsHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(new DeletionResponse(await handler.HandleAsync(id, month, cancellationToken))));

        invoices.MapPost("/carry-over", async (Guid id, string month, CarryOverInvoiceHandler handler, CancellationToken cancellationToken) =>
            Results.Created($"/api/v1/credit-cards/{id}/invoices/{month}", InvoiceResponse.From(await handler.HandleAsync(id, month, cancellationToken))));

        var payments = app.MapGroup("/api/v1/invoice-payments").WithTags("invoices").RequireAuthorization();

        payments.MapPut("/{id:guid}", async (Guid id, InvoicePaymentUpdateRequest body, UpdateInvoicePaymentHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(InvoicePaymentResponse.From(await handler.HandleAsync(body.ToCommand(id), cancellationToken))));

        payments.MapDelete("/{id:guid}", async (Guid id, DeleteInvoicePaymentHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(new DeletionResponse(await handler.HandleAsync(id, cancellationToken))));
    }
}
