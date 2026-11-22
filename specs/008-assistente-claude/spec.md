# Feature Specification: Lançar e consultar pelo Claude (conector + skill)

**Feature Branch**: `008-assistente-claude`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Queria uma skill + MCP para minha esposa usar no Claude dela."

> Glossário e regras transversais (RT-xx): ver `.specify/memory/constitution.md`.
> Depende de: 001 a 004 (usa 005 e 007 se existirem). Implementar depois do MVP (001–003).
> Princípio: o Claude é **mais uma porta de entrada** para as MESMAS regras. Nenhuma regra de negócio
> nova ou diferente é criada aqui; tudo que o conector faz é exatamente o que o app faria.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Conectar o Claude ao Fin-My (Priority: P1)

A pessoa adiciona o conector "Fin-My" no Claude dela, faz login com a conta do Fin-My e autoriza o
acesso. A partir daí, o Claude age no Espaço dela, com o usuário dela.

**Why this priority**: pré-requisito de tudo.

**Independent Test**: conectar, pedir "qual o saldo das minhas carteiras?" e receber a resposta certa.

**Acceptance Scenarios**:

1. **Given** uma conta Fin-My, **When** a pessoa adiciona o conector no Claude, **Then** é levada à tela de login do Fin-My, vê "O Claude poderá ver e registrar seus lançamentos no Espaço <nome>" e autoriza.
2. **Given** a conexão autorizada, **When** a pessoa abre "Perfil → Aplicativos conectados" no Fin-My, **Then** vê "Claude", desde quando está conectado e o último uso, e pode desconectar; após desconectar, o Claude não consegue mais acessar nada.
3. **Given** a senha da conta é redefinida (001 FR-012), **When** o Claude tenta usar a conexão, **Then** precisa autorizar de novo.
4. **Given** a conexão, **When** o Claude faz qualquer operação, **Then** ela vale apenas para o Espaço da pessoa (RT-01).

---

### User Story 2 - Lançar conversando (Priority: P1)

A pessoa escreve ou fala naturalmente ("gastei 32,90 no ifood no verde, metade é da Bruna") e o
Claude registra o lançamento, respondendo o que foi feito e onde caiu.

**Why this priority**: é o motivo da integração: lançar sem abrir o app.

**Independent Test**: pedir um lançamento de compra e conferir no app.

**Acceptance Scenarios**:

1. **Given** cartão "Verde" e Tag de acerto "Bruna", **When** a pessoa diz "gastei 32,90 no ifood no verde, metade é da Bruna", **Then** são criadas duas compras de R$ 16,45 (uma sem tag de acerto e outra com "Bruna", 004 US4), com data de hoje, e o Claude responde informando valores, cartão e a fatura (mês e vencimento) em que caíram.
2. **Given** a pessoa diz "comprei um tênis de 293,70 em 6x no azul", **Then** é criado o parcelamento em 6x (003) e a resposta informa o valor das parcelas e a primeira fatura.
3. **Given** a pessoa diz "recebi 150 da Carla" sem dizer a carteira, **Then** a Entrada é criada na carteira definida pela FR-005 e a resposta diz qual carteira foi usada.
4. **Given** a pessoa cita um cartão, carteira ou tag que não existe ou que corresponde a mais de um cadastro (ex.: "cartão do banco" com dois cartões), **When** o Claude tenta lançar, **Then** nada é criado e o Claude pergunta qual é, listando as opções.
5. **Given** um lançamento acabou de ser feito pelo Claude, **When** a pessoa diz "não, desfaz", **Then** o lançamento (ou o grupo de parcelas/divisão) é excluído com a mesma regra de desfazer do app, permitida por até 10 minutos após a criação.
6. **Given** um lançamento criado pelo Claude, **When** visto no app, **Then** mostra a marca "via Claude".

---

### User Story 3 - Perguntar sobre as finanças (Priority: P1)

A pessoa pergunta "quanto tenho pra gastar esse mês?", "quanto tá a fatura do azul?", "quanto a
Bruna me deve?" e recebe respostas com os mesmos números do app.

**Why this priority**: consulta rápida sem abrir telas.

**Independent Test**: comparar as respostas com as telas do app para os mesmos dados.

**Acceptance Scenarios**:

1. **Given** dados de março, **When** a pessoa pergunta o resumo do mês, **Then** o Claude informa Entradas, Saídas, Resultado e Saldo final em Previsto e Realizado, idênticos à visão do mês (002 FR-015).
2. **Given** uma fatura, **When** a pessoa pergunta por ela, **Then** recebe situação, total, pago, restante, vencimento e, se pedir, a lista de compras (003).
3. **Given** Tags de acerto, **When** a pessoa pergunta quanto alguém deve, **Then** recebe os valores de acerto (004 FR-006..008).
4. **Given** lançamentos, **When** a pessoa pede "o que eu gastei com uber em março?", **Then** recebe a lista e o total (busca 002 FR-025, compras incluídas).

---

### User Story 4 - Ações de rotina (Priority: P2)

Pelo Claude, a pessoa marca entradas/saídas como recebidas/pagas, registra pagamento de fatura e gera
cobranças de acerto — sempre com confirmação explícita antes.

**Why this priority**: conveniência; o essencial é lançar e consultar.

**Independent Test**: pedir "paguei a fatura do azul" e conferir o pagamento no app.

**Acceptance Scenarios**:

