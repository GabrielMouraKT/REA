# Git Control Center

Gerenciamento de Git e GitHub dentro do REA, sem abrir terminal nem navegador para as operações comuns.
Requisito integral: `docs/REQUISITOS/05_GIT_CONTROL_CENTER.md`.

| Documento                                      | Conteúdo                                                          |
| ---------------------------------------------- | ----------------------------------------------------------------- |
| [ARCHITECTURE.md](ARCHITECTURE.md)             | Camadas, serviço único para interface e Claude, dados guardados   |
| [AUTHENTICATION.md](AUTHENTICATION.md)         | Conta do GitHub via GitHub CLI (`gh`), sem senha nem token no REA |
| [REPOSITORIES.md](REPOSITORIES.md)             | Vincular, iniciar, clonar, publicar no GitHub, desvincular        |
| [GIT_OPERATIONS.md](GIT_OPERATIONS.md)         | Operações disponíveis, proteções e mensagens                      |
| [GITHUB_INTEGRATION.md](GITHUB_INTEGRATION.md) | O que já usa a API do GitHub e o que vem depois                   |
| [SECURITY.md](SECURITY.md)                     | Segredos, confirmações, auditoria, URLs permitidas                |
| [USER_GUIDE.md](USER_GUIDE.md)                 | Passo a passo para iniciantes                                     |

## Situação (15/11/2026)

| Tela                                                                                        | Situação                                                  |
| ------------------------------------------------------------------------------------------- | --------------------------------------------------------- |
| Dashboard, Accounts, Repositories, Commit Manager, Branch Manager, Remote Sync, Git History | **Pronto** (testado com Git real)                         |
| Pull Requests, Issues, Releases, Git Settings                                               | Próxima etapa (aparecem no menu como planejadas)          |
| Rebase, tag, reset, revert, cherry-pick, blame, clean, bisect, submodule                    | Próxima etapa (serviço)                                   |
| Modo Claude (pedidos em linguagem natural)                                                  | Junto do Claude Control Center, usando este mesmo serviço |
