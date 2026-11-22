# Feature Specification: Cartões de crédito, Compras parceladas e Faturas

**Feature Branch**: `003-cartoes-e-faturas`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Ela precisa conseguir cadastrar cartão, sendo que no cartão tem fatura, lançamento em vezes e tal. Tão fácil quanto a planilha."

> Glossário e regras transversais (RT-xx): ver `.specify/memory/constitution.md`.
> Depende de: 001, 002. Tags nas Compras: 004.

## Como a planilha funciona hoje (referência)

Cada aba mensal tem um bloco **Cartão de Crédito** com Nome, Pessoa, Data, Cartão e Valor. Parcelas
são digitadas à mão em cada mês, com o número no nome ("Shoppe Casa 5/8") e a data da compra
original; o valor da parcela é calculado à mão (`=110,94/4`) e ajustado em centavos. Estornos são
valores negativos; juros e multa são linhas comuns. O total por cartão de uma aba vira, no mês
seguinte (às vezes no mesmo), uma linha de Saída "Cartão Azul". Este app automatiza: a fatura de
cada compra é calculada pelo dia de fechamento do cartão; as parcelas são geradas sozinhas; e a
fatura aparece sozinha como saída no mês do vencimento (RT-07).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Cadastrar Cartão (Priority: P1)

A pessoa cadastra cada cartão com nome, dia de fechamento e dia de vencimento, e a carteira de onde a
fatura costuma ser paga.

**Why this priority**: pré-requisito para lançar compras.

**Independent Test**: cadastrar "Azul" (fecha 18, vence 25, paga pela carteira Azul) e ver o cartão listado com a fatura atual zerada.

**Acceptance Scenarios**:

1. **Given** um Espaço com a carteira "Azul", **When** a pessoa cadastra o cartão "Azul" com fechamento dia 18, vencimento dia 25 e carteira de pagamento "Azul", **Then** o cartão aparece na lista com a fatura atual R$ 0,00.
2. **Given** o cartão "Azul" existe, **When** a pessoa cadastra outro cartão "AZUL", **Then** o app avisa que o nome já existe.
3. **Given** o cartão tem compras, **When** a pessoa tenta excluí-lo, **Then** só é possível arquivar (RT-11); faturas com valor em aberto continuam aparecendo na visão do mês.

---

### User Story 2 - Lançar Compra à vista no cartão (Priority: P1)

A pessoa lança uma compra no cartão pelo celular; o app mostra em qual fatura ela vai cair antes de
salvar.

**Why this priority**: é o lançamento mais frequente da planilha (dezenas de "Uber" por mês).

**Independent Test**: lançar "Uber" R$ 12,95 no cartão Azul e ver a compra na fatura correta.

**Regra de fatura (vira teste automatizado)** — cartão Azul: fecha dia 18, vence dia 25.

| Data da compra | Fatura (mês de vencimento) | Por quê |
|---|---|---|
| 17/03/2026 | 2026-03 (vence 25/03) | fechamento 18/03 é posterior à compra |
| 18/03/2026 | 2026-04 (vence 25/04) | compra no dia do fechamento vai para a próxima |
| 31/12/2026 | 2027-01 (vence 25/01/2027) | |

Cartão Verde: fecha dia 1, vence dia 10. Compra 31/01/2026 → fatura 2026-02 (fecha 01/02, vence
10/02). Compra 01/02/2026 → fatura 2026-03.

Cartão X: fecha dia 28, vence dia 5 (fechamento no mês anterior ao vencimento). Fatura 2026-03 fecha
28/02/2026 e vence 05/03/2026. Compra 27/02/2026 → 2026-03. Compra 28/02/2026 → 2026-04.

Cartão Y: fecha dia 30, vence dia 8. Fatura 2026-03 fecha 28/02/2026 (RT-03) e vence 08/03/2026.

**Acceptance Scenarios**:

