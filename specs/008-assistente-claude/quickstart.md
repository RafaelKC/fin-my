# Quickstart — validar a 008

## Suítes automáticas

| Suíte | Cobre |
|---|---|
| `OAuthFlowTests` (Api.Tests) | descoberta, registro dinâmico (redirect permitido/negado), authorize → front, ticket, código, token com PKCE, refresh, revogação (SC-004), troca/redefinição de senha revoga |
| `McpToolContractTests` | cada ferramenta via cliente MCP do SDK contra `WebApplicationFactory`: entrada/saída, erros `ambiguous_or_not_found`, padrões FR-005, `created_via = claude` |
| `McpConfirmationTests` | SC-003: as 4 ferramentas com confirmação não alteram nada sem `confirmed: true` |
| `McpParityTests` | SC-002 automatizado: mesmas respostas numéricas de `get_month_summary`, `get_invoice`, `get_settlements` e dos endpoints REST para um conjunto de dados |
| Isolamento | ferramentas com ids de outro Espaço → erro `not_found` |
| e2e `autorizar.spec.ts`, `aplicativos-conectados.spec.ts` | US1.1, US1.2 |

## Manual (antes de liberar)

1. Publicar com HTTPS (`docs/arquitetura.md` §13).
2. No Claude: adicionar conector personalizado `<APP_URL>/mcp`, logar, autorizar.
3. Enviar a skill (`integrations/claude/skill/`).
4. Rodar `integrations/claude/roteiro-de-testes.md` (30 pedidos) e registrar o resultado.
5. Desconectar no Perfil → próximo pedido no Claude falha em ≤ 1 min.
