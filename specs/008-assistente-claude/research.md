# Research — 008 Lançar e consultar pelo Claude (conector MCP + skill)

Normativo: `docs/arquitetura.md` §12. Regra de ouro: ferramentas MCP chamam os MESMOS handlers da
`Application` que os endpoints REST; nenhuma regra nova.

## R1. Servidor MCP

- SDK oficial C# (`ModelContextProtocol.AspNetCore`, versão estável mais recente; conferir na
  implementação), transporte Streamable HTTP em `/mcp`, mesmo processo da API, sem estado de sessão
  (stateless). Ferramentas em `backend/src/FinMy.Api/Mcp/Tools/`, uma classe por grupo.
- O nginx do `web` já faz proxy de `/mcp` (001 T003); conferir que não há buffering
  (`proxy_buffering off`) para respostas em streaming.

## R2. OAuth 2.1 para o conector

- **OpenIddict** (servidor + validação local) com as tabelas do EF Core no mesmo banco (tabelas
  globais, lista global do teste de arquitetura). Fluxo authorization code + PKCE (S256 obrigatório),
  clientes públicos, refresh token com rotação (90 dias deslizante), access token 1 h.
- **Descoberta**: `GET /.well-known/oauth-protected-resource` (RFC 9728, `resource = <APP_URL>/mcp`,
  `authorization_servers = [<APP_URL>]`) e `GET /.well-known/oauth-authorization-server` (RFC 8414,
  gerado pelo OpenIddict). `/mcp` sem token → 401 com
  `WWW-Authenticate: Bearer resource_metadata="<APP_URL>/.well-known/oauth-protected-resource"`.
  O nginx precisa encaminhar `/.well-known/` e `/oauth/` para a API.
- **Registro dinâmico** (RFC 7591): o OpenIddict não traz; endpoint próprio `POST /oauth/register` cria
  uma aplicação pública com o `client_name` e os `redirect_uris` pedidos, aceitos só se todos estiverem
  na lista `Mcp:AllowedRedirectUris` (padrão: `https://claude.ai/api/mcp/auth_callback`,
  `https://claude.com/api/mcp/auth_callback`); senão 400 `invalid_redirect_uri`. Rate limit `auth`.
- **Login e consentimento** (o front guarda o access token só em memória, sem cookie de sessão na API):
  1. Claude abre `GET /oauth/authorize?...`; o endpoint (modo passthrough) valida o pedido e, sem
     `consent_ticket`, redireciona para o front `/autorizar?<mesmos parâmetros>`.
  2. O front garante login (fluxo normal da 001), mostra "O Claude poderá ver e registrar seus
     lançamentos no Espaço <nome>" e, em "Autorizar", chama `POST /api/v1/oauth/consent-tickets`
     (bearer do app; corpo = parâmetros do pedido) → `{ ticket }` (opaco, uso único, 2 min, hash
     guardado em `oauth_consent_tickets`).
  3. O front navega (página inteira) para `GET /oauth/authorize?<parâmetros>&consent_ticket=<ticket>`;
     a API valida o ticket (mesmo usuário, mesmos `client_id`/`redirect_uri`/`code_challenge`), cria
     a autorização permanente e responde 302 para o `redirect_uri` do Claude com o código.
  "Cancelar" volta ao `redirect_uri` com `error=access_denied`.
- **Claims do token**: `sub` (UserId), `tid` (TenantId da participação, 001 FR-005), `channel=claude`,
  `oi_au_id` (id da autorização). Um handler de validação próprio preenche `ICurrentContext` igual ao
  `CurrentContextMiddleware` (tenant, auditoria `created_via = claude` automáticos).
- **Revogação imediata (SC-004)**: validação com `EnableAuthorizationEntryValidation()` e
  `EnableTokenEntryValidation()` (consulta a autorização a cada chamada). Desconectar = revogar a
  autorização e seus tokens. Trocar senha (001 FR-012) e redefinir senha revogam todas as autorizações
  do usuário.