1. **Given** hoje é 17/03/2026 e o cartão Azul acima, **When** a pessoa escolhe a aba "Cartão", digita `12,95`, descreve "Uber" e salva, **Then** é criada uma Compra de Débito de R$ 12,95, data 17/03/2026, na fatura Azul 2026-03; antes de salvar o formulário mostrava "Fatura de março — vence 25/03".
2. **Given** a pessoa digita `-99,90` numa compra "Estorno Óculos", **When** salva, **Then** é criada uma Compra de natureza Crédito de R$ 99,90 e o app avisa "Lançado como estorno/crédito" (RT-02).
3. **Given** a pessoa digita `239,8/2`, **When** salva, **Then** o valor é R$ 119,90 (RT-05).
4. **Given** compras na tabela "Regra de fatura" acima, **When** salvas, **Then** cada uma cai na fatura indicada.

---

### User Story 3 - Compra parcelada (Priority: P1)

A pessoa lança uma compra em N vezes informando o valor total (ou o valor da parcela) e o app cria
todas as parcelas, cada uma na fatura certa, com o número "k/N" no nome.

**Why this priority**: hoje ela copia cada parcela à mão em cada mês; é a maior fonte de erro.

**Independent Test**: lançar R$ 100,00 em 3x e verificar as 3 parcelas nas 3 faturas seguidas.

**Acceptance Scenarios**:

1. **Given** cartão Azul (fecha 18, vence 25), **When** a pessoa lança "Casa China" com valor total R$ 100,00 em 3x, data 28/01/2026, **Then** são criadas 3 parcelas: "Casa China 1/3" R$ 33,34 na fatura 2026-02, "Casa China 2/3" R$ 33,33 na 2026-03 e "Casa China 3/3" R$ 33,33 na 2026-04 (RT-02), todas com data 28/01/2026.
2. **Given** o modo "valor da parcela", **When** a pessoa lança "Shoppe Casa" R$ 102,79 em 8x, **Then** são criadas 8 parcelas de R$ 102,79 (total R$ 822,32).
3. **Given** antes de salvar, **When** a pessoa escolhe 3x, **Then** o formulário mostra "3x de R$ 33,34 (1ª) e R$ 33,33 — primeira fatura: fevereiro".
4. **Given** uma compra parcelada, **When** a pessoa edita a descrição de qualquer parcela, **Then** o app pergunta "Alterar só esta parcela ou todas?" e aplica conforme a escolha.
5. **Given** uma compra parcelada, **When** a pessoa exclui uma parcela, **Then** o app oferece "Só esta", "Esta e as seguintes" ou "Todas"; RT-09 vale para o grupo excluído.

---

### User Story 4 - Cadastrar parcelamento que já está em andamento (Priority: P1)

Ao começar a usar o app, a pessoa tem compras parceladas em curso (ex.: "Shoppe Casa 5/8"). Ela
informa a parcela em que está e o app cria só as restantes.

**Why this priority**: sem isso, a migração da planilha para o app fica errada desde o primeiro mês.

**Independent Test**: cadastrar "Shoppe Casa", parcela atual 5 de 8, R$ 102,74, e ver 4 parcelas (5/8 a 8/8).

**Acceptance Scenarios**:

1. **Given** cartão Azul, **When** a pessoa marca "Parcelamento em andamento" e informa: descrição "Shoppe Casa", total de parcelas 8, próxima parcela 5, valor da parcela R$ 102,74, fatura da parcela 5 = 2026-11, data da compra 20/06/2026, **Then** são criadas as parcelas 5/8 (fatura 2026-11), 6/8 (2026-12), 7/8 (2027-01) e 8/8 (2027-02), de R$ 102,74 cada, com data 20/06/2026.
2. **Given** a próxima parcela informada é maior que o total, **When** tenta salvar, **Then** o app impede.

---

### User Story 5 - Ver e pagar a Fatura (Priority: P1)

A pessoa abre a fatura de um cartão, vê as compras e o total, e registra o pagamento (total ou
parcial) a partir de uma carteira. A fatura aparece sozinha como saída no mês do vencimento.

