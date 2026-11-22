# Research — 001 Contas e Espaços (inclui fundação técnica)

As decisões gerais já estão em `docs/arquitetura.md` (normativo). Aqui ficam só as decisões
específicas desta feature ou as que a arquitetura deixou em aberto.

## R1. Como testar tenant/auditoria/soft delete se a 001 ainda não tem dado financeiro

- **Decision**: o modelo é montado por um método reutilizável `ModelBuilderExtensions.ApplyFinMyConventions(modelBuilder, ctx)`
  (filtros nomeados, conversões, FKs compostas). Os testes de infraestrutura usam um `TestFinMyDbContext`
  que herda do contexto real e adiciona uma entidade de sonda `ProbeItem : TenantEntity` (com uma FK
  composta para outra sonda `ProbeParent`). Os interceptors e filtros reais são registrados.
- **Rationale**: valida o mecanismo antes da 002 sem criar tabela fictícia no banco de produção.
- **Alternatives**: esperar a 002 (atrasa o risco principal); criar tabela "probe" em produção (lixo no esquema).

## R2. Filtros nomeados do EF Core 10

- **Decision**: `modelBuilder.Entity<T>().HasQueryFilter("Tenant", e => e.TenantId == ctx.TenantIdOrEmpty)`
  e `HasQueryFilter("SoftDelete", e => e.DeletedAt == null)`; para desligar só o soft delete:
  `IgnoreQueryFilters(["SoftDelete"])`. Expressões referenciam o `ICurrentContext` capturado pelo
  `DbContext` (avaliado por consulta, não no momento da construção do modelo).
- **Rationale**: EF 10 permite vários filtros por entidade e desligá-los individualmente; antes disso só
  havia um filtro combinado e `IgnoreQueryFilters()` desligava o de tenant junto.
- **Verificação na implementação**: a primeira tarefa da fundação inclui um teste que prova que
  `IgnoreQueryFilters(["SoftDelete"])` mantém o filtro de Espaço. Se a API do EF 10 diferir, ajustar
  só a extensão `ApplyFinMyConventions`.

## R3. Sessões: refresh token + encerramento imediato em outros aparelhos

- **Problema**: a spec (001 Edge Cases e FR-012) exige que, ao trocar a senha num aparelho, o outro
  seja deslogado "na próxima ação". Com access token de 15 min isso não aconteceria.
- **Decision**: o access token leva a claim `sid` = `family_id` da sessão (cadeia de refresh tokens).
  Um middleware verifica, a cada requisição autenticada, se a sessão `sid` está ativa, usando cache em
  memória (`IMemoryCache`, 5 min) que é **invalidado na hora** quando a sessão é revogada (logout, troca
  ou redefinição de senha). Revogada → 401 `session_revoked`.
- **Rationale**: atende "próxima ação" sem consulta ao banco a cada requisição.
- **Restrição registrada**: vale para uma única instância da API (cenário atual). Se um dia houver
  várias instâncias, trocar o cache por um distribuído. Anotado em `docs/arquitetura.md` §7.
- **Alternatives**: access token de 1 min (muito refresh); consultar banco sempre (custo desnecessário).

## R4. Fluxo de sessão no front (PWA)

- **Decision**: ao abrir o app, o front chama `POST /api/v1/auth/refresh` (cookie HttpOnly vai sozinho);
  se der 200 recebe o access token e segue; se 401 mostra o login. Um `HttpInterceptor` anexa o
  access token e, em 401 `token_expired`, faz um único refresh e repete a requisição (com fila para
  requisições simultâneas). 401 `session_revoked` ou refresh falho → login, preservando rascunhos
  (constituição, restrições).
- **Rationale**: mantém o login por 90 dias sem guardar token em `localStorage`.

## R5. Bloqueio por tentativas (001 FR-010)

