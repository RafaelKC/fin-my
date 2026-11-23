# Testes do backend

Quatro projetos, um por camada (veja `docs/arquitetura.md` §1). Rode **um projeto por vez**
(cada um sobe um PostgreSQL via Testcontainers; no Linux sem Docker, use o `podman` com o socket ativo):

```bash
cd backend
dotnet build
dotnet test tests/FinMy.Domain.Tests
dotnet test tests/FinMy.Application.Tests
dotnet test tests/FinMy.Architecture.Tests
dotnet test tests/FinMy.Api.Tests
```

Depois, limpe só os containers de teste já parados:
`podman container prune -f --filter label=org.testcontainers=true`.

## Suíte de isolamento entre Espaços

Fica em `FinMy.Api.Tests/Isolation/` e cobre o SC-003 (docs/arquitetura.md §3.6 e §3.7). Para rodar só ela:

```bash
cd backend
dotnet test tests/FinMy.Api.Tests --filter "FullyQualifiedName~FinMy.Api.Tests.Isolation"
```

| Arquivo | O que garante |
|---|---|
| `TenantEntityIsolationTests` | Descobre por reflexão todo tipo `ITenantEntity` mapeado e **falha se faltar fábrica** em `TenantDataFactories`; B não vê nem apaga linhas de A em nenhum tipo (e sem Espaço no contexto nada aparece). |
| `ApiIsolationTests` | Lê as operações do **OpenAPI gerado pela própria API** (`/openapi/v1.json`, fora de produção) e chama cada uma como B depois de A criar dados: nada de A na resposta, id de A em rota → 404 `not_found`, id de A em corpo/consulta → 404/422 e nada de A alterado (`AccountSnapshot`). |
| `TenantClaimTests` | O `tid` do token só é emitido para quem tem `tenant_members`; login e refresh recusam quem perdeu a participação. |

### Ao criar uma feature nova

- **Nova entidade `ITenantEntity`:** acrescente a fábrica em `TenantDataFactories.ByType` (o teste falha até lá).
- **Nova entidade global (sem `TenantId`):** registre como achar as linhas de A em `AccountSnapshot.GlobalOwners`.
- **Novo endpoint:** nada a fazer — ele entra na varredura sozinho. Se o corpo gerado a partir do esquema não passar
  da validação, registre um corpo válido em `OperationSamples.Bodies` para a chamada chegar à regra de negócio.
- **Dados de A para o endpoint listar/detalhar:** crie-os em `ApiIsolationTests.CreateAAsync` (pelo contrato HTTP).
