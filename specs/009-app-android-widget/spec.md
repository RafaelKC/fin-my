# Feature Specification: App Android (TWA) com widget de cartão

**Feature Branch**: `009-app-android-widget`

**Created**: 2026-11-25

**Status**: Draft

**Input**: User description: "Fazer como TWA já com um widget de cartão: ela escolhe o cartão e mostra a fatura atual como está e o planejado. Não vou publicar na Play Store, então um APK já resolve."

> Glossário e regras transversais (RT-xx): ver `.specify/memory/constitution.md`.
> Depende de: 003 (faturas), 007 (`InvoiceForecast`/estimativa), 008 (OAuth/OpenIddict, aplicativos conectados).
> Distribuição: APK instalado à mão (sem Play Store). O app web continua sendo o mesmo PWA.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - App instalado pelo APK (Priority: P1)

A pessoa instala o APK no Android e abre o "Fin-My" pelo ícone. O app abre o mesmo site em tela
cheia (sem barra do navegador), com o login, as telas e os atalhos de sempre.

**Independent Test**: instalar o APK num emulador apontando para a stack local e navegar até o mês.

**Acceptance Scenarios**:

1. **Given** o APK instalado e o site com `/.well-known/assetlinks.json` correto, **When** a pessoa abre o app, **Then** o site abre em tela cheia, sem barra de endereço (TWA verificada).
2. **Given** o app aberto, **When** a pessoa entra com a conta, **Then** a sessão funciona como no navegador (mesmo cookie, mesma origem).
3. **Given** o ícone do app, **When** a pessoa segura o ícone, **Then** aparecem os atalhos do manifest (ex.: "Novo lançamento").

### User Story 2 - Widget da fatura do cartão (Priority: P1)

Na tela inicial, a pessoa adiciona o widget "Fatura do cartão", entra (uma vez) e escolhe qual
cartão ele mostra. O widget exibe a fatura atual daquele cartão: quanto já está nela (**Atual**),
quanto se espera que ela feche (**Planejado**), vencimento, situação e quanto falta pagar.

**Independent Test**: com um cartão "Azul" tendo R$ 300,00 de compras na fatura atual e estimativa de R$ 800,00, o widget mostra "Atual R$ 300,00 · Planejado R$ 800,00" e o vencimento.

**Acceptance Scenarios**:

1. **Given** o app instalado e nenhum login no widget, **When** a pessoa adiciona o widget, **Then** abre a configuração pedindo para entrar; após autorizar (tela de consentimento com o nome "App Fin-My (Android)"), aparece a lista de cartões ativos para escolher.
2. **Given** o cartão escolhido, **When** o widget é exibido, **Then** mostra nome e cor do cartão, mês/vencimento da fatura atual (a fatura em que cairia uma compra feita hoje, 003), **Atual** (`totalCents`), **Planejado** (`plannedCents` da 007), situação (aberta, fechada, paga, atrasada) e restante a pagar.
3. **Given** dois widgets na tela, **When** cada um foi configurado com um cartão, **Then** cada um mostra o seu cartão.
4. **Given** o widget, **When** a pessoa toca no corpo dele, **Then** o app abre na tela da fatura (`/cartoes/:id/faturas/:mes`); **When** toca no ícone de atualizar, **Then** os dados são buscados de novo.
5. **Given** o widget, **When** passam 30 minutos, **Then** ele se atualiza sozinho (WorkManager), sem gastar bateria fora disso.
6. **Given** sem rede, **When** o widget tenta atualizar, **Then** mantém os últimos valores e mostra "atualizado às HH:MM".
7. **Given** a pessoa desconectou o "App Fin-My (Android)" em Perfil → Aplicativos conectados (ou trocou a senha), **When** o widget atualiza, **Then** mostra "Entre de novo" e o toque abre o login do widget.
8. **Given** o cartão foi arquivado ou excluído, **When** o widget atualiza, **Then** mostra "Cartão indisponível — toque para escolher outro".

### Edge Cases

- Fatura atual paga: mostra "Paga" e restante R$ 0,00.
- Sem estimativa: Planejado = valor de `InvoiceForecast` (que já cai para o total quando não há estimativa, 007).
- Tema do sistema claro/escuro: o widget acompanha.
- Valores grandes: formato compacto sem cortar ("R$ 12.345,67" cabe no tamanho 2x2 ou usa fonte menor).

## Requirements *(mandatory)*

- **FR-001**: Projeto Android em `android/` gerando APK instalável (TWA) com o pacote fixo `br.app.finmy` e URL do site configurável na build.
- **FR-002**: O servidor publica `/.well-known/assetlinks.json` com o pacote e as impressões digitais SHA-256 configuradas (`Android__PackageName`, `Android__CertSha256__N`); sem configuração, responde 404.
- **FR-003**: O widget usa OAuth 2.1 com PKCE (008) como um cliente público pré-registrado `finmy-android` com redirect `br.app.finmy:/oauth/callback`; não usa o cookie do app.
- **FR-004**: Tokens do cliente `finmy-android` só acessam as rotas `GET /api/v1/widget/*` (somente leitura); tokens do Claude não acessam essas rotas e vice-versa.
- **FR-005**: `GET /api/v1/widget/credit-cards` lista os cartões ativos; `GET /api/v1/widget/credit-cards/{id}/current-invoice` devolve a fatura atual com Atual, Planejado, estimativa, pago, restante, situação, fechamento, vencimento e `today`.
- **FR-006**: A tela `/autorizar` e "Aplicativos conectados" mostram o nome do cliente ("Claude" ou "App Fin-My (Android)") e o texto de consentimento adequado a cada um.
- **FR-007**: Tokens guardados no Android com armazenamento cifrado (Keystore); refresh com rotação (008).
- **FR-008**: Isolamento: o widget só vê dados do Espaço de quem autorizou (RT de isolamento).
- **FR-009**: CI compila o projeto Android e roda os testes unitários; APK de release assinado localmente com chave fora do repositório.

## Success Criteria

- **SC-001**: Do APK instalado ao widget mostrando a fatura em menos de 2 minutos.
- **SC-002**: Os valores do widget são idênticos aos da tela da fatura no app para o mesmo cartão e mês.
- **SC-003**: Desconectar no Perfil faz o widget pedir login na próxima atualização.
