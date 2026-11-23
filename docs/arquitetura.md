# Arquitetura técnica do Fin-My

Documento técnico normativo. Complementa a constituição (`.specify/memory/constitution.md`), que
define o QUE (regras de negócio RT-xx e princípios); aqui está o COMO. O modelo de dados está em
[`modelo-de-dados.md`](modelo-de-dados.md). Cada `/speckit-plan` de feature MUST seguir este documento;
desvios exigem registro em "Complexity Tracking" do `plan.md` e atualização deste arquivo.

Palavras MUST / MUST NOT / SHOULD / MAY no sentido da RFC 2119.

---

## 1. Visão geral

```
┌──────────────────────────┐      HTTPS (mesma origem)      ┌──────────────────────────────┐
│ Front-end Angular (PWA)  │ ─────────── /api/v1 ─────────▶ │ API ASP.NET Core (.NET 10)   │
│ celular + computador     │ ◀────────── JSON ───────────── │ Application / Domain / Infra │
└──────────────────────────┘                                 └──────────────┬───────────────┘
                                                                            │ EF Core 10 (Npgsql)
                                                                     ┌──────▼──────┐
                                                                     │ PostgreSQL  │
                                                                     └─────────────┘
```

- **Monorepo** neste repositório:
  ```
  backend/
    src/FinMy.Api/              # endpoints, auth, middlewares, ProblemDetails
    src/FinMy.Application/      # casos de uso (commands/queries), validação de entrada, DTOs
    src/FinMy.Domain/           # entidades, enums, regras puras (cálculos), erros de domínio
    src/FinMy.Infrastructure/   # EF Core, interceptors, filtros, jobs, e-mail, relógio
    tests/FinMy.Domain.Tests/        # testes unitários das regras (vetores da spec)
    tests/FinMy.Application.Tests/   # testes de casos de uso com banco real (Testcontainers)
    tests/FinMy.Api.Tests/           # testes HTTP ponta a ponta da API + suíte de isolamento
    tests/FinMy.Architecture.Tests/  # regras de arquitetura (dependências, tenant, auditoria)
  frontend/                     # Angular
  shared/test-vectors/          # vetores JSON usados pelos testes do backend E do frontend
  docs/  specs/  .specify/
  integrations/claude/skill/    # SKILL.md da skill Fin-My (008)
  backend/Dockerfile  frontend/Dockerfile
  docker-compose.yml
  ```
- Dependências entre camadas: `Api → Application → Domain`; `Infrastructure → Application, Domain`;
  `Api → Infrastructure` apenas como raiz de composição (registro de DI e comando `migrate`; endpoints
  não usam tipos da Infrastructure); `Domain` não depende de nada (nem de EF Core). Verificado por
  teste de arquitetura.
- Ambiente local sem Docker: usar podman (`DOCKER_HOST=unix:///run/user/$UID/podman/podman.sock`,
  `TESTCONTAINERS_RYUK_DISABLED=true`) e `podman compose`/`podman-compose`. O CI usa Docker.

## 2. Stack

