# Data model — 008

Todas globais (sem Espaço; entram na lista global do teste de arquitetura e de `docs/arquitetura.md` §3.2):

- Tabelas do **OpenIddict** (`openiddict_applications`, `openiddict_authorizations`,
  `openiddict_scopes`, `openiddict_tokens`) via `UseOpenIddict()` com nomes snake_case.
- `oauth_consent_tickets`: `id uuid PK`, `ticket_hash bytea UNIQUE`, `user_id uuid`, `tenant_id uuid`,
  `client_id text`, `redirect_uri text`, `code_challenge text`, `expires_at timestamptz`,
  `used_at timestamptz null`, `created_at`. Expurgo diário (> 1 dia).
- `oauth_authorization_usage`: `authorization_id text PK`, `user_id uuid`, `last_used_at timestamptz`.

Colunas `created_via`/`updated_via` já existem desde a 001 (valor `claude`).

Migração: `OAuth`.