**Why this priority**: fecha o ciclo cartão → saída do mês, que na planilha era a linha "Cartão Azul".

**Independent Test**: com o exemplo abaixo, conferir fatura, visão do mês e saldo da carteira antes e depois de pagar.

**Exemplo numérico (vira teste automatizado)** — hoje 20/03/2026; cartão Azul (fecha 18, vence
25, carteira de pagamento "Azul"); carteira Azul com saldo atual R$ 2.000,00. Fatura 2026-03:

| Compra | Natureza | Valor |
|---|---|---|
| Uber | Débito | 12,95 |
| Casa China 2/3 | Débito | 33,33 |
| Shopee | Débito | 44,00 |
| Estorno Óculos | Crédito | 99,90 |
| Lanche | Débito | 209,62 |

Total = 12,95 + 33,33 + 44,00 + 209,62 − 99,90 = **R$ 200,00**. Situação: **Fechada** (hoje ≥ fechamento
18/03 e ≤ vencimento 25/03). Na visão de março/2026, Saídas: linha "Fatura Azul" Previsto 200,00,
Realizado 0,00.

**Acceptance Scenarios**:

1. **Given** o exemplo, **When** a pessoa toca na caixinha da linha "Fatura Azul" na visão de março, **Then** é registrado um Pagamento de fatura de R$ 200,00, data 20/03/2026, carteira Azul; a fatura fica **Paga**; Saídas Realizado de março aumenta 200,00; saldo atual da carteira Azul passa a R$ 1.800,00.
2. **Given** o exemplo, **When** a pessoa registra pagamento parcial de R$ 150,00, **Then** a fatura mostra Pago 150,00 e Restante 50,00, continua Fechada, e em março Previsto 200,00 e Realizado 150,00.
3. **Given** o pagamento parcial acima e hoje 26/03/2026, **When** a pessoa abre a fatura, **Then** a situação é **Vencida** com Restante R$ 50,00.
4. **Given** a fatura Vencida com Restante 50,00, **When** a pessoa escolhe "Levar restante para a próxima fatura", **Then** são criadas duas compras ligadas: na fatura 2026-03 um Crédito "Saldo levado para 04/2026" de R$ 50,00 (a fatura fica Paga, total 150,00) e na fatura 2026-04 um Débito "Saldo da fatura 03/2026" de R$ 50,00. Excluir uma delas exclui as duas.
5. **Given** a fatura com Restante 50,00, **When** a pessoa tenta registrar pagamento de R$ 60,00, **Then** o app impede ("O valor em aberto é R$ 50,00").
6. **Given** a fatura Paga, **When** a pessoa desmarca a caixinha da linha da fatura na visão do mês, **Then** todos os pagamentos dessa fatura são excluídos (com RT-09) e o saldo da carteira volta.
7. **Given** a fatura 2026-03 está Paga, **When** a pessoa lança nova compra que cai nela, **Then** o app avisa "Esta fatura já foi paga e voltará a ter valor em aberto" e, se confirmado, salva.

---

### User Story 6 - Visão dos cartões e limite (Priority: P2)

A pessoa vê, para cada cartão, a fatura atual (aberta), a próxima, as anteriores e o limite
disponível.

**Why this priority**: substitui o bloco "Cartões" (totais por cartão) da planilha.

**Independent Test**: cartão com limite R$ 3.000,00 e parcelas futuras; conferir limite disponível.

**Acceptance Scenarios**:

1. **Given** cartão Azul com limite R$ 3.000,00, fatura 2026-03 com Restante 200,00 e parcelas futuras somando 466,66 nas faturas 2026-04 e 2026-05, **When** a pessoa abre o cartão, **Then** vê limite disponível R$ 2.333,34.
2. **Given** um cartão, **When** a pessoa navega pelas faturas, **Then** passa de mês em mês (anteriores e futuras), vendo para cada uma: datas de fechamento e vencimento, situação, total, pago, restante e a lista de compras (RT-12).
3. **Given** um cartão sem limite cadastrado, **When** exibido, **Then** não mostra limite disponível.

