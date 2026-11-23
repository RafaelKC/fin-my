# Data model — 002

Normativo: `docs/modelo-de-dados.md` (colunas padrão, convenções, FKs compostas). Esta feature cria a
migração `Wallets` com as tabelas abaixo. Colunas de features futuras **não** entram agora
(`entries.settlement_tag_id`, `split_group_id`, `recurrence_*` vêm nas migrações da 004/005).

## `wallets` (TenantEntity, IArchivable)

| Coluna | Tipo | Nulo | Regras |
|---|---|---|---|
| [padrão] | | | |
| name | varchar(40) | não | trim; 1..40 |
| name_normalized | varchar(40) | não | `UNIQUE (tenant_id, name_normalized) WHERE deleted_at IS NULL` |
| type | text | não | `CHECK IN ('checking','digital','savings','cash','meal_voucher','other')` |
| color | varchar(7) | não | `CHECK (color ~ '^#[0-9A-Fa-f]{6}$')` |
| initial_balance_cents | bigint | não | qualquer sinal; `CHECK (abs(initial_balance_cents) <= 999999999)` |
| initial_balance_date | date | não | 2000-01-01..2100-12-31 |
| is_primary | boolean | não | `UNIQUE (tenant_id) WHERE is_primary AND archived_at IS NULL AND deleted_at IS NULL` |
| archived_at | timestamptz | sim | `CHECK (NOT (is_primary AND archived_at IS NOT NULL))` |

## `entries` (TenantEntity)

| Coluna | Tipo | Nulo | Regras |
|---|---|---|---|
| [padrão] | | | |
| kind | text | não | `CHECK IN ('income','expense')` |
| description | varchar(80) | não | RT-05a |
| description_normalized | varchar(80) | não | índice `(tenant_id, description_normalized)` |
| amount_cents | bigint | não | `CHECK (amount_cents > 0 AND amount_cents <= 999999999)` |
| entry_date | date | não | `CHECK` 2000-01-01..2100-12-31 |
| reference_month | date | não | `CHECK (extract(day from reference_month) = 1)`, mesmo intervalo; índice `(tenant_id, reference_month)` |
| status | text | não | `CHECK IN ('planned','settled')`; índice `(tenant_id, status, reference_month)` |
| wallet_id | uuid | não | FK composta → wallets; índice `(tenant_id, wallet_id)` |
| notes | varchar(500) | sim | |

## `transfers` (TenantEntity)

| Coluna | Tipo | Nulo | Regras |
|---|---|---|---|
| [padrão] | | | |
| from_wallet_id | uuid | não | FK composta → wallets |
| to_wallet_id | uuid | não | FK composta → wallets; `CHECK (from_wallet_id <> to_wallet_id)` |
| amount_cents | bigint | não | `CHECK (amount_cents > 0 AND amount_cents <= 999999999)` |
| transfer_date | date | não | intervalo RT-03; índice `(tenant_id, transfer_date)` |
| description | varchar(80) | não | padrão "Transferência"; `description_normalized varchar(80)` também gravado (busca) |
| notes | varchar(500) | sim | |

## `idempotency_keys` (global com filtro de Espaço; não é TenantEntity)

`tenant_id uuid`, `key uuid`, PK `(tenant_id, key)`; `user_id uuid`; `request_hash bytea`;
`status_code int` (0 = em andamento); `response_body jsonb` nulo; `created_at timestamptz`.
Índice `(created_at)` para o expurgo. Entra na **lista global** de `docs/arquitetura.md` §3.2 (não é
`ITenantEntity`): o `IdempotencyFilter` grava e consulta sempre com `tenant_id = ctx.TenantId`
explícito; é a única tabela com essa exceção, e o teste de arquitetura a lista nominalmente.

## Domínio (`FinMy.Domain`)

- `Wallets/Wallet` — `Create(...)`, `Rename`, `ChangeColor`, `ChangeInitialBalance`, `Archive()`,
  `Unarchive()`, `MarkPrimary()/UnmarkPrimary()`; enum `WalletType`.
- `Entries/Entry` — `Create(kind, description, amount, date, walletId, status?, referenceMonth?, notes, today)`,
  `ChangeDate(newDate)` (RT-04), `ChangeReferenceMonth`, `ToggleStatus()`, `MoveToMonth(month)`;
  enums `EntryKind`, `EntryStatus`.
- `Transfers/Transfer` — `Create(from, to, amount, date, description?, notes)`.
- `Money/` — `Money`, `ValueExpressionParser`, `MoneySplitter`, `TextNormalizer`, `YearMonth`
  (valor `DateOnly` dia 1, parse/format `AAAA-MM`).
- `Months/MonthSummaryCalculator` — R4.
