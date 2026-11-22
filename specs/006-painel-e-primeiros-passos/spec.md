# Feature Specification: Painel anual, resumo de agora e primeiros passos

**Feature Branch**: `006-painel-e-primeiros-passos`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Fácil de lançar pelo telefone, mas também ter uma visão na web para facilitar." — na planilha, a aba **Home** mostra, para os 12 meses, Entradas, Saídas e Resultado (com resultado negativo em vermelho), e o saldo/fatura dos cartões.

> Glossário e regras transversais (RT-xx): ver `.specify/memory/constitution.md`.
> Depende de: 001 a 005. Todos os números desta feature vêm das regras já definidas em 002 a 005;
> esta feature NÃO cria regra de cálculo nova (Princípio III).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Painel do ano (Priority: P1)

A pessoa vê o ano inteiro numa tabela: para cada mês, Entradas, Saídas, Resultado e Saldo final,
alternando entre Previsto e Realizado, e entra num mês com um toque.

**Why this priority**: é a aba Home da planilha; a visão "web" que o pedido menciona.

**Independent Test**: com dados em vários meses, conferir que cada linha do painel é idêntica ao resumo da visão do mês correspondente.

**Acceptance Scenarios**:

1. **Given** dados em 2026, **When** a pessoa abre o Painel em 2026 na leitura Previsto, **Then** vê 12 linhas (JAN a DEZ) com Entradas(M), Saídas(M), Resultado(M) e Saldo final(M) idênticos aos da visão de cada mês (002 FR-015), e uma linha de total do ano (soma de Entradas, Saídas e Resultado; Saldo final = o de DEZ).
2. **Given** um mês com Resultado negativo, **When** exibido, **Then** o valor aparece em vermelho.
3. **Given** o Painel, **When** a pessoa toca/clica num mês, **Then** abre a visão daquele mês.
4. **Given** o Painel, **When** a pessoa troca para Realizado, **Then** todos os valores passam a ser os do Realizado.
5. **Given** o Painel no computador, **When** exibido, **Then** mostra também um gráfico de barras Entradas x Saídas por mês e a linha do Saldo final, para a leitura escolhida.
6. **Given** o mês de hoje, **When** exibido no Painel, **Then** a linha dele aparece destacada.

---

### User Story 2 - Resumo de agora (Priority: P1)

No topo do Painel, a pessoa vê o que importa hoje: saldo atual de cada carteira e total, faturas que
vencem nos próximos 30 dias, pendências e quanto cada pessoa (Tag de acerto) deve.

**Why this priority**: responde "quanto eu tenho e o que vem por aí" sem abrir várias telas.

**Independent Test**: com dados de exemplo, conferir cada bloco contra as telas de origem.

**Acceptance Scenarios**:

1. **Given** carteiras Azul (R$ 1.800,00) e Verde (R$ 150,00), **When** o Painel abre, **Then** mostra cada saldo atual (002 FR-003) e o total R$ 1.950,00.
2. **Given** hoje 20/03/2026 e faturas Azul 2026-03 (vence 25/03, Restante 200,00) e Verde 2026-04 (vence 10/04, Restante 300,00), **When** o Painel abre, **Then** lista as duas com vencimento, situação e Restante, em ordem de vencimento; uma fatura que vence em 25/04/2026 não aparece (mais de 30 dias).
3. **Given** existem pendências (002 FR-019), **When** o Painel abre, **Then** mostra a quantidade e os totais com acesso à lista.
4. **Given** Tags de acerto com Saldo acumulado até o mês de hoje (004 FR-008) diferente de zero, **When** o Painel abre, **Then** lista cada uma com o valor ("Bruna te deve R$ 170,00" ou "Crédito da Bruna: R$ 20,00").

---

### User Story 3 - Primeiros passos (Priority: P2)

Num Espaço novo, o app guia a pessoa a cadastrar, em sequência, carteiras com saldo, cartões, pessoas
(Tags de acerto), parcelamentos em andamento e contas fixas — podendo pular qualquer etapa.

**Why this priority**: garante que o primeiro mês no app já bata com a realidade.

**Independent Test**: completar o guia num Espaço vazio e ver o mês atual já com saldos, faturas com parcelas e contas fixas previstas.

**Acceptance Scenarios**:

