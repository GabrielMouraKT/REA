# REA ADVANCED SUITE — GIT CONTROL CENTER

> Requisito do dono, recebido em 15/11/2026. Texto integral preservado abaixo. Status e decisões em `IMPLEMENTATION_PLAN.md`.

## Gerenciamento completo de Git, GitHub e repositórios

### 1. OBJETIVO

Implementar um módulo completo de gerenciamento Git dentro do REA Advanced Suite.

O módulo deverá permitir que o usuário gerencie projetos locais e remotos, conecte contas, crie repositórios, execute operações Git, acompanhe alterações e administre o ciclo de desenvolvimento.

O módulo deverá funcionar em dois modos:

- Manual: usuário executa operações pela interface.
- Claude: usuário solicita operações em linguagem natural e Claude coordena a execução.

Claude deverá utilizar os mesmos serviços Git disponibilizados pela interface manual.

### 2. AUTENTICAÇÃO GITHUB

Criar tela:

GIT ACCOUNT CONNECTION

Permitir:

- Conectar conta GitHub.
- Realizar login pelo fluxo oficial OAuth ou dispositivo, conforme suporte oficial.
- Autorizar permissões solicitadas.
- Consultar conta conectada.
- Consultar organizações acessíveis.
- Desconectar conta.
- Revogar autorização.
- Reconectar quando necessário.

Não solicitar senha do GitHub em formulário próprio.

Não armazenar tokens em texto simples.

Utilizar armazenamento seguro de credenciais do Windows.

Solicitar somente as permissões necessárias.

### 3. REPOSITORY MANAGER

Permitir:

- Criar repositório.
- Criar repositório público.
- Criar repositório privado.
- Clonar repositório.
- Importar projeto local.
- Conectar projeto existente a um remoto.
- Desconectar remoto.
- Renomear repositório, quando suportado.
- Arquivar repositório.
- Excluir repositório mediante confirmação.
- Consultar descrição.
- Consultar URL.
- Consultar visibilidade.
- Consultar data de atualização.
- Consultar tamanho.
- Consultar branches.
- Consultar colaboradores e permissões, conforme autorização.

Permitir criação de repositórios em contas pessoais e organizações quando o usuário possuir autorização.

### 4. GIT OPERATIONS

Implementar interface para as operações:

- Init.
- Clone.
- Status.
- Add.
- Commit.
- Log.
- Diff.
- Show.
- Branch.
- Checkout.
- Switch.
- Merge.
- Rebase.
- Fetch.
- Pull.
- Push.
- Stash.
- Tag.
- Reset.
- Revert.
- Cherry-pick.
- Remote.
- Blame.
- Clean, com confirmação e pré-visualização.
- Bisect.
- Submodule.

Apresentar resultados e mensagens de erro em linguagem acessível.

### 5. COMMIT MANAGER

Criar painel de commits.

Apresentar:

- Arquivos modificados.
- Arquivos novos.
- Arquivos excluídos.
- Arquivos renomeados.
- Diff lado a lado.
- Seleção individual de arquivos.
- Seleção parcial de alterações quando suportada.
- Campo de mensagem do commit.
- Histórico.
- Autor.
- Data.
- Hash.
- Branch.

Permitir commits manuais e commits assistidos por Claude.

Claude deverá sugerir mensagens de commit descritivas e organizadas.

Nunca incluir credenciais, senhas, tokens ou arquivos sensíveis em commits.

### 6. BRANCH MANAGER

Permitir:

- Criar branch.
- Renomear branch.
- Alternar branch.
- Excluir branch.
- Comparar branches.
- Visualizar histórico.
- Identificar divergências.
- Fazer merge.
- Rebase.
- Resolver conflitos com interface visual.

Criar proteção contra operações que possam perder alterações não salvas.

### 7. REMOTE SYNC

Permitir:

- Fetch.
- Pull.
- Push.
- Consultar estado de sincronização.
- Comparar local e remoto.
- Visualizar commits pendentes.
- Identificar divergências.
- Resolver conflitos.
- Consultar histórico de sincronização.

Antes de operações que possam sobrescrever trabalho remoto ou local, apresentar resumo e solicitar aprovação.

Não realizar force push automaticamente.

### 8. GITHUB MANAGEMENT

Integrar recursos da API oficial do GitHub.

Permitir:

- Criar e consultar repositórios.
- Gerenciar branches.
- Criar Pull Requests.
- Consultar Pull Requests.
- Aprovar ou solicitar alterações, conforme permissões.
- Criar Issues.
- Consultar e atualizar Issues.
- Criar Releases.
- Gerenciar tags.
- Consultar Actions e workflows.
- Consultar resultados de CI/CD.
- Consultar colaboradores.
- Consultar notificações.
- Consultar milestones.
- Gerenciar labels.
- Consultar histórico de atividade.

