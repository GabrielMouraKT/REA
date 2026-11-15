# Autenticação

Decisão do dono (15/11/2026): usar o login do **GitHub CLI** (`gh`).

- O REA **não pede senha** e **não lê nem guarda token**. O `gh` guarda a credencial no cofre do Windows.
- **Conectar / trocar conta:** Git Control Center › Accounts › _Conectar / trocar conta_ abre o PowerShell com
  `gh auth login` (fluxo oficial: navegador ou código de dispositivo). Depois clique em _Atualizar_.
- **Desconectar:** _Desconectar_ abre `gh auth logout`.
- **Revogar de vez:** github.com › Settings › Applications › Authorized OAuth Apps › GitHub CLI.
- **Consultar:** a tela mostra login, nome, permissões do login (scopes) e organizações visíveis.
- **Permissões mínimas:** o login padrão do `gh` pede `repo`, `read:org` e `gist`. Para criar repositórios em
  organizações é preciso ter permissão na organização.
- **Operações Git (push/pull) em HTTPS** usam o gerenciador de credenciais do Git; o `gh auth login` oferece configurar
  o Git para usar a mesma conta (`gh auth setup-git`).
