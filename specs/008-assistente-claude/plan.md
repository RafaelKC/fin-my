# Implementation Plan: Lançar e consultar pelo Claude (conector + skill)

**Branch**: `008-assistente-claude` | **Date**: 2026-11-23 | **Spec**: [spec.md](spec.md)

## Summary

Servidor MCP em `/mcp` dentro da API, com OAuth 2.1 (OpenIddict, PKCE, registro dinâmico, consentimento
pelo front), ferramentas que chamam os mesmos handlers do app, "Aplicativos conectados" no perfil, selo
"via Claude" e a skill versionada. Decisões em [research.md](research.md); ferramentas em
[contracts/mcp-tools.md](contracts/mcp-tools.md).

## Technical Context

Igual às anteriores + OpenIddict (EF Core stores) e o SDK MCP oficial para C#. Pré-requisitos: 002,
003 e 004 na `main`.

## Constitution Check

| Princípio / regra | Como o plano atende | Status |
|---|---|---|
| II | Token com `tid`; `ICurrentContext` preenchido igual ao app; isolamento testado também via MCP | ✅ |
| III | Nenhum cálculo nas ferramentas; números vêm dos handlers (SC-002) | ✅ |
| IV | Resolução de nomes só exata (research R4); confirmação por `confirmed` com preview | ✅ |
| T1 | `created_via = claude` pela infraestrutura (claim `channel`) | ✅ |

## Project Structure

```text
backend/src/FinMy.Infrastructure/Auth/OAuth/   OpenIddict, registro dinâmico, tickets de consentimento
backend/src/FinMy.Api/Endpoints/OAuth/         /.well-known/*, /oauth/*, /api/v1/oauth/consent-tickets
backend/src/FinMy.Api/Mcp/                     servidor e Tools/
backend/src/FinMy.Application/Catalog/         CatalogResolver, padrões FR-005
frontend/src/app/features/autorizar/           consentimento
frontend/src/app/features/perfil/              Aplicativos conectados
integrations/claude/                           skill/SKILL.md, README.md, roteiro-de-testes.md
```

## Complexity Tracking

| Item | Por quê | Alternativa rejeitada |
|---|---|---|
| Ticket de consentimento (research R2) | O app não tem sessão por cookie na API; o authorize precisa de navegação de página inteira | Cookie de sessão só para OAuth (segundo mecanismo de sessão) |
