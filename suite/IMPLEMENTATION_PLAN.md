# Plano de implementação

| Fase | Escopo                                                                                           | Critério de aceitação                                                                              | Situação                                  |
| ---- | ------------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------- | ----------------------------------------- |
| 0    | Auditoria, branch, ambiente                                                                      | Relatório de auditoria                                                                             | **Concluída** (`docs/AUDITORIA_FASE0.md`) |
| 1    | Fundação: solução, DI, MVVM, SQLite/EF, logging, navegação, dashboard, projetos, backup          | Abre, cria projeto, persiste e recupera após reinício                                              | **Concluída**                             |
| 2    | Network Capture (SharpPcap/PacketDotNet), PCAPNG, Packet Inspector, TCP Stream Explorer, filtros | Capturar loopback → salvar PCAPNG → reabrir → visualizar → reconstruir fluxo → comparar com TShark | Próxima                                   |
| 3    | Protocol Explorer, Protobuf, Binary/File Analysis, Ghidra headless, relatórios                   | —                                                                                                  | Pendente                                  |
| 4    | Perfis de ambiente, version tracker, comparações, timeline, correlação de evidências             | —                                                                                                  | Pendente                                  |
| 5    | Launcher Studio                                                                                  | Launcher demo criado, editado, compilado e executado                                               | Pendente                                  |
| 6    | Portal Studio                                                                                    | Portal demo gerado e executado localmente                                                          | Pendente                                  |
| 7    | Wiki + GM                                                                                        | Catálogo demo importado; busca por ID/nome; operações em adaptador de laboratório                  | Pendente                                  |
| 8    | Template MIR4                                                                                    | —                                                                                                  | Pendente                                  |
| 9    | Finalização: performance, segurança, MSI, validação em Windows limpo                             | —                                                                                                  | Pendente                                  |

## Próximos passos da Fase 2

1. Projetos `REA.NetworkCapture` e `REA.PacketAnalyzer` (SharpPcap 6.3.1, PacketDotNet 1.4.8 — verificar licenças MIT/LGPL).
2. Entidades `Capture` e `CapturedPacket` (metadados no SQLite; pacotes no arquivo PCAPNG, lidos por streaming e paginação).
3. Serviço de captura assíncrono com cancelamento, limite de pacotes/bytes e estatísticas.
4. Reconstrução TCP com reordenação, retransmissões e lacunas (sem declarar fluxo completo quando houver lacuna).
5. Teste de integração real em loopback (pular com motivo explícito quando Npcap ausente) + comparação com `tshark -z conv,tcp`.
6. Bloquear exclusão de ambiente com capturas.
