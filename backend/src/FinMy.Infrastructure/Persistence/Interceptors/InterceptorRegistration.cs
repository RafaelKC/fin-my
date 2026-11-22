using FinMy.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Persistence.Interceptors;

public static class InterceptorRegistration
{
    /// <summary>
    /// Registra os interceptors de gravação na ordem de docs/arquitetura.md §3.4:
    /// <see cref="TenantInterceptor"/> → <see cref="SoftDeleteInterceptor"/> → <see cref="AuditInterceptor"/>.
    /// </summary>
    public static DbContextOptionsBuilder AddFinMyInterceptors(this DbContextOptionsBuilder builder, IClock clock)
        => builder.AddInterceptors(new TenantInterceptor(), new SoftDeleteInterceptor(clock), new AuditInterceptor(clock));
}
