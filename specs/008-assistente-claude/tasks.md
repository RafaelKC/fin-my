---
description: "Tarefas da feature 008 — Lançar e consultar pelo Claude"
---

# Tasks: Lançar e consultar pelo Claude (conector + skill)

**Input**: `specs/008-assistente-claude/` — [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/mcp-tools.md](contracts/mcp-tools.md), [quickstart.md](quickstart.md).
Normas: `docs/arquitetura.md` §12, constituição, research da 002–004.

**Testes**: obrigatórios, no mesmo PR. **Unidade**: 1 tarefa = 1 card = 1 PR. Agentes não editam este arquivo.
**Pré-requisito**: 004 concluída na `main`. Dados fictícios.

## Phase 1: Conexão (US1)

- [x] T001 [US1] OAuth 2.1 com OpenIddict (research R2): tabelas e migração `OAuth` (data-model.md), descoberta (`/.well-known/oauth-protected-resource`, `/.well-known/oauth-authorization-server`), `POST /oauth/register` com lista de redirects permitidos, `/oauth/authorize` (passthrough → front ou ticket), `/oauth/token` (code + PKCE S256, refresh com rotação), `POST /api/v1/oauth/consent-tickets`, validação que preenche `ICurrentContext` com `channel=claude` e checa a autorização a cada chamada, revogação de todas as autorizações ao trocar/redefinir senha; nginx encaminhando `/.well-known/` e `/oauth/` para a API; `OAuthFlowTests`
- [ ] T002 [US1] Front `/autorizar` (garante login preservando os parâmetros, texto de consentimento com o nome do Espaço, Autorizar/Cancelar com navegação de página inteira) e "Aplicativos conectados" no Perfil; API `GET/DELETE /api/v1/me/connected-apps` e `oauth_authorization_usage` (research R3); e2e `autorizar.spec.ts` e `aplicativos-conectados.spec.ts`. Depende de T001

## Phase 2: Ferramentas (US2, US3, US4)

- [ ] T003 [US3] Servidor MCP em `/mcp` (SDK oficial, stateless, 401 com `resource_metadata`), `CatalogResolver` e as ferramentas de consulta `list_catalog`, `get_wallet_balances`, `get_month_summary`, `list_pending`, `list_invoices`, `get_invoice`, `get_settlements`, `search_entries` (contracts/mcp-tools.md); `McpToolContractTests` dessas ferramentas e `McpParityTests` (SC-002). Depende de T001
- [ ] T004 [US2] Ferramentas de criação `create_entry`, `create_card_purchase`, `create_transfer` e `undo_creation` (padrões FR-005, tags e divisão, fatura na resposta, `requestId` como idempotência, research R4) chamando os handlers existentes; testes de contrato de US2.1–US2.5 e do erro `ambiguous_or_not_found`. Depende de T003
- [ ] T005 [US4] Ferramentas com confirmação `set_entry_status`, `pay_invoice`, `generate_settlement_charge`, `delete_entry` (preview sem `confirmed`, `destructiveHint`); `McpConfirmationTests` (SC-003) e testes de US4.1–US4.3. Depende de T003
- [ ] T006 [P] [US2] Selo "via Claude" (research R5): `createdVia` nas respostas REST de entradas, compras e transferências e selo no mês, na fatura e no formulário; testes de unidade e e2e simples (lançamento criado pelo banco com `created_via = claude`). Depende de T001

## Phase 3: Skill e entrega

- [ ] T007 Skill e documentação em `integrations/claude/` (research R6): `skill/SKILL.md`, `README.md` (publicar com HTTPS, adicionar conector, enviar skill) e `roteiro-de-testes.md` com os 30 pedidos e resultados esperados (dados fictícios). Depende de T004, T005
- [ ] T008 Validação: isolamento via MCP, quickstart (partes automatizáveis), `docs/arquitetura.md` §12 (nomes finais das ferramentas, ex. `undo_creation`) e §14, README. Depende de todas

## Dependências (resumo)

T001 → {T002, T003, T006}; T003 → {T004, T005}; T004 + T005 → T007 → T008.
