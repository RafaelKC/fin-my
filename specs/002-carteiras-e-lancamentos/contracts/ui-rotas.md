# Contrato de interface — 002

Rotas novas/alteradas do front. Textos em pt-BR; valores `R$ 1.234,56`; datas `25/02/2026` (RT-10).
Layout funcional a partir de 360 px; alvos de toque ≥ 44 px.

| Rota | Tela | Requisitos |
|---|---|---|
| `/` | redireciona para `/mes/<mês de hoje>` | US3.5 |
| `/mes/:mes` (`AAAA-MM`) | Visão do mês | US3, US4; FR-014..FR-020, FR-026 |
| `/carteiras` | Lista de carteiras ativas com saldo atual e principal marcada; filtro "Arquivadas"; "Nova carteira" | US1 |
| `/carteiras/nova`, `/carteiras/:id` | Formulário de carteira (nome, tipo, cor da paleta, saldo inicial com calculadora, data); ações Tornar principal, Arquivar/Desarquivar, Excluir (só sem lançamentos) | US1; FR-001..FR-004 |
| `/busca` | Campo de busca + filtros (tipo, carteira, situação, período), lista e total | US6.5; FR-025 |
| (sobreposição) Formulário rápido | Aberto pelo "+" de qualquer tela logada (bottom sheet tela cheia no celular, diálogo no computador); também abre para editar ao tocar num lançamento | US2, US5, US6; FR-008..FR-013 |

## Casca (alterações)

- Barra inferior no celular: **Mês · Carteiras · + · Busca · Mais** (Cartões e Acertos entram na 003/004
  substituindo Carteiras/Busca, que então vão para "Mais"). "Mais" leva a `/perfil` por enquanto.
- Menu lateral no computador: Mês, Carteiras, Busca, Perfil, e botão "+ Lançar".

## Visão do mês — detalhes

- Cabeçalho: `‹  Março de 2026  ›` (setas e deslizar; mantém filtros na query string
  `?carteiras=<ids>&situacao=planned|settled`).
- Resumo em duas colunas **Previsto | Realizado**: Saldo inicial (com linha secundária
  "inclui saldo inicial de carteiras" quando houver), Entradas, Saídas, Resultado, Saldo final; com uma
  única carteira filtrada, também "Transferências recebidas/enviadas". Resultado e Saldo final
  negativos em vermelho (FR-017).
- Aviso de pendências só no mês de hoje: "N pendência(s) de meses anteriores: a receber R$ X · a pagar
  R$ Y" → abre lista com seleção múltipla e ações Marcar como recebida/paga, Trazer para este mês,
  Excluir (com Desfazer).
- Listas: Entradas, Saídas (ordem RT-12), Transferências (recolhida). Cada linha: descrição, data,
  valor, carteira (cor), e o controle de situação de um toque (caixinha). Prevista com data < hoje
  mostra selo "Atrasada". Com filtro de uma carteira, transferências aparecem como
  "Transferência recebida/enviada".
- Computador (≥ 1024 px): lista em tabela (Descrição, Data, Valor, Carteira, Situação) ordenável por
  coluna, situação com um clique (FR-026).
- Mês vazio: resumo + "Nada lançado neste mês. Toque em + para lançar."
- Após qualquer mudança, recarrega a visão do mês (FR-016) sem ação manual.

## Formulário rápido — detalhes

- Topo: seletor Saída | Entrada | Transferência (inicial = último tipo salvo no aparelho; primeiro
  uso: Saída).
- Campos na ordem: Valor (teclado próprio no celular; pré-visualização "= R$ 102,20"); Descrição
  (sugestões após 1 caractere; transferência: padrão "Transferência", sem sugestões); Data (hoje);
  Carteira (Transferência: De / Para); Situação (só Entrada/Saída; FR-012); "Mais opções":
  Mês de referência e Observação.
- Sem carteira cadastrada: o campo Carteira mostra "Criar carteira" que abre mini-formulário (nome,
  saldo inicial) sem perder o que foi digitado.
- Data anterior ao saldo inicial da carteira: aviso não bloqueante (Edge Cases da spec).
- Botões: "Salvar" e "Salvar e lançar outro" (FR-013). Em edição: "Salvar", "Duplicar", "Excluir".
- Exclusão: snackbar "Excluído — Desfazer" por 8 s (RT-09).
- Sem conexão: snackbar de erro e formulário mantido (US2.7).
