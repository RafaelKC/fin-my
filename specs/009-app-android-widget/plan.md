# Implementation Plan: App Android (TWA) com widget de cartão

**Branch**: `009-app-android-widget` | **Date**: 2026-11-25 | **Spec**: [spec.md](spec.md)

## Summary
APK Android (TWA) que abre o PWA em tela cheia, mais um widget nativo (Kotlin/Glance) que mostra a fatura atual
de um cartão escolhido (Atual × Planejado). O widget autentica com o OAuth da 008 como cliente público
`finmy-android` e lê duas rotas novas somente leitura em `/api/v1/widget`. O site publica `assetlinks.json`.

## Technical Context
- Backend: .NET 10, OpenIddict 7 (008), serviços de fatura (003) e `InvoiceForecast` (007).
- Front: Angular 22 (Zard UI) — só `/autorizar` e Aplicativos conectados mudam.
- Android: Kotlin, Gradle Kotlin DSL, AGP atual, minSdk 26, Java 21, androidbrowserhelper (TWA), Jetpack Glance,
  WorkManager, AppAuth, DataStore + Keystore, kotlinx.serialization, OkHttp. Testes: JUnit + Robolectric.
- Estrutura nova: `android/` (`app/` com TWA + widget), `integrations/android/README.md`.

## Decisões
Ver [research.md](research.md) R1–R7. Contratos em [contracts/api.md](contracts/api.md).

## Constitution Check
Isolamento por Espaço (FR-008), mesmos números em todo lugar (SC-002), testes no mesmo PR, dados fictícios.