---

### User Story 7 - Conferir compras e mover compra de fatura (Priority: P3)

A pessoa marca compras como "conferidas" ao bater com o app do banco e, quando o banco colocou uma
compra em outra fatura, move a compra para a fatura correta.

**Why this priority**: refinamento; a planilha tinha a caixinha na coluna do cartão.

**Independent Test**: conferir 2 de 3 compras; mover uma compra para a fatura seguinte.

**Acceptance Scenarios**:

1. **Given** uma fatura com 3 compras somando R$ 100,00, **When** a pessoa marca como conferidas compras que somam R$ 60,00, **Then** a fatura mostra "Conferido R$ 60,00 de R$ 100,00". Conferir não altera nenhum total da visão do mês.
2. **Given** uma compra à vista na fatura 2026-03, **When** a pessoa escolhe "Mover para a próxima fatura", **Then** ela passa para 2026-04 e fica marcada como "movida manualmente".
3. **Given** a parcela 2/4 de uma compra, **When** a pessoa a move para a próxima fatura e escolhe "esta e as seguintes", **Then** as parcelas 2/4, 3/4 e 4/4 avançam uma fatura cada.

---

### Edge Cases

- Alteração dos dias de fechamento/vencimento de um cartão: vale para faturas cujo novo fechamento seja posterior a hoje; compras dessas faturas são redistribuídas pela regra (FR-008), exceto as movidas manualmente. Faturas já fechadas mantêm suas datas e compras.
- Total da fatura negativo (mais créditos que débitos): situação "Sem valor a pagar" com o texto "Crédito de R$ X"; a ação "Levar restante para a próxima fatura" funciona no sentido inverso (Débito nesta, Crédito na próxima).
- Estorno de compra parcelada: lançado como Crédito à vista na fatura em que o banco o colocou; o app não desfaz parcelas automaticamente.
- Compra com data muito antiga numa fatura já paga: aplica o cenário 5.7.
- Pagamento antecipado (fatura ainda Aberta): permitido; se o Restante chegar a 0, a fatura fica Paga, mas volta a ter valor em aberto se entrar nova compra (cenário 5.7; o aviso de "já foi paga" só aparece se hoje ≥ data de fechamento da fatura).
- Cartão arquivado: suas faturas continuam visíveis e pagáveis; não aparece na escolha de cartão para novas compras.
- Uma estimativa de juros/encargos não é calculada pelo app; a pessoa lança como Compra de Débito ("Juros Verde").

## Requirements *(mandatory)*

### Functional Requirements

**Cartão**

- **FR-001**: Cartão MUST ter: nome (1 a 40 caracteres, único no Espaço sem diferenciar maiúsculas/minúsculas e acentos), dia de fechamento (1 a 31), dia de vencimento (1 a 31), Carteira de pagamento padrão (ativa), cor, limite opcional (RT-02). Arquivamento/exclusão: RT-11.

**Fatura**

- **FR-002**: Fatura é identificada por Cartão + mês de vencimento (AAAA-MM). Existe para todo mês em que haja ao menos uma Compra ou Pagamento, e sempre para a fatura atual de cada cartão ativo.
- **FR-003**: Data de vencimento da fatura do mês V = dia de vencimento em V (RT-03).
- **FR-004**: Data de fechamento da fatura do mês V = dia de fechamento no mês V se o dia de fechamento for menor que o dia de vencimento; caso contrário, dia de fechamento no mês anterior a V (RT-03 para meses curtos).
- **FR-005**: Total da fatura = soma das Compras de Débito − soma das Compras de Crédito nela. Pago = soma dos Pagamentos dela. Restante = Total − Pago.
- **FR-006**: Situação da fatura, avaliada nesta ordem (a primeira condição verdadeira define a situação; por isso uma fatura paga antes do fechamento aparece como Paga, não como Aberta. Quando outra regra depende do período da fatura, ela usa a data de fechamento, não a situação): (1) Total ≤ 0 → "Sem valor a pagar"; (2) Restante ≤ 0 → Paga; (3) hoje < data de fechamento → Aberta; (4) hoje ≤ data de vencimento → Fechada; (5) senão → Vencida.
- **FR-007**: Fatura atual de um cartão = a fatura com a menor data de fechamento posterior a hoje (a que está recebendo compras de hoje).

