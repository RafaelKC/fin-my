# Data Model: 009

Nenhuma tabela nova. Alterações:
- Seed idempotente de uma aplicação OpenIddict (tabelas da 008): `client_id = finmy-android`, tipo público,
  `display_name = "App Fin-My (Android)"`, redirect `br.app.finmy:/oauth/callback`, permissões authorize/token,
  `authorization_code`, `refresh_token`, PKCE obrigatório.
- `ICurrentContext.Channel` ganha o valor `android_widget`.
- Configuração nova: `Android:PackageName`, `Android:CertSha256` (lista), `Android:RedirectUri` (padrão acima).
