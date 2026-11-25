# Research: 009 — App Android (TWA) com widget

## R1 — TWA em vez de Capacitor
TWA (androidx.browser + `com.google.androidbrowserhelper:androidbrowserhelper`, `LauncherActivity`) abre o PWA no
Chrome em tela cheia, mesma origem: login por cookie, service worker e atalhos continuam iguais. Sem Play Store:
APK assinado instalado à mão. Para uma futura troca por Capacitor sem perder a instalação: `applicationId`
fixo `br.app.finmy`, mesma chave de assinatura, `versionCode` sempre crescente.
URL do site por propriedade Gradle `finmy.appUrl` (padrão `https://localhost:8443` só para build).

## R2 — Verificação (Digital Asset Links)
API serve `GET /.well-known/assetlinks.json` (o nginx já encaminha `/.well-known/` para a API, 008) com
`delegate_permission/common.handle_all_urls` para `Android:PackageName` e `Android:CertSha256` (lista).
Sem config → 404. Sem verificação o Chrome mostra a barra de endereço (o app ainda funciona).

## R3 — Autenticação do widget
Widget é nativo (Kotlin + Jetpack Glance) e não enxerga o cookie do Chrome. Usa o servidor OAuth da 008:
- cliente público pré-registrado na inicialização (seed idempotente): `client_id = finmy-android`,
  `redirect_uri = br.app.finmy:/oauth/callback`, PKCE S256, `authorization_code` + `refresh_token`,
  nome de exibição "App Fin-My (Android)".
- O fluxo authorize/consentimento é o mesmo da 008 (`/oauth/authorize` → `/autorizar` → ticket). O texto do
  consentimento passa a depender do cliente (FR-006).
- Recurso/audiência: tokens do `finmy-android` recebem o recurso `<APP_URL>/api/v1/widget`; os do Claude
  continuam com `<APP_URL>/mcp`. O seletor de esquema (AuthenticationSetup) manda `/mcp` e `/api/v1/widget`
  para a validação do OpenIddict, e cada grupo de rotas exige o seu recurso (FR-004).
- `ICurrentContext` com `channel = android_widget` (apenas leitura; não cria nada).
- Revogação: mesma da 008 (Aplicativos conectados, troca/redefinição de senha).
- Android: biblioteca AppAuth (`net.openid:appauth`) com Custom Tab; tokens em DataStore cifrado com chave do
  Android Keystore (Tink ou `androidx.security`).

## R4 — Dados do widget
"Fatura atual" = a fatura em que cairia uma compra feita hoje no cartão (mesma regra de `invoice-for`, 003,
`InvoiceCycle`). Valores calculados pelos mesmos serviços da tela da fatura (SC-002):
`totalCents` (Atual), `plannedCents` (`InvoiceForecast`, 007), `estimateCents?`, `paidCents`, `remainingCents`,
`status`, `closingDate`, `dueDate`, `dueMonth`. Hoje no fuso do app (America/Sao_Paulo).

## R5 — Widget
Glance AppWidget (2x2 mínimo, redimensionável), configuração por `appwidget-provider` com activity de
configuração (login se necessário + escolha do cartão), estado por `GlanceId` (cartão escolhido, último
payload, hora). Atualização: WorkManager periódico de 30 min (uma tarefa única para todos os widgets) e
ação de atualizar. Toque abre a TWA em `<appUrl>/cartoes/{id}/faturas/{dueMonth}`. Cores do tema (claro/escuro)
e do cartão; texto com `R$` em pt-BR.

## R6 — Certificado de desenvolvimento
Para testar na VPN (certificado autoassinado): a build `debug` aceita um certificado extra via
`network_security_config` gerado a partir de `finmy.devCert` (caminho para um PEM, opcional, fora do repo).
O Chrome (TWA) exige o certificado instalado no aparelho como CA de usuário — documentado no README.
A build `release` não inclui certificado extra.

## R7 — Build e CI
Gradle Kotlin DSL, AGP atual, minSdk 26, targetSdk/compileSdk atuais, Java 21. Job `android` no CI:
`./gradlew lint testDebugUnitTest assembleDebug` e publica o APK debug como artifact. Release assinado
localmente com keystore fora do repo (`~/.local/share/fin-my-android/`), propriedades via `keystore.properties`
git-ignorado.
