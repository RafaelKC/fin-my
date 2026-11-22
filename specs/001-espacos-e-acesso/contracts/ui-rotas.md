# Contrato de interface — 001

Rotas do front (Angular) e o que cada tela precisa garantir. Textos visíveis em pt-BR.

| Rota | Acesso | Tela | Requisitos |
|---|---|---|---|
| `/entrar` | público | E-mail, senha, "Entrar", links "Criar conta" e "Esqueci minha senha" | US2; erro único "E-mail ou senha incorretos"; bloqueio mostra quando poderá tentar de novo |
| `/criar-conta` | público | Nome, e-mail, senha (mostrar/ocultar), "Criar conta" | US1; senha < 8 explica o requisito; e-mail já cadastrado mostra a mensagem do cenário 1.2 com atalhos para entrar/recuperar |
| `/esqueci-senha` | público | E-mail, "Enviar link" | US4; sempre a mesma mensagem de confirmação |
| `/redefinir-senha?token=` | público | Nova senha, "Salvar" | US4; token inválido/expirado → mensagem e botão "Pedir novo link" |
| `/` | logado | Casca do app: barra inferior (celular) / menu lateral (≥ 1024 px), nome do Espaço no topo; conteúdo inicial = página "Mês" vazia (placeholder até a 002) | arquitetura §9 |
| `/perfil` | logado | Nome do usuário, nome do Espaço, trocar senha, "Sair" | US5 |

Regras transversais da casca:
- Ao abrir o app: tentar `/auth/refresh`; sucesso → rota pedida; falha → `/entrar` (guardando a rota pedida para voltar depois do login).
- Rotas logadas sem sessão → `/entrar`. Rotas públicas com sessão ativa → `/`.
- PWA instalável: manifest com nome "Fin-My", ícones, `display: standalone`, cor de tema; service worker só para o app shell.
- Layout funcional a partir de 360 px; alvos de toque ≥ 44 px.
