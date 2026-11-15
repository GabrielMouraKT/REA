# Matriz de funcionalidades

Legenda: **Pronto** = implementado + testado · **Planejado (Fn)** = não implementado, fase prevista.

| Área                | Funcionalidade                                                                                  | Situação       | Validação                                |
| ------------------- | ----------------------------------------------------------------------------------------------- | -------------- | ---------------------------------------- |
| Fundação            | Solução, camadas, DI, Host, Serilog, SQLite/EF, migrações                                       | Pronto         | Build sem avisos; testes de persistência |
| Projetos            | CRUD, busca, paginação, duplicar, arquivar, excluir sem histórico                               | Pronto         | Integração + UI Automation               |
| Ambientes           | CRUD por projeto, isolamento, bloqueio de exclusão com capturas                                 | Pronto         | Integração                               |
| Auditoria           | Registro legível antes/depois                                                                   | Pronto         | Integração                               |
| Backup              | Manual, automático, restauração segura                                                          | Pronto         | BackupTests                              |
| Settings            | Database, Tools (detecção real)                                                                 | Pronto         | Integração + visual                      |
| Dashboard           | Projetos, ambientes, capturas, pacotes, fluxos TCP, módulos, ferramentas                        | Pronto         | Integração + visual                      |
| Release             | Publicação self-contained win-x64                                                               | Pronto         | publish.ps1 executado                    |
| Network Capture     | Npcap/SharpPcap, BPF validado, limites, estatísticas, PCAPNG contínuo                           | Pronto         | Captura real em loopback (teste + UI)    |
| Captures            | Importação com SHA-256, detalhes, exportação, validação TShark, reanálise, recuperação de queda | Pronto         | Integração (inclui queda simulada)       |
| Packet Inspector    | Paginação em disco, filtros, camadas, hex/ASCII, CSV                                            | Pronto         | NetworkTests + UI                        |
| TCP Stream Explorer | Reordenação, retransmissões, lacunas, completude, timeline, exportação                          | Pronto         | 7 cenários + captura real + TShark       |
| Formatos            | PCAPNG (SHB/IDB/EPB/SPB/OPB), PCAP µs/ns LE/BE, truncado/corrompido                             | Pronto         | NetworkTests                             |
| Captura             | Pausar                                                                                          | Não suportado  | Limitação do libpcap (KNOWN_ISSUES #10)  |
| Release             | Instalador MSI                                                                                  | Planejado (F9) | Requer WiX                               |
| Research            | HTTP, Protocol, Protobuf, Binary, File, Ghidra, Kali                                            | Planejado (F3) | —                                        |
| Comparative         | Perfis, comparações, timeline, correlação                                                       | Planejado (F4) | —                                        |
| Bot Lab AI          | Manual Gameplay, Recorder, History, Evidence Explorer, Dashboard, Session Intelligence          | Planejado (F4) | —                                        |
| Bot Lab AI          | AI Gameplay, Live Observer, Visual Recognition, Scenario Builder, Agent Configuration           | Planejado (F7) | —                                        |
| Launcher Studio     | Todos os itens                                                                                  | Planejado (F5) | —                                        |
| Portal Studio       | Todos os itens                                                                                  | Planejado (F6) | —                                        |
| Wiki / GM           | Todos os itens                                                                                  | Planejado (F7) | —                                        |
| MIR4 Research       | Template 45 categorias                                                                          | Planejado (F8) | —                                        |