| Item | Escolha | Observação |
|---|---|---|
| Runtime | .NET 10 (LTS) | |
| API | ASP.NET Core Minimal APIs agrupadas por feature | OpenAPI gerado (`Microsoft.AspNetCore.OpenApi`) |
| ORM | EF Core 10 + Npgsql | Named query filters (EF 10) |
| Banco | PostgreSQL 17+ | nomes em `snake_case` (`EFCore.NamingConventions`) |
| Jobs | Hangfire + Hangfire.PostgreSql | mesmo padrão do fin-backend |
| E-mail | MailKit (SMTP) | |
| Logs | Serilog estruturado (JSON) | sem dados financeiros nem e-mail em claro nos logs |
| Testes back | xUnit, Testcontainers.PostgreSql, Shouldly (ou AwesomeAssertions) | **não** usar FluentAssertions 8 (licença comercial); **não** usar SQLite/InMemory em testes de dados |
| Front | Angular (última versão estável), standalone components, signals, Angular Material 3 + CDK | `@angular/pwa` (service worker) |
| Gráficos | Chart.js via ng2-charts | só Painel (006) |
| Testes front | runner padrão do Angular CLI; Playwright para e2e (viewports 360×800 e 1280×800) | |
| Deploy | Um `Dockerfile` por aplicação (`backend/Dockerfile`, `frontend/Dockerfile`) e um único `docker-compose.yml` na raiz que sobe `api`, `web` (nginx servindo o build e fazendo proxy de `/api` e `/mcp`) e `db` | mesma origem → sem CORS; ver §13 |
| Integração Claude | MCP server (SDK oficial C# `ModelContextProtocol.AspNetCore`) dentro da API + OpenIddict como servidor OAuth 2.1 | 008; ver §12 |

## 3. Multi-tenancy (Espaços) — automático, nunca caso a caso

Termos: **Espaço = Tenant** no código. Participação = `TenantMember`.

### 3.1 Contexto ambiente

- `ICurrentContext` (scoped, em `Application`), implementado em `Infrastructure`:
  `UserId?`, `TenantId?`, `Channel` (`app`, `claude`, `system`), `IsAuthenticated`, `IsSystem`,
  `BeginSystemScope(Guid tenantId)`.
- Preenchido por um middleware a partir do **access token** já validado pelo `JwtBearer`
  (claims `sub` = UserId, `tid` = TenantId). Nunca ler o token sem validação (o fin-backend usa
  `ReadJwtToken` sem validar — não repetir).
- Jobs em segundo plano usam `ITenantJobRunner.ForEachTenantAsync(func)`, que abre um escopo de DI
  por Espaço, chama `BeginSystemScope(tenantId)` (UserId = `SystemUser.Id`, constante) e executa.
  Assim filtros e interceptors funcionam igual nos jobs.

### 3.2 Marcação das entidades

```csharp
public interface IEntity            { Guid Id { get; } }          // UUID v7 gerado pela aplicação
public interface ITenantEntity : IEntity { Guid TenantId { get; } }
public interface IAudited           { DateTimeOffset CreatedAt; Guid CreatedBy; DateTimeOffset UpdatedAt; Guid UpdatedBy; }
public interface ISoftDeletable     { DateTimeOffset? DeletedAt; Guid? DeletedBy; Guid? DeletionBatchId; }
public interface IArchivable        { DateTimeOffset? ArchivedAt; }

public abstract class TenantEntity : ITenantEntity, IAudited, ISoftDeletable { ... }  // base padrão
```

- Toda tabela financeira MUST herdar `TenantEntity` (ou implementar `ITenantEntity`, para tabelas de
  ligação). Exceções globais (não pertencem a Espaço): `users`, `tenants`, `tenant_members`,
  `refresh_tokens`, `password_reset_tokens`, `login_throttles`. Teste de arquitetura falha se uma
  entidade mapeada não for `ITenantEntity` e não estiver nessa lista.
- Setters de `TenantId` e dos campos de auditoria são `internal`/privados: só os interceptors os
  preenchem. Código de aplicação MUST NOT atribuí-los.

### 3.3 Leitura: filtros globais (fail-closed)

No `OnModelCreating`, por reflexão, para cada tipo:
- `ITenantEntity` → filtro nomeado `"Tenant"`: `e.TenantId == _ctx.TenantIdOrEmpty`
  (`TenantIdOrEmpty` = `Guid.Empty` quando não há Espaço no contexto ⇒ **nenhuma linha**). Diferente do
  fin-backend (`!IsLogged || ...`), que desliga o filtro quando não há login.
- `ISoftDeletable` → filtro nomeado `"SoftDelete"`: `e.DeletedAt == null`.
- `IgnoreQueryFilters(["SoftDelete"])` MAY ser usado apenas em: restaurar (desfazer) e job de expurgo.
- Ignorar o filtro `"Tenant"` MUST NOT acontecer em código de aplicação. Teste de arquitetura procura
  `IgnoreQueryFilters()` sem nome e `"Tenant"` fora de `Infrastructure/Jobs`.

### 3.4 Escrita: interceptors (`SaveChangesInterceptor`)

Ordem de registro: `TenantInterceptor` → `SoftDeleteInterceptor` → `AuditInterceptor`.

1. **TenantInterceptor**
   - `Added` de `ITenantEntity`: se não há `TenantId` no contexto → lança `MissingTenantContextException`;
     se a entidade já tem `TenantId` diferente do contexto → lança `CrossTenantWriteException`;
     senão atribui `TenantId` do contexto.
   - `Modified`: se `TenantId` foi alterado → lança `CrossTenantWriteException` (o fin-backend
     apenas ignora a alteração em silêncio; aqui é erro).
   - `Modified`/`Deleted` de entidade de outro Espaço → lança (defesa extra; normalmente o filtro já
     impede carregar).
2. **SoftDeleteInterceptor**: `Deleted` de `ISoftDeletable` vira `Modified` com `DeletedAt = agora`,
   `DeletedBy = UserId`, `DeletionBatchId` = id do lote corrente (`ICurrentContext.DeletionBatchId`,
   gerado por requisição de exclusão). Entidades dependentes (ex.: `entry_tags`) não são apagadas.
3. **AuditInterceptor**: `Added` → `CreatedAt/By/Via` e `UpdatedAt/By/Via` (`Via` = `ICurrentContext.Channel`, 008 FR-002/FR-008); `Modified` → `UpdatedAt/By` e
   marca `CreatedAt/By` como não modificados. `By` = `UserId` do contexto ou `SystemUser.Id` em
   escopo de sistema; se nenhum → lança. Horário vem de `IClock` (nunca `DateTime.Now`).

### 3.5 Integridade entre Espaços no banco

- Toda tabela com `tenant_id` tem `UNIQUE (tenant_id, id)`.
- Toda chave estrangeira entre tabelas de Espaço é **composta**: `(tenant_id, wallet_id)` →
  `wallets (tenant_id, id)`. Assim o banco recusa um lançamento do Espaço A apontando para a carteira
  do Espaço B, mesmo que a aplicação erre (RT-01). Em EF: `HasPrincipalKey(w => new { w.TenantId, w.Id })`
  + `HasForeignKey(e => new { e.TenantId, e.WalletId })`.
- (Opcional, fase 2) Row-Level Security no Postgres com `SET app.tenant_id` via
  `DbConnectionInterceptor`, como terceira camada.

### 3.6 Preparação para compartilhamento (001 FR-005a)

- O `tid` do token só é emitido se existir `tenant_members (tenant_id, user_id)`. O refresh verifica de novo a
  participação, então remover alguém do Espaço vale em no máximo 15 min (validade do access token).
- Nada no código pergunta "é o dono?". Autorização = "participa do Espaço do contexto?" (garantido
  pelo token) + papel (na v1 só `owner`, que pode tudo).
- Trocar de Espaço (futuro) = endpoint que emite novo access token com outro `tid`.

### 3.7 Testes obrigatórios de isolamento

`FinMy.Api.Tests/Isolation`: cria Espaços A e B com uma instância de **cada** tipo `ITenantEntity`
(descobertos por reflexão; teste falha se surgir tipo sem fábrica de dados), e para cada endpoint
listado no OpenAPI verifica: listar/detalhar/buscar/sugerir/relatório como B não retorna nada de A;
GET/PUT/DELETE de id de A como B → 404; POST de B referenciando id de A → 404/422 e nada gravado.

## 4. Auditoria e exclusão

- Colunas automáticas em toda `TenantEntity`: `created_at`, `created_by`, `created_via`, `updated_at`, `updated_by`, `updated_via`,
  `deleted_at`, `deleted_by`, `deletion_batch_id`. Usuário de sistema: `SystemUser.Id =
  00000000-0000-0000-0000-000000000001`.
- Exclusão é sempre lógica (soft delete). **Desfazer** (RT-09): `POST /api/v1/deletions/{batchId}/restore`
  restaura todas as linhas do lote. O servidor aceita restaurar por 10 minutos (folga sobre os
  8 s da interface). Job diário expurga definitivamente linhas com `deleted_at` > 30 dias.
- Índices únicos de regra de negócio são **parciais** (`WHERE deleted_at IS NULL`).
- Concorrência otimista: coluna de sistema `xmin` do Postgres como token de concorrência em toda
  entidade editável; conflito → HTTP 409 com código `concurrency_conflict` (relevante quando houver
  compartilhamento).
- Histórico campo a campo (audit log) não é exigido na v1.

## 5. Dinheiro, datas e cálculos (implementação das RT)

- Dinheiro: `long` em centavos no domínio e no banco (`bigint`, colunas `*_cents`). Value object
  `Money` no domínio. Nenhum `decimal`/`double` em regra de negócio, exceto dentro do parser de
  expressão, que usa `decimal` e converte com arredondamento meio-para-cima (RT-02).
- Datas de negócio: `DateOnly` / `date`. Mês: `DateOnly` com dia 1 / `date` com `CHECK (extract(day
  from x) = 1)`, exposto na API como `"AAAA-MM"`. Instantes técnicos: `DateTimeOffset` / `timestamptz` (UTC).
- `IClock`: `UtcNow` e `Today` (data em America/Sao_Paulo). Testes usam relógio fixo.
- Regras puras ficam em `FinMy.Domain` como funções sem I/O, testadas com os exemplos das specs:
  `ValueExpressionParser` (RT-05), `MoneySplitter` (RT-02), `InvoiceCycle` (003 FR-003/004/008),
  `InstallmentGenerator` (003 FR-012..014), `InvoiceStatusRule` (003 FR-006), `InvoiceForecast`
  (003 FR-022 + 007 FR-010), `MonthSummaryCalculator` (002 FR-015), `SettlementCalculator` (004),
  `RecurrenceScheduler` (005).
- Agregações: o banco soma por mês (`GROUP BY reference_month`, faturas por `due_month`); o domínio
  compõe saldos acumulados. Nada de totais desnormalizados gravados em tabelas na v1 (volume
  esperado: poucos milhares de lançamentos/ano por Espaço).
- **Vetores compartilhados**: `shared/test-vectors/*.json` (expressões de valor, divisão de centavos,
  ciclo de fatura, situação de fatura). O backend e o frontend rodam os mesmos vetores, garantindo
  que a pré-visualização da interface bate com o servidor.

## 6. API

- REST JSON em `/api/v1`, recursos em inglês no plural (`/wallets`, `/entries`, `/credit-cards`,
  `/invoices`, `/card-purchases`, `/transfers`, `/tags`, `/recurrences`, `/months/{yyyy-MM}/summary`,
  `/years/{yyyy}/planning`...). Contratos de cada feature em `specs/NNN-*/contracts/` (OpenAPI).
- JSON em camelCase; datas `"AAAA-MM-DD"`; meses `"AAAA-MM"`; dinheiro na resposta em centavos
  inteiros (`amountCents`). Na entrada, campos de valor são **texto** (`amount: "29+73,2"`) avaliado
  pelo servidor (RT-05); a resposta traz o valor calculado.
- Erros: `application/problem+json` com `code` estável (ex.: `invoice.payment_exceeds_remaining`),
  `detail` em pt-BR pronto para exibir e `errors` por campo em validação (HTTP 422). Recurso de
  outro Espaço ou inexistente → 404 `not_found` (RT-01).
- `POST` de criação aceita cabeçalho `Idempotency-Key` (UUID gerado pelo cliente por formulário):
  repetir a mesma chave em 24 h devolve a mesma resposta sem duplicar (toque duplo, rede instável
  no celular). Tabela `idempotency_keys`.
- Listas paginadas por cursor quando puderem passar de 200 itens (busca); listas do mês e da fatura
  vêm inteiras.
- Casos de uso como handlers (`CreateEntry`, `PayInvoice`...) na camada Application; validação de
  formato com FluentValidation; regras de negócio no domínio retornando `Result`/erros de domínio
  (sem exceções para fluxo esperado).

## 7. Autenticação (001)

- Senha: hash com `PasswordHasher<T>` do ASP.NET Core Identity (PBKDF2), sem o resto do Identity.
- Access token JWT assinado, validade 15 min, claims `sub`, `tid`, `sid` (sessão = `family_id` do
  refresh token), `name`, `channel`. Guardado só em memória no front.
- A cada requisição autenticada, um middleware confere se a sessão `sid` está ativa (cache em memória
  invalidado na hora quando a sessão é revogada), para que logout/troca de senha valham na próxima ação
  (001 research R3). Válido para uma instância de API; com várias, usar cache distribuído.
- Refresh token opaco de 256 bits, guardado **hash** (SHA-256) em `refresh_tokens`, enviado em cookie
  `HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth`. Validade deslizante de 90 dias desde o
  último uso (001 FR-009); **rotação** a cada uso, com detecção de reuso (reuso de token já trocado
  revoga a família inteira).
- Troca/redefinição de senha: revoga todos os refresh tokens do usuário exceto o da sessão atual
  (troca) ou todos (redefinição) (001 FR-012).
- Bloqueio por e-mail em `login_throttles` (001 FR-010), inclusive para e-mails inexistentes (não
  revelar existência, 001 SC-004); rate limiting por IP (`Microsoft.AspNetCore.RateLimiting`) nos
  endpoints de auth.
- Tokens de redefinição: 256 bits, hash guardado, 1 h, uso único.

## 8. Jobs

| Job | Frequência | Feature |
|---|---|---|
| Estender horizonte das recorrências (até dezembro do ano seguinte) | diário 03:00 BRT, idempotente | 005 |
| Expurgar soft-deletes > 30 dias | diário | todas |
| Limpar refresh tokens expirados, tokens de senha e `idempotency_keys` > 24 h | diário | 001 |

Jobs MUST ser idempotentes e rodar por Espaço via `ITenantJobRunner` (§3.1). Além do job, a extensão
do horizonte também é executada sob demanda ao abrir um mês/ano além do horizonte gerado.

## 9. Front-end

- Angular standalone + signals; estado de servidor via serviços por feature; sem NgRx na v1.
- Layout responsivo: celular (≥ 360 px) com barra de navegação inferior (Mês · Cartões · **+** ·
  Acertos · Mais) e botão "+" central; computador (≥ 1024 px) com menu lateral e tabelas.
- **Campo de valor**: no celular, teclado numérico próprio do app com `+ − × ÷ ( ) ,` e `⌫`, porque
  os teclados nativos `inputmode="decimal"` não têm operadores; no computador, campo de texto.
  Pré-visualização do resultado com o mesmo parser dos vetores compartilhados.
- PWA: service worker só para o *app shell* e ativos; dados nunca ficam em cache offline (constituição).
  Rascunhos de formulário em `localStorage` por formulário, apagados ao salvar.
- Preferências do aparelho (tela inicial, último tipo/carteira/cartão usados) em `localStorage`.
- Localização fixa pt-BR (`LOCALE_ID = 'pt-BR'`), moeda BRL.
- Acessibilidade: alvos de toque ≥ 44 px, contraste AA, foco visível.

## 10. Qualidade e processo

- Teste primeiro para toda regra de cálculo (constituição, Princípio III): os exemplos numéricos das
  specs viram testes nomeados pelo requisito (`Spec003_FR008_compra_no_dia_do_fechamento_vai_para_proxima`).
- Testes de dados sempre em PostgreSQL real (Testcontainers) — filtros e FKs compostas não funcionam
  em SQLite/InMemory (o fin-backend desliga o filtro de tenant no SQLite; não repetir).
- Testes de arquitetura (NetArchTest ou ArchUnitNET): camadas; toda entidade mapeada é
  `ITenantEntity` ou está na lista global; nenhum `IgnoreQueryFilters` proibido; nenhum `DateTime.Now`.
- Migrações EF versionadas no repositório; aplicadas no start da API em desenvolvimento e por passo
  explícito no deploy.
- CI: build, testes back (com Postgres), testes front, e2e Playwright nos dois viewports.

## 11. Mapa glossário → código

| Domínio (pt-BR) | Código | Tabela |
|---|---|---|
| Usuário | `User` | `users` |
| Espaço | `Tenant` | `tenants` |
| Participação | `TenantMember` | `tenant_members` |
| Carteira | `Wallet` | `wallets` |
| Entrada / Saída | `Entry` (`EntryKind.Income` / `Expense`) | `entries` |
| Situação Prevista / Recebida-Paga | `EntryStatus.Planned` / `Settled` | `entries.status` |
| Mês de referência | `ReferenceMonth` | `entries.reference_month` |
| Transferência | `Transfer` | `transfers` |
| Cartão | `CreditCard` | `credit_cards` |
| Fatura | `Invoice` | `invoices` |
| Estimativa de fatura | `Invoice.EstimateCents` | `invoices.estimate_cents` |
| Compra | `CardPurchase` (`PurchaseNature.Debit` / `Credit`) | `card_purchases` |
| Grupo de parcelamento | `InstallmentPlan` | `installment_plans` |
| Pagamento de fatura | `InvoicePayment` | `invoice_payments` |
| Tag / Tag de acerto | `Tag` (`IsSettlement`) | `tags` |
| Recorrência / Ocorrência | `Recurrence` / lançamento com `RecurrenceId` | `recurrences` |
| Divisão | `SplitGroupId` | coluna em `entries` / `card_purchases` |

## 12. Integração com o Claude (008) — MCP + skill

- **Regra de ouro**: o MCP server é só mais um adaptador da camada `Application`, como os endpoints
  REST. Cada ferramenta MCP chama os MESMOS handlers (`CreateEntry`, `CreateCardPurchase`,
  `GetMonthSummary`, `PayInvoice`...). Nenhuma regra de negócio dentro das ferramentas. Por isso,
  desde a 001, casos de uso MUST NOT depender de HTTP (sem `HttpContext` em `Application`).
- **Transporte**: MCP Streamable HTTP em `/mcp`, mesmo processo da API.
- **Autorização**: OAuth 2.1 com PKCE e registro dinâmico de cliente, como exigido para conectores
  personalizados do Claude, implementado com **OpenIddict** na API (tela de login/consentimento
  servida pelo front). Token de acesso MCP com claims `sub`, `tid` e `channel=claude`; o middleware
  preenche `ICurrentContext` igual ao app, então tenant, auditoria e soft delete continuam
  automáticos (§3–§4). Revogação: "Aplicativos conectados" (008 FR-003) revoga as autorizações do
  OpenIddict; redefinir senha também revoga.
- **Ferramentas**: nomes em inglês `snake_case` (`create_entry`, `create_card_purchase`,
  `create_transfer`, `undo_last`, `get_month_summary`, `get_wallet_balances`, `list_invoices`,
  `get_invoice`, `list_pending`, `get_settlements`, `search_entries`, `set_entry_status`,
  `pay_invoice`, `generate_settlement_charge`, `delete_entry`, `list_catalog`). Parâmetros aceitam
  **nomes** de carteira/cartão/tag (resolução 008 FR-006) e devolvem erros estruturados com as opções.
  Descrições das ferramentas em pt-BR. Ferramentas que exigem confirmação (008 FR-007) são marcadas
  como destrutivas (`destructiveHint`) e recebem `confirmed: true` obrigatório.
- **Idempotência**: cada chamada de criação gera/aceita uma chave de idempotência (§6).
- **Skill**: `integrations/claude/skill/SKILL.md` versionada no repo; contém as instruções da 008
  FR-009 e exemplos de conversa. Distribuída manualmente (upload no Claude dela).
- **Testes**: testes de contrato das ferramentas (entrada/saída) + roteiro de conversas (008 SC-002/003)
  executado manualmente antes de liberar.

## 13. Deploy

- `backend/Dockerfile`: build multi-stage (`sdk` → `aspnet`), usuário não-root, healthcheck em
  `/health`.
- `frontend/Dockerfile`: build multi-stage (`node` → `nginx`), nginx serve o build com fallback para
  `index.html` e faz proxy de `/api` e `/mcp` para o serviço `api`.
- `docker-compose.yml` (raiz): serviços `db` (PostgreSQL com volume persistente), `api` (depende de
  `db` saudável; migrações aplicadas por passo explícito `api migrate` antes de subir) e `web`
  (única porta exposta). Variáveis em `.env` (não versionado; `.env.example` versionado).
- HTTPS terminado na frente do `web` (proxy reverso do servidor ou Cloudflare). Obrigatório para o
  conector do Claude (008).
- Backup diário do PostgreSQL (`pg_dump`) para fora do servidor.

## 14. Desvios e decisões de implementação da 001

Registro do que a implementação da 001 (contas, Espaços e fundação) fez de diferente do descrito acima
ou que não estava dito. Quando uma feature futura mexer em um destes pontos, atualize esta lista.

| # | Tema | O que foi feito | Por quê |
|---|---|---|---|
| 1 | `Hangfire:Enabled` (§8) | O Hangfire (servidor e jobs recorrentes) só sobe se houver connection string **e** `Hangfire:Enabled` não for `false` (padrão `true`). Os testes HTTP (`FinMy.Api.Tests`) o desligam. | Cada `WebApplicationFactory` abriria conexões e workers próprios do Hangfire no PostgreSQL do teste, esgotando conexões. |
| 2 | E-mail em segundo plano (§7) | `POST /auth/password-reset/request` só enfileira (`HangfirePasswordResetDispatcher`) e responde 202; o envio roda em `PasswordResetEmailJob`. Com Hangfire desligado, `InlinePasswordResetDispatcher` envia na própria requisição e engole/registra qualquer falha. | O tempo e o resultado da resposta não podem revelar se o e-mail tem conta (001 SC-004/US4.2); falha de SMTP não pode virar erro para quem pediu. Consequência para os e2e: o e-mail chega depois da resposta, então se espera com `expect.poll` no Mailpit. |
| 3 | Limite de auth configurável (§7) | `RateLimiting:Auth:PermitLimit` (padrão 20/min por IP) e `WindowSeconds` (60). No compose de CI vira `RateLimiting__Auth__PermitLimit=100000` (`docker-compose.ci.yml`). A API confia no `X-Forwarded-For` do nginx (porta da API não publicada) para enxergar o IP real. | A suíte e2e roda dezenas de cadastros/logins da mesma máquina contra a mesma API e esbarraria no limite de produção. Nunca subir o limite fora do ambiente de teste. |
| 4 | Migração como passo separado (§13) | O compose tem o serviço `migrate` (mesma imagem da API, comando `migrate`, `restart: "no"`); a `api` só sobe depois de `service_completed_successfully`. A API não aplica migrações ao iniciar, nem em desenvolvimento (contrário ao §10, "aplicadas no start da API em desenvolvimento"). Na primeira execução o `migrate` loga um erro do EF ao consultar `__EFMigrationsHistory` antes de a tabela existir; é esperado e a migração conclui. | Um único caminho de migração em dev, CI e produção; evita corrida entre instâncias da API. |
| 5 | e2e: service worker bloqueado | `playwright.config.ts` usa `serviceWorkers: 'block'`. Só `pwa.spec.ts` religa (`test.use({ serviceWorkers: 'allow' })`). | O worker do build de produção intercepta as requisições antes do `page.route`, e os e2e com API simulada deixariam de funcionar. O worker só é registrado no build de produção (`enabled: !isDevMode()`), então a cobertura de PWA exige `E2E_BASE_URL` apontando para o `web` do compose. |
| 6 | e2e contra a stack real | Além da API simulada, há e2e contra o compose real (cadastro, login, bloqueio, troca de senha em dois aparelhos, recuperação lendo o Mailpit). Exigem `E2E_BASE_URL` (e `MAILPIT_URL`); no CI rodam no job `e2e` com `docker-compose.ci.yml`. Mapa passo a passo em `frontend/e2e/README.md`. | O quickstart §3 precisa ser provado de ponta a ponta nos dois viewports, e o isolamento do refresh token (cookie `HttpOnly`, `Path=/api/v1/auth`) só se prova com a API de verdade. |
| 7 | Testcontainers no Podman | `PostgresFixture` usa `ContainerBuilder` genérico (imagem `postgres:17`, porta aleatória) e testa a prontidão abrindo uma conexão, em vez de `Testcontainers.PostgreSql` com a espera padrão. Localmente: `DOCKER_HOST` no socket do Podman, `TESTCONTAINERS_RYUK_DISABLED=true`, um projeto de teste por vez e `podman container prune -f --filter label=org.testcontainers=true` depois. | A espera padrão usa `exec` (`pg_isready`) e leitura de logs, que travam no socket do Podman rootless. Funciona igual com Docker no CI. |
| 8 | nginx e o IP da API | O nginx do `web` resolve `api` uma vez, ao iniciar. Recriar só o container da `api` (IP novo) exige reiniciar o `web`. | Comportamento do `proxy_pass` com nome fixo; relevante no Podman manual, onde a ordem de criação é feita à mão. |
| 9 | PWA: "instalar" no aparelho | O e2e verifica o que o navegador exige (manifest com `standalone`, ícones 192 e 512, worker registrado, nada de `/api/` no cache). A instalação em si ("Adicionar à tela inicial", abrir em tela cheia já logado) continua passo manual do quickstart §3.8. | O Playwright não aciona o prompt de instalação. |