1. **Given** um Espaço sem carteiras, cartões e lançamentos, **When** a pessoa entra, **Then** vê o guia com 5 etapas: Carteiras (002), Cartões (003), Pessoas que te devolvem dinheiro (Tags de acerto, 004), Parcelamentos em andamento (003 US4) e Contas fixas e salário (005).
2. **Given** o guia, **When** a pessoa toca "Pular" numa etapa, **Then** vai para a próxima; ao final, cai na visão do mês de hoje.
3. **Given** a pessoa saiu do guia no meio, **When** volta ao app, **Then** pode retomar o guia pelo Painel enquanto houver etapa não concluída nem pulada; o guia não reaparece sozinho depois de concluído ou dispensado.
4. **Given** a etapa Carteiras, **When** a pessoa cadastra a primeira carteira, **Then** a data do saldo inicial sugerida é hoje e o texto explica "informe o saldo que está na conta hoje".

---

### User Story 4 - Exportar lançamentos (Priority: P3)

A pessoa exporta os lançamentos de um período para planilha (CSV), para guardar ou analisar.

**Why this priority**: segurança de ter os dados fora do app; não é usado no dia a dia.

**Independent Test**: exportar março/2026 e abrir o arquivo numa planilha.

**Acceptance Scenarios**:

1. **Given** um período escolhido (mês inicial e final), **When** a pessoa exporta, **Then** recebe um arquivo CSV (separador ponto e vírgula, codificação UTF-8, valores com vírgula decimal, datas DD/MM/AAAA) com uma linha por lançamento: tipo, descrição, data, Mês de referência (vazio para Compras), valor (positivo para Entrada, negativo para Saída/Débito/Pagamento de fatura, positivo para Crédito), situação, carteira, cartão, fatura (mês de vencimento), parcela (k/N), tags (separadas por vírgula).
2. **Given** o período, **When** exportado, **Then** Compras entram pelo mês de vencimento da fatura e os demais lançamentos pelo Mês de referência; Transferências aparecem com origem e destino.

---

### Edge Cases

- Ano sem nenhum lançamento: o Painel mostra os 12 meses com zeros e os saldos herdados.
- Ano anterior ao primeiro saldo inicial de carteira: meses com saldo 0,00.
- Navegação de ano: entre 2000 e 2100 (RT-03).
- Resumo de agora com carteiras arquivadas com saldo diferente de zero: aparecem numa linha "Arquivadas" separada; o total inclui todas.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: O Painel MUST mostrar, para o ano escolhido, uma linha por mês com Entradas, Saídas, Resultado e Saldo final, nas leituras Previsto e Realizado, idênticos à visão do mês (002 FR-015, incluindo faturas 003 FR-022), e uma linha de total.
- **FR-002**: Resultados e saldos negativos MUST aparecer em vermelho; o mês de hoje, destacado.
- **FR-003**: Cada mês do Painel MUST levar à visão daquele mês.
- **FR-004**: No computador (largura a partir de 1024 px), o Painel MUST incluir um gráfico de barras Entradas x Saídas por mês com a linha do Saldo final; no celular o gráfico é opcional e fica abaixo da tabela.
- **FR-005**: O Resumo de agora MUST conter: saldos atuais por carteira e total (002 FR-003); faturas com Restante > 0 e vencimento entre hoje − 30 dias e hoje + 30 dias (inclui vencidas recentes), ordenadas por vencimento; pendências (002 FR-019); Saldo acumulado de cada Tag de acerto até o mês de hoje (004 FR-008), quando diferente de zero.
- **FR-006**: A tela inicial ao abrir o app é a visão do mês de hoje (002); a pessoa pode escolher, em preferências do aparelho, que a tela inicial seja o Painel.
- **FR-007**: O guia de primeiros passos MUST aparecer automaticamente no primeiro acesso de um Espaço sem carteiras, com as 5 etapas da US3, cada uma podendo ser pulada; pode ser dispensado definitivamente.
- **FR-008**: A exportação MUST seguir a US4.

### Key Entities

- Nenhuma entidade nova de negócio. Preferência do aparelho: tela inicial (Mês ou Painel). Estado do guia por Espaço: etapas concluídas/puladas, dispensado.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% dos valores do Painel são iguais aos da visão do mês correspondente (teste automatizado com dados de pelo menos 3 meses incluindo faturas, transferências e pendências).
- **SC-002**: Uma pessoa sem treinamento configura o Espaço pelo guia em até 15 minutos.
- **SC-003**: O Painel do ano carrega em até 2 segundos com 3.000 lançamentos no ano.

## Assumptions

- O Painel considera todas as carteiras (sem filtro) na v1.
- O CSV é para leitura humana/planilha; reimportação não faz parte da v1.
