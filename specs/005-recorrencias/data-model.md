# Data model — 005

Normativo: `docs/modelo-de-dados.md` (`recurrences`, `recurrence_tags`). Migração `Recurrences`:

- tabela `recurrences` exatamente como no documento (inclui `settlement_tag_id` e as regras de
  `CHECK`), `TenantEntity`; `name` não existe — a descrição é `description` + `description_normalized`;
- tabela `recurrence_tags (recurrence_id, tag_id)` como as outras ligações da 004 (tags comuns; a de
  acerto fica em `recurrences.settlement_tag_id`);
- em `entries` e `card_purchases`: `recurrence_id uuid null` (FK composta), `recurrence_month date null`
  (`CHECK ((recurrence_id IS NULL) = (recurrence_month IS NULL))`, dia 1), `recurrence_modified boolean
  not null default false`; índice único parcial `(tenant_id, recurrence_id, recurrence_month) WHERE
  deleted_at IS NULL AND recurrence_id IS NOT NULL`;
- `card_purchases.origin` passa a aceitar `recurrence` (atualizar o `CHECK`).

Domínio: `Recurrences/Recurrence` (`Create`, `ChangeFrom`, `End`), enums `RecurrenceKind`,
`RecurrenceEndType`, `ReferenceMonthRule`; `RecurrenceScheduler` (research R1).
