# Status do desenvolvimento

**Última atualização:** 2026-11-15 · **Branch:** `feature/rea-advanced-suite` · **Versão:** 0.1.0

## Onde paramos

Fases 0 e 1 concluídas e testadas. **Próxima: Fase 2 (Network Research)** — ver `IMPLEMENTATION_PLAN.md`.

## Para retomar

1. `cd suite; .\scripts\check-environment.ps1` (Npcap, TShark, Ghidra e REA CLI já presentes nesta máquina).
2. `.\scripts\build.ps1; .\scripts\test.ps1` → esperado: 35/35 aprovados.
3. Ler `KNOWN_ISSUES.md` e os “Próximos passos da Fase 2” em `IMPLEMENTATION_PLAN.md`.

## Fase 1 — resultado

- Concluído: fundação, projetos, ambientes, auditoria, backup/restauração, detecção de ferramentas, dashboard, navegação completa, publicação self-contained, documentação.
- Testes executados: 20 unitários + 15 integração (SQLite real) = 35 aprovados; fluxo de criação de projeto e validação de nome duplicado conferidos na interface real via UI Automation + capturas de tela.
- Testes pendentes: fluxos de Ambientes, Backup e Duplicar exercitados só por testes de integração (não pela interface).
- Problemas: ver `KNOWN_ISSUES.md` (#5 teste possivelmente instável sob execução concorrente).

## Fase 0 — resultado

`docs/AUDITORIA_FASE0.md`.

## Arquivos por fase

Fase 0/1: todo o conteúdo de `suite/` (nenhum arquivo do motor REA em TypeScript foi alterado).