**Compra**

- **FR-008**: A fatura de uma Compra (ou da 1ª parcela) com data D é a fatura do cartão com a menor data de fechamento estritamente posterior a D.
- **FR-009**: Compra MUST ter: descrição (RT-05a), valor (RT-02, RT-05), data, Cartão (ativo), natureza (Débito ou Crédito; padrão Débito), número de parcelas (1 a 48; padrão 1 = à vista), conferida (sim/não; padrão não), observação opcional (até 500 caracteres). Compras de Crédito são sempre à vista.
- **FR-010**: O formulário de lançamento rápido (002 FR-008 a FR-013) MUST ganhar a aba "Cartão". Na aba Cartão o campo Carteira é substituído por Cartão (padrão: último cartão usado naquele aparelho; se não houver ou estiver arquivado, o primeiro cartão ativo em ordem alfabética), há o campo Parcelas e não há Situação. Após esta feature, o tipo padrão no primeiro uso passa a ser "Cartão". O formulário mostra, antes de salvar, a fatura em que a compra (ou a 1ª parcela) cairá, com mês e data de vencimento.
- **FR-011**: Ao escolher uma sugestão de descrição (002 FR-011) na aba Cartão, o Cartão da compra mais recente com essa descrição MUST ser aplicado se o campo não tiver sido alterado manualmente.

**Parcelamento**

- **FR-012**: Compra parcelada em N vezes MUST gerar N Compras-parcela ligadas, com descrição exibida "<descrição> k/N", a mesma data da compra, a parcela 1 na fatura da FR-008 e a parcela k na fatura do mês seguinte ao da parcela k−1.
- **FR-013**: Modo "valor total": valores das parcelas pela divisão da RT-02. Modo "valor da parcela": todas as parcelas iguais ao valor informado; total = valor × N.
- **FR-014**: Parcelamento em andamento: a pessoa informa total de parcelas N, próxima parcela p (1 ≤ p ≤ N), valor da parcela, fatura da parcela p (padrão: fatura atual do cartão) e data da compra original; o sistema cria apenas as parcelas p…N, a parcela k na fatura (k − p) meses após a fatura da parcela p.
- **FR-015**: Edição em parcela: descrição, observação e tags (004) perguntam "só esta / todas"; valor e conferida afetam só a parcela editada; cartão e data da compra afetam todas as parcelas e redistribuem as faturas pela FR-008/FR-012 (a parcela 1 de um parcelamento em andamento mantém a fatura escolhida e as seguintes a sequência), exceto parcelas movidas manualmente; alterar N ou o valor total recria todas as parcelas, permitido apenas se nenhuma parcela estiver em fatura Paga.
- **FR-016**: Exclusão em parcela oferece "Só esta", "Esta e as seguintes" ou "Todas" (RT-09 para o conjunto excluído).

**Pagamento de fatura**

- **FR-017**: Pagamento de fatura MUST ter: fatura, valor (RT-02, RT-05; padrão = Restante; máximo = Restante no momento), data (padrão hoje), Carteira (padrão: carteira de pagamento do cartão). É sempre efetivado. Mês de referência = mês de vencimento da fatura (RT-04).
- **FR-018**: Uma fatura pode ter vários pagamentos. Pagamentos podem ser editados e excluídos (RT-09); ao editar, o máximo é Restante + valor atual do pagamento editado.
- **FR-019**: A caixinha da linha da fatura na visão do mês: marcar = registra um pagamento com os valores padrão da FR-017; desmarcar (só disponível se houver pagamentos) = exclui todos os pagamentos da fatura (RT-09).
- **FR-020**: "Levar restante para a próxima fatura" está disponível quando hoje ≥ data de fechamento da fatura e Restante ≠ 0. Cria o par de compras ligadas descrito no cenário 5.4 (ou o inverso quando Restante < 0). Excluir uma exclui a outra.

