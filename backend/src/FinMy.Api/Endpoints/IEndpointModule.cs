namespace FinMy.Api.Endpoints;

/// <summary>Grupo de endpoints de uma feature (Minimal APIs agrupadas por feature). Registrado no DI e mapeado em <c>Program</c>.</summary>
public interface IEndpointModule
{
    void MapEndpoints(IEndpointRouteBuilder app);
}