## R3. Aplicativos conectados (FR-003)

- `GET /api/v1/me/connected-apps` → `[{ id (autorização), name (client_name), connectedAt, lastUsedAt }]`;
  `DELETE /api/v1/me/connected-apps/{id}` → 204.
- Último uso: tabela global `oauth_authorization_usage (authorization_id PK, user_id, last_used_at)`
  atualizada pelo handler de validação no máximo 1×/5 min por autorização.

## R4. Ferramentas (FR-004)

Parâmetros e respostas em [contracts/mcp-tools.md](contracts/mcp-tools.md). Decisões:

- **Resolução de nomes (FR-006)**: `CatalogResolver` (Application) compara `TextNormalizer` exato,
  só entre itens ativos. Zero ou mais de um → erro de ferramenta `ambiguous_or_not_found` com
  `options` (nomes ativos daquele tipo). Sem busca por trecho (evita escolher errado).
- **Padrões (FR-005)**: data = hoje (`IClock`); situação = FR-012; carteira/cartão = do lançamento mais
  recente com a mesma descrição normalizada e mesmo tipo (sugestões da 002/003); senão carteira
  principal (entrada/saída) ou cartão com mais compras nos últimos 30 dias (compra); se nenhum cartão
  → erro `card_required` com `options`. Toda resposta de criação devolve todos os valores usados e,
  em compras, a fatura (mês e vencimento) de cada parcela/parte.
- **Tags**: `tags` por nome; tag comum inexistente é criada (004 US1.2); tag de acerto inexistente →
  `ambiguous_or_not_found` (não cria de acerto). Divisão (`split`) usa os endpoints/handlers da 004.
- **Desfazer (US2.5)**: `undo_creation { id }` exclui o lançamento e todo o grupo criado junto
  (parcelamento, divisão) se ele foi criado por este usuário com `created_via = claude` há ≤ 10 min;
  senão erro `undo_not_allowed`. Sem confirmação.
- **Confirmação (FR-007, SC-003)**: `set_entry_status`, `pay_invoice`, `generate_settlement_charge` e
  `delete_entry` têm `confirmed: boolean` (`destructiveHint = true`). Sem `confirmed: true` a
  ferramenta **não altera nada** e devolve `preview` com exatamente o que faria. Teste automatizado
  garante que nenhuma dessas altera dados sem `confirmed: true`.
- **Idempotência**: ferramentas de criação aceitam `requestId` (UUID opcional) usado como
  `Idempotency-Key` (mesma tabela e serviço da 002 R2; extrair a lógica do filtro HTTP para um serviço
  reaproveitável se necessário).
- **Consultas** devolvem `today` (para o Claude interpretar "ontem", "esse mês") e valores em centavos
  **e** formatados (`"R$ 1.234,56"`) para o Claude não errar formatação (FR-009).
- Erros de regra de negócio viram resultado de ferramenta com `isError: true` e `{ code, message }`
  (message = `detail` pt-BR do app).

## R5. "via Claude" (FR-008)

- `Entry`, `CardPurchase`, `Transfer` nas respostas REST ganham `createdVia: app | claude | system`.
  O front mostra o selo "via Claude" na lista do mês, na fatura e no formulário de edição.

## R6. Skill

- `integrations/claude/skill/SKILL.md` (frontmatter `name: fin-my`, `description` em pt-BR) com as
  instruções da FR-009, o glossário curto, o fluxo de confirmação (chamar sem `confirmed`, mostrar o
  preview, chamar com `confirmed: true` só após "sim") e exemplos de conversa (dados fictícios).
- `integrations/claude/README.md`: como publicar (HTTPS obrigatório), adicionar o conector
  personalizado no Claude (URL `<APP_URL>/mcp`) e enviar a skill.
- `integrations/claude/roteiro-de-testes.md`: os 30 pedidos de SC-002/SC-003 com o resultado esperado,
  executados manualmente antes de liberar.
