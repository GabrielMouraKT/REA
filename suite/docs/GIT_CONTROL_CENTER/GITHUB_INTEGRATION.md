# Integração com o GitHub

Feito pelo GitHub CLI (`gh`), que usa a API oficial do GitHub com o login do usuário.

| Recurso                                               | Situação                   |
| ----------------------------------------------------- | -------------------------- |
| Conta, permissões e organizações                      | Pronto (Accounts)          |
| Listar repositórios                                   | Pronto (até 100, Accounts) |
| Criar repositório público/privado e configurar origin | Pronto (Repositories)      |
| Pull Requests (criar, consultar, revisões, merge)     | Próxima etapa              |
| Issues, labels, milestones                            | Próxima etapa              |
| Releases e tags                                       | Próxima etapa              |
| Actions / resultados de CI                            | Próxima etapa              |
| Colaboradores, notificações, atividade                | Próxima etapa              |

Recursos de organização só aparecem conforme as permissões da conta conectada.
Teste com a conta real (somente leitura): `REA_TEST_GITHUB=1` + `dotnet test --project tests/REA.ToolTests`.
