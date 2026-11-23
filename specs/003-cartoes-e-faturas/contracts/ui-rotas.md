# Contrato de interface — 003

| Rota | Tela | Requisitos |
|---|---|---|
| `/cartoes` | Lista de cartões: fatura atual (total, fecha em), próxima fatura, faturas em aberto (vencidas em vermelho), limite disponível; filtro Arquivados; "Novo cartão" | US1, US6; FR-025, FR-026 |
| `/cartoes/novo`, `/cartoes/:id/editar` | Nome, fechamento, vencimento, carteira de pagamento, cor, limite; Arquivar/Excluir | US1; FR-001 |
| `/cartoes/:id/faturas/:mes` | Fatura: `‹ Fatura de março ›`, fecha/vence, situação, Total, Pago, Restante, "Conferido R$ X de R$ Y", lista de compras (RT-12; caixinha "conferida"; ações por compra: editar, mover anterior/seguinte, excluir), pagamentos (editar/excluir), botões "Pagar" (valor, data, carteira) e "Levar restante para a próxima fatura" | US5, US6.2, US7 |

## Casca

Barra inferior no celular: **Mês · Cartões · + · Busca · Mais** (Carteiras vai para "Mais"). Menu
lateral: Mês, Cartões, Carteiras, Busca, Perfil.

## Formulário rápido — aba "Cartão"

- Tipos: Cartão | Saída | Entrada | Transferência; primeiro uso = Cartão (FR-010).
- Campos: Valor (negativo permitido → aviso "Lançado como estorno/crédito"), Descrição (sugestões
  aplicam cartão), Data, Cartão, Parcelas (1x padrão; seletor 1–48) com modo "valor total" / "valor da
  parcela", e "Parcelamento em andamento" (próxima parcela p, fatura da parcela p).
- Linha de pré-visualização sempre visível: "Fatura de março — vence 25/03"; parcelado: "3x de
  R$ 33,34 (1ª) e R$ 33,33 — primeira fatura: fevereiro" (US3.3), calculada pela porta TS.
- 409 de fatura paga → diálogo com o texto do `detail` e "Lançar mesmo assim".
- Edição de parcela: diálogo "Alterar só esta parcela ou todas?" para descrição/observação; exclusão:
  "Só esta / Esta e as seguintes / Todas"; Desfazer 8 s.

## Visão do mês

- Saídas ganham linhas "Fatura <Cartão>" (cor do cartão) com situação, Total, Pago, Restante; caixinha:
  marcar = pagar com padrões; desmarcar (se houver pagamentos) = excluir pagamentos com Desfazer;
  tocar a linha abre `/cartoes/:id/faturas/:mes`.
