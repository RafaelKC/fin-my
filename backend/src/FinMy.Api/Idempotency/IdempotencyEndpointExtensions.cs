namespace FinMy.Api.Idempotency;

public static class IdempotencyEndpointExtensions
{
    /// <summary>Aplica o <see cref="IdempotencyFilter"/> (cabeçalho <c>Idempotency-Key</c>) ao endpoint de criação.</summary>
    public static TBuilder WithIdempotency<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => builder.WithMetadata(IdempotencyMetadata.Instance).AddEndpointFilter<TBuilder, IdempotencyFilter>();

    /// <summary>
    /// Habilita a releitura do corpo nos endpoints marcados por <see cref="WithIdempotency{TBuilder}"/>. Filtros de
    /// endpoint rodam depois do binding, que já consumiu o corpo; por isso o buffer precisa ser ligado antes, aqui.
    /// Deve vir depois de <c>UseRouting</c>.
    /// </summary>
    public static IApplicationBuilder UseIdempotencyBuffering(this IApplicationBuilder app)
        => app.Use((context, next) =>
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<IdempotencyMetadata>() is not null)
            {
                context.Request.EnableBuffering();
            }

            return next(context);
        });

    /// <summary>Marca o endpoint como idempotente.</summary>
    internal sealed class IdempotencyMetadata
    {
        public static readonly IdempotencyMetadata Instance = new();
    }
}