Implementar recursos de organizações somente conforme as permissões concedidas.

### 9. PULL REQUEST MANAGER

Criar interface para:

- Selecionar branch de origem.
- Selecionar branch de destino.
- Comparar alterações.
- Exibir commits.
- Exibir arquivos modificados.
- Criar descrição.
- Criar Pull Request.
- Consultar revisões.
- Consultar comentários.
- Atualizar Pull Request.
- Fazer merge quando autorizado.

Claude poderá analisar as alterações e preparar descrições e revisões técnicas.

### 10. CLAUDE GIT ASSISTANT

Integrar o Claude ao Git Control Center.

O usuário poderá solicitar:

"Crie um repositório privado para este projeto."

"Faça um commit das alterações."

"Crie uma branch para implementar o novo módulo."

"Verifique o que foi modificado desde o último commit."

"Compare a branch atual com a principal."

"Prepare uma Pull Request."

"Analise os conflitos."

"Verifique se esqueci algum arquivo importante."

"Faça uma revisão das alterações antes do push."

Claude deverá:

1. Identificar o projeto correto.
2. Verificar o estado do repositório.
3. Consultar as alterações.
4. Elaborar um plano.
5. Executar operações permitidas.
6. Solicitar aprovação quando necessário.
7. Verificar o resultado.
8. Explicar ao usuário o que foi realizado.

Se o usuário for iniciante, Claude deverá explicar o significado de commit, branch, push, pull e demais conceitos quando necessário.

### 11. INTEGRAÇÃO COM PROJECT MANAGER

Cada projeto deverá possuir:

- Diretório local.
- Repositório remoto.
- Conta associada.
- Branch principal.
- Branch atual.
- Estado de sincronização.
- Último commit.
- Histórico.
- Configurações Git.

Permitir vários repositórios por projeto quando necessário.

Permitir que o usuário gerencie todos os projetos a partir do REA.

### 12. SEGURANÇA

Implementar:

- Confirmação antes de excluir repositórios.
- Confirmação antes de force push.
- Confirmação antes de reset destrutivo.
- Pré-visualização antes de clean.
- Proteção de arquivos sensíveis.
- Verificação de secrets antes do commit.
- Auditoria de operações.
- Histórico de comandos.
- Proteção de alterações não commitadas.
- Verificação de permissões remotas.

Nunca fazer push, merge, exclusão ou publicação sem respeitar as permissões e confirmações configuradas.

### 13. INTERFACE

Criar aba:

GIT CONTROL CENTER

Submenus:

- Dashboard.
- Accounts.
- Repositories.
- Commit Manager.
- Branch Manager.
- Remote Sync.
- Pull Requests.
- Issues.
- Releases.
- Git History.
- Git Settings.

Criar indicadores visuais para:

- Clean.
- Modified.
- Untracked.
- Ahead.
- Behind.
- Diverged.
- Conflict.
- Synced.

### 14. DOCUMENTAÇÃO

Criar:

docs/GIT_CONTROL_CENTER/README.md
docs/GIT_CONTROL_CENTER/ARCHITECTURE.md
docs/GIT_CONTROL_CENTER/AUTHENTICATION.md
docs/GIT_CONTROL_CENTER/REPOSITORIES.md
docs/GIT_CONTROL_CENTER/GIT_OPERATIONS.md
docs/GIT_CONTROL_CENTER/GITHUB_INTEGRATION.md
docs/GIT_CONTROL_CENTER/SECURITY.md
docs/GIT_CONTROL_CENTER/USER_GUIDE.md

### 15. TESTES

Validar:

- Autenticação.
- Criação de repositórios.
- Clone.
- Init.
- Commit.
- Branch.
- Merge.
- Rebase.
- Fetch.
- Pull.
- Push.
- Conflitos.
- Pull Requests.
- Issues.
- Releases.
- Permissões.
- Recuperação de falhas.
- Proteção contra perda de dados.

Utilizar repositórios de teste para operações destrutivas.

### 16. RESULTADO FINAL

O REA deverá possuir gerenciamento Git integrado, permitindo que o usuário trabalhe sem precisar abrir o terminal ou acessar o navegador para as operações comuns.

Claude deverá ser capaz de interpretar solicitações, coordenar operações e orientar o usuário.

A operação manual deverá continuar integralmente disponível.

A arquitetura deverá permitir adicionar outros provedores Git futuramente, sem depender exclusivamente do GitHub.
