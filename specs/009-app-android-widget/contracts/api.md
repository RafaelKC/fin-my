# Contratos — 009

Todas exigem `Authorization: Bearer <token do cliente finmy-android>` (recurso `<APP_URL>/api/v1/widget`).
JWT do app ou token do Claude → 401. Respostas em JSON, valores em centavos + texto formatado.

| Método e rota | Resposta | Erros |
|---|---|---|
| `GET /api/v1/widget/credit-cards` | `{ today, cards: [{ id, name, color, closingDay, dueDay }] }` (só ativos, ordem por nome) | 401 |
| `GET /api/v1/widget/credit-cards/{id}/current-invoice` | ver abaixo (inclui `history`: as 6 últimas faturas até a atual, `[{ dueMonth, totalCents }]`) | 401, 404 `credit_card.not_found` (outro Espaço, arquivado ou excluído) |
| `GET /.well-known/assetlinks.json` (anônimo) | `[{ relation: ["delegate_permission/common.handle_all_urls"], target: { namespace: "android_app", package_name, sha256_cert_fingerprints } }]` | 404 sem configuração |

```json
{
  "today": "2026-11-25",
  "card": { "id": "…", "name": "Azul", "color": "#3b82f6" },
  "invoice": {
    "dueMonth": "2026-12", "closingDate": "2026-12-10", "dueDate": "2026-12-25",
    "status": "open|closed|paid|overdue",
    "totalCents": 30000, "totalText": "R$ 300,00",
    "plannedCents": 80000, "plannedText": "R$ 800,00",
    "estimateCents": 80000,
    "paidCents": 0, "remainingCents": 30000, "remainingText": "R$ 300,00"
  },
  "appPath": "/cartoes/…/faturas/2026-12"
}
```

## Telas

- `/autorizar`: título e texto conforme o cliente. Claude: texto atual da 008. `finmy-android`:
  "O App Fin-My (Android) poderá ver seus cartões e faturas no Espaço <nome> para mostrar no widget."
- Perfil → Aplicativos conectados: nome do cliente na lista (Claude / App Fin-My (Android)).

## Adendo 010 — `GET /api/v1/widget/overview`

Mesmo token do widget. Resposta: `{ today, month, plannedIncome, plannedExpense, plannedFinal, actualIncome, actualExpense,
actualFinal, totalBalance, pending: { count, toReceive, toPay }, upcoming: [{ kind: income|expense|invoice, description, date,
amount, overdue }] (até 8: atrasadas primeiro, depois por data), settlements: [{ tagId, name, color, amount }] (≠ 0, maior
primeiro), monthPath, settlementsPath }`, com cada valor como `{ cents, text }`. Números iguais aos de `GET /months/{m}`,
`GET /now` e `GET /settlements/{m}`.
