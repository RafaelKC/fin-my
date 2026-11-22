using FinMy.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Persistence.Interceptors;

internal static class InterceptorContext
{
    /// <summary>Contexto ambiente do <see cref="FinMyDbContext"/> que está salvando.</summary>
    public static ICurrentContext Current(DbContext? context)
        => context is FinMyDbContext finMy
            ? finMy.CurrentContext
            : throw new InvalidOperationException($"Os interceptors do Fin-My exigem um {nameof(FinMyDbContext)}.");
}
