# Changelog — REA Advanced Suite

## 0.1.0 — 2026-11-15 (Fases 0 e 1)

### Adicionado
- Solução `suite/REA.sln` (.NET 10, WPF): Domain, Core, Infrastructure, Desktop, testes unitários e de integração.
- Banco SQLite com EF Core e migração `InitialCreate` (projects, environments, activity_log, app_settings).
- Projetos: criar, editar, listar com busca/filtro/paginação (50/100/200/500), duplicar, arquivar/reativar, excluir sem histórico.
- Ambientes por projeto com isolamento entre projetos.
- Auditoria legível (operador, ação, antes/depois).
- Backup manual, automático diário (14 mantidos) e restauração com cópia de segurança e validação.
- Detecção real de Npcap, TShark, Ghidra, Java, REA CLI, WSL/Kali e WiX.
- Interface escura com menu completo da especificação; módulos futuros exibem fase e dependências.
- Scripts: `check-environment`, `build`, `test`, `run`, `publish`.
- Documentação: guia do usuário, instalação, arquitetura, ADRs, matriz de funcionalidades, problemas conhecidos.

### Ambiente
- Instalados com autorização: Wireshark/TShark 4.6.9 (winget) e Npcap 1.90 (instalador oficial assinado).
- Motor REA 6.4.0 instalado (`npm ci` + build).