- **Decision**: tabela `login_throttles` por `email_normalized`, mesmo para e-mail inexistente.
  Regra: se `window_started_at` tem mais de 15 min, zera; cada falha incrementa; ao chegar a 5 falhas,
  `locked_until = agora + 15 min`. Durante o bloqueio, qualquer tentativa (inclusive com senha certa)
  → 429 `login_locked` com `retryAfterSeconds`. Sucesso zera o registro. Atualização com
  `SELECT ... FOR UPDATE` (ou `UPDATE ... RETURNING`) para não perder contagem em tentativas simultâneas.
- **Complemento**: rate limiting por IP (`AddRateLimiter`, janela fixa 20 req/min) em `/api/v1/auth/*`.
- **Tempo constante**: login com e-mail inexistente executa um hash de senha "fantasma" para não
  revelar existência pelo tempo de resposta (001 SC-004).

## R6. E-mails

- **Decision**: interface `IEmailSender` (Application) com implementação MailKit/SMTP. Em
  desenvolvimento e testes de ponta a ponta, **Mailpit** no `docker-compose.dev.yml` (captura os e-mails
  e tem interface web). Testes automatizados usam um `FakeEmailSender` que guarda as mensagens.
- **Template**: texto simples + HTML mínimo em pt-BR, link `{APP_URL}/redefinir-senha?token=...`.

## R7. Concorrência no cadastro (mesmo e-mail ao mesmo tempo)

- **Decision**: índice único em `users.email_normalized`; o caso de uso tenta inserir e converte a
  violação de unicidade (`PostgresException` 23505) em 409 `email_already_registered`. Usuário, Espaço
  e Participação são criados na mesma transação.

## R8. Estrutura e ferramentas da fundação

| Tema | Decisão | Alternativa descartada |
|---|---|---|
| Solução .NET | `backend/FinMy.slnx` com os 4 projetos + 4 de teste (arquitetura §1) | solução única com pastas: perde a verificação de dependências entre camadas |
| Handlers | classes simples por caso de uso (`SignUpHandler`), registradas por convenção; sem MediatR | MediatR: dependência a mais (e licença comercial a partir da v13) sem ganho aqui |
| Validação de entrada | FluentValidation | DataAnnotations: mensagens pt-BR e regras compostas ficam piores |
| Testes de arquitetura | NetArchTest.Rules | ArchUnitNET (equivalente; NetArchTest é mais simples) |
| Asserções | Shouldly | FluentAssertions 8 (licença comercial) |
| Migrações | EF Core migrations no projeto Infrastructure; a API tem o comando `migrate` (`dotnet FinMy.Api.dll migrate`) usado pelo compose antes de subir | migrar no start em produção (corrida com várias réplicas, falha silenciosa) |
| Front | `ng new` (Angular estável mais recente), standalone, SCSS, Angular Material 3, `@angular/pwa`, locale pt-BR | Ionic: camada a mais; o visual Material atende |
| Testes front | runner padrão do Angular CLI para unidade; Playwright para e2e | Cypress: Playwright cobre melhor viewport móvel e é mais rápido no CI |
| CI | GitHub Actions: `backend` (build + testes com Testcontainers, Docker disponível no runner), `frontend` (lint + testes + build), `e2e` (compose sobe tudo + Playwright) e `docker` (build das duas imagens) | — |
| Formatação | `dotnet format` + `.editorconfig`; Prettier + ESLint no front; ambos verificados no CI | — |

## R9. Usuário de sistema e ids

- **Decision**: `SystemUser.Id = 00000000-0000-0000-0000-000000000001` (constante, não existe em
  `users`). Ids de entidades gerados na aplicação com `Guid.CreateVersion7()`.

## R10. Nome do Espaço a partir do nome da pessoa

- **Decision**: primeiro nome = primeiro trecho do nome antes de espaço, após trim. Se o resultado
  ultrapassar 60 caracteres com o prefixo "Finanças de ", trunca o primeiro nome para caber.
