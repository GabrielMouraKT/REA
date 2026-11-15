# Matriz de funcionalidades

Legenda: **Pronto** = implementado + testado · **Planejado (Fn)** = não implementado, fase prevista.

| Área            | Funcionalidade                                       | Situação                                | Validação                                                             |
| --------------- | ---------------------------------------------------- | --------------------------------------- | --------------------------------------------------------------------- |
| Fundação        | Solução, camadas, DI, Generic Host, Serilog          | Pronto                                  | Build sem avisos                                                      |
| Fundação        | Banco SQLite + EF Core + migrações                   | Pronto                                  | `Migrations_are_all_applied_on_a_fresh_database`                      |
| Fundação        | Persistência após reinício                           | Pronto                                  | `Project_and_environment_survive_application_restart`                 |
| Projetos        | Criar, editar, listar, buscar, paginar               | Pronto                                  | Testes de integração + fluxo real via UI Automation                   |
| Projetos        | Duplicar (com ambientes, sem evidências)             | Pronto                                  | `Duplicate_copies_environments_into_a_new_project`                    |
| Projetos        | Arquivar/reativar idempotente                        | Pronto                                  | `..._archive_is_idempotent`                                           |
| Projetos        | Excluir só sem histórico                             | Pronto                                  | idem                                                                  |
| Ambientes       | CRUD por projeto, isolamento entre projetos          | Pronto                                  | `Environment_of_another_project_is_reported_as_not_found`             |
| Auditoria       | Registro legível antes/depois                        | Pronto                                  | Testes de persistência                                                |
| Backup          | Manual, automático 24 h, restauração segura          | Pronto                                  | `BackupTests` (4 cenários, incl. arquivo corrompido e path traversal) |
| Settings        | Database, Tools (detecção real)                      | Pronto                                  | `ToolDetectionTests` + verificação visual                             |
| Dashboard       | Indicadores reais                                    | Pronto (parcial: só métricas da Fase 1) | Verificação visual                                                    |
| Release         | Publicação self-contained win-x64                    | Pronto                                  | Executado sem .NET via `publish.ps1`                                  |
| Release         | Instalador MSI                                       | Planejado (F9)                          | Requer WiX                                                            |
| Research        | Network Capture, Packet Inspector, TCP Stream        | Planejado (F2)                          | Captura real já validada com TShark/Npcap (pré-requisito)             |
| Research        | HTTP, Protocol, Protobuf, Binary, File, Ghidra, Kali | Planejado (F3)                          | —                                                                     |
| Comparative     | Perfis, comparações, timeline, correlação            | Planejado (F4)                          | —                                                                     |
| Launcher Studio | Todos os itens                                       | Planejado (F5)                          | —                                                                     |
| Portal Studio   | Todos os itens                                       | Planejado (F6)                          | —                                                                     |
| Wiki / GM       | Todos os itens                                       | Planejado (F7)                          | —                                                                     |
| MIR4 Research   | Template 45 categorias                               | Planejado (F8)                          | —                                                                     |