1. **Given** a fatura Azul de março com Restante R$ 200,00, **When** a pessoa diz "paguei a fatura do azul", **Then** o Claude mostra o que vai registrar (R$ 200,00, carteira de pagamento do cartão, data de hoje) e só registra após ela confirmar.
2. **Given** a Entrada Prevista "Bruna" de R$ 150,00, **When** a pessoa diz "a Bruna me pagou", **Then** o Claude identifica a entrada, pede confirmação e a marca como Recebida.
3. **Given** um pedido de exclusão de um lançamento antigo (não criado na conversa), **When** feito, **Then** o Claude mostra o lançamento e pede confirmação antes de excluir.

---

### Edge Cases

- Valores falados com vírgula, ponto ou por extenso ("trinta e dois e noventa"): o Claude converte para número; o servidor aplica RT-02/RT-05 como em qualquer lançamento.
- Datas relativas ("ontem", "sexta passada"): convertidas para data absoluta no fuso de RT-03 antes de lançar; a resposta sempre mostra a data usada.
- Pedido fora do escopo (ex.: "cria um cartão novo", "apaga a carteira"): o Claude orienta a fazer pelo app. Cadastros (carteiras, cartões, tags, recorrências) não são criados nem alterados pelo Claude na v1 — exceto tag comum nova citada num lançamento, criada como no app (004 cenário 1.2).
- Conexão perdida no meio: nenhuma operação fica pela metade (cada operação é completa ou não acontece).
- Pedido repetido por falha de rede: não duplica (mesmo mecanismo anti-duplicação do app).

## Requirements *(mandatory)*

### Functional Requirements

**Conexão**

- **FR-001**: O Fin-My MUST oferecer um conector compatível com o Claude (web, desktop e celular) com autorização por login do próprio Fin-My e consentimento explícito.
- **FR-002**: Cada conexão age como o Usuário que autorizou, no Espaço em que ele participa (001 FR-005). Tudo que for criado ou alterado registra esse Usuário e a origem "Claude".
- **FR-003**: A pessoa MUST poder ver e desconectar aplicativos conectados no perfil. Desconectar ou redefinir a senha encerra o acesso imediatamente.

**Operações disponíveis** (cada uma aplica exatamente as regras da feature indicada)

- **FR-004**: Disponíveis na v1:
  | Operação | Regras |
  |---|---|
  | Lançar Entrada/Saída (com tags, Tag de acerto, divisão) | 002, 004 |
  | Lançar Compra (à vista, parcelada, crédito/estorno, com tags e divisão) | 003, 004 |
  | Lançar Transferência | 002 FR-021 |
  | Desfazer lançamento feito na conversa (até 10 min) | RT-09 |
  | Resumo do mês | 002 FR-015 |
  | Saldos das carteiras | 002 FR-003 |
  | Listar faturas de um cartão / detalhar fatura | 003 |
  | Pendências | 002 FR-019 |
  | Acertos por Tag de acerto | 004 |
  | Buscar lançamentos | 002 FR-025 |
  | Marcar Entrada/Saída como Recebida/Paga | 002 FR-006 |
  | Registrar pagamento de fatura | 003 FR-017 |
  | Gerar cobrança de acerto | 004 FR-010 |
  | Excluir lançamento | RT-09 |
  | Listar carteiras, cartões e tags ativos (para o Claude resolver nomes) | — |
- **FR-005**: Padrões quando a pessoa não informa: data = hoje; situação = regra padrão (002 FR-012); carteira/cartão = os do lançamento mais recente com a mesma descrição e tipo; se não houver, a Carteira principal (Entrada/Saída) ou o cartão com mais compras nos últimos 30 dias (Compra; se nenhum, o Claude pergunta). A resposta de toda criação MUST informar todos os valores efetivamente usados.
- **FR-006**: Nomes de carteiras, cartões e tags são comparados sem diferenciar maiúsculas/minúsculas e acentos. Nenhuma correspondência ou mais de uma → a operação não é executada e retorna as opções existentes.
- **FR-007**: Pagamento de fatura, marcar como Recebida/Paga, gerar cobrança e excluir lançamento não criado na mesma conversa MUST exigir confirmação explícita da pessoa antes de executar.
- **FR-008**: Lançamentos criados pelo Claude MUST exibir no app a marca "via Claude".

**Skill (instruções para o Claude)**

- **FR-009**: Uma skill "Fin-My" MUST acompanhar o conector, instruindo o Claude a: falar em pt-BR; usar o vocabulário do glossário; formatar valores como RT-10; sempre dizer a fatura em que uma compra caiu; perguntar em vez de supor quando faltar algo que não tem padrão (FR-005) ou o nome for ambíguo; pedir confirmação nos casos da FR-007; nunca inventar números (só responder com dados retornados pelo Fin-My); e oferecer "desfazer" após cada lançamento.

### Key Entities

- **Aplicativo conectado**: Usuário, nome do aplicativo, data de autorização, último uso, revogado em.
- **Origem do registro**: app ou Claude, registrada automaticamente em todo lançamento/alteração.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Lançar uma compra por mensagem leva uma única mensagem da pessoa quando cartão e descrição estão claros.
- **SC-002**: Em um roteiro de 30 pedidos típicos (lançar, consultar, pagar), 100% dos números informados pelo Claude são iguais aos do app.
- **SC-003**: Nenhuma operação da FR-007 é executada sem confirmação (teste com roteiro).
- **SC-004**: Desconectar no perfil bloqueia o acesso em até 1 minuto.

## Assumptions

- A pessoa usa um plano do Claude que permite adicionar conectores personalizados.
- O Fin-My está publicado na internet com HTTPS (necessário para o conector).
- Leitura de comprovante/nota fiscal por foto e lançamento por voz dependem do próprio Claude e não exigem nada a mais do Fin-My.