**Integração com a visão do mês (002)**

- **FR-021**: Cada fatura com vencimento no mês M MUST aparecer na lista de Saídas de M como "Fatura <Cartão>", com situação, Total, Pago e Restante, abrindo a fatura ao toque.
- **FR-022**: Valor da fatura nas Saídas(M) (002 FR-015): Previsto = maior valor entre Total, Pago e 0; Realizado = Pago. (Ponto de extensão: a 007 acrescenta a Estimativa de fatura, que vale enquanto hoje < data de fechamento — 007 FR-010/FR-011.)
- **FR-023**: Com filtro por Carteira, a parte prevista da fatura atribuída à Carteira X = pagamentos feitos com X + (se X for a carteira de pagamento do cartão) o maior valor entre Restante e 0. A parte realizada = pagamentos feitos com X.
- **FR-024**: Pagamentos de fatura MUST reduzir o saldo atual da Carteira usada (002 FR-003).

**Cartões, limite e conferência**

- **FR-025**: A tela de Cartões MUST mostrar, por cartão: fatura atual (total e data de fechamento), próxima fatura com valor já comprometido, faturas fechadas/vencidas com valor em aberto, e limite disponível quando houver limite.
- **FR-026**: Limite disponível = limite − soma do maior valor entre Restante e 0 de todas as faturas do cartão que não estejam Pagas (incluindo faturas futuras com parcelas). Pode ficar negativo (exibido em vermelho).
- **FR-027**: A fatura MUST mostrar "Conferido R$ X de R$ Y", sendo X a soma (Débitos − Créditos) das compras conferidas e Y o Total. Conferir não afeta outros cálculos.
- **FR-028**: A pessoa MUST poder mover uma compra para a fatura anterior ou seguinte do mesmo cartão; em parcela, perguntando "só esta / esta e as seguintes". Compras movidas ficam marcadas como "movida manualmente".

### Key Entities

- **Cartão**: nome, dia de fechamento, dia de vencimento, Carteira de pagamento padrão, limite (opcional), cor, arquivado.
- **Fatura**: Cartão, mês de vencimento, data de fechamento, data de vencimento; Total, Pago, Restante e Situação são calculados.
- **Compra**: descrição, valor, natureza, data, Cartão, Fatura, conferida, movida manualmente, observação; se parcela: grupo de parcelamento, número k e total N.
- **Grupo de parcelamento**: descrição base, total de parcelas, modo (valor total / valor da parcela / em andamento).
- **Pagamento de fatura**: Fatura, Carteira, valor, data.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Lançar uma compra à vista no cartão pelo celular leva no máximo 15 segundos e 4 toques.
- **SC-002**: Todos os exemplos de "Regra de fatura" e o exemplo numérico da User Story 5 passam como testes automatizados.
- **SC-003**: A pessoa nunca precisa digitar uma parcela à mão em meses seguintes.
- **SC-004**: Para qualquer fatura, a soma das partes por Carteira (FR-023) é igual ao valor da fatura nas Saídas de todas as Carteiras (FR-022) (teste automatizado de consistência).

## Assumptions

- Cartões adicionais (de outras pessoas) são cadastrados como cartões próprios; quem usou é indicado por Tag de acerto (004).
- O cartão "Léo" da planilha (cartão de outra pessoa que ela usa e reembolsa) é cadastrado como um cartão normal.
- Não há cálculo automático de juros, IOF, anuidade, rotativo, antecipação de parcelas nem leitura de fatura do banco (Open Finance) na v1.
- Ajuste pontual das datas de uma fatura específica (ex.: vencimento caiu num feriado) está fora de escopo; a pessoa pode mover compras manualmente (US7).
