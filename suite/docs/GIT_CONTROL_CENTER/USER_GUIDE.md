# Guia do usuário — Git Control Center

**Palavras rápidas**

- **Repositório:** pasta cujo histórico o Git guarda.
- **Commit:** uma “foto” das alterações com uma descrição.
- **Branch:** linha de trabalho separada para testar algo sem mexer na principal.
- **Push / Pull:** enviar seus commits ao servidor / receber os commits do servidor.
- **Merge:** juntar o trabalho de um branch em outro.

**Começar**

1. _Accounts_: confira se aparece “Conectado ao GitHub como …”. Se não, clique em _Conectar / trocar conta_, siga o
   login no PowerShell e depois _Atualizar_.
2. _Repositories_: escolha o projeto e clique em _Vincular pasta existente_ (pasta que já usa Git) ou _Iniciar
   repositório novo_. Para trazer um do GitHub, cole o endereço e use _Escolher pasta e clonar_.
3. Para publicar: selecione o repositório, preencha _Publicar no GitHub_ (deixe _Privado_ marcado se for só seu) e
   clique em _Criar no GitHub_.

**Dia a dia**

1. _Commit Manager_: marque os arquivos, _Selecionar para o commit_, escreva a mensagem, _Fazer commit_. Se o REA
   avisar sobre possíveis segredos, cancele e remova-os, a não ser que tenha certeza de que não são segredos.
2. _Remote Sync_: _Enviar (push)_ mostra o que vai e pede confirmação. _Receber (pull)_ traz o que há de novo.
3. _Branch Manager_: digite um nome (ex.: `recurso/login`) e _Criar e ativar_. Quando terminar, ative o branch
   principal, selecione o seu e clique em _Merge no ativo_.
4. _Git History_: clique num commit para ver o que ele mudou.

**Se algo der errado**

- “O remoto tem commits que você ainda não tem”: clique em _Receber (pull)_ e envie de novo.
- “Seu branch e o remoto divergiram”: os dois lados têm commits novos; faça merge do branch remoto em Branch Manager.
- “Há conflitos”: abra os arquivos marcados como CONFLITO, escolha o conteúdo certo, selecione-os e faça o commit;
  ou clique em _Cancelar merge_.
- “O Git não sabe seu nome/e-mail”: no PowerShell, `git config --global user.name "Seu Nome"` e
  `git config --global user.email voce@exemplo.com` (Git Settings no REA vem na próxima etapa).
