# Status do desenvolvimento

**Última atualização:** 2026-11-15 · **Branch:** `feature/rea-advanced-suite` · **Versão:** 0.3.0

## Onde paramos

> **Retomada:** leia primeiro `docs/CONTINUAR.md`.

Fases 0 a 3 concluídas; Git Control Center (modo manual) pronto. **Próximo:** completar o Git (operações avançadas,
Pull Requests, Issues, Releases) e depois o Claude Control Center (Claude Code instalado) — ver `IMPLEMENTATION_PLAN.md`.

## Para retomar

1. `cd suite; .\scripts\check-environment.ps1`
2. `.\scripts\build.ps1; .\scripts\test.ps1` → esperado **149 testes, 1 ignorado** (o teste da conta real do GitHub só roda com `REA_TEST_GITHUB=1`)
3. Ler `KNOWN_ISSUES.md`, `docs/RELATORIO_FASE2.md` e `docs/BOT_LAB/README.md`.
4. Ao terminar cada fase: atualizar `docs/guia/index.html`, `docs/GENERAL/USER_GUIDE.md` e escrever `docs/RELATORIO_FASE<N>.md`.

## Fase 2 — resultado

- Concluído: Network Capture, Evidence › Captures, Packet Inspector, TCP Stream Explorer, validação com TShark, recuperação de captura interrompida, métricas no Dashboard.
- Critério obrigatório (captura real, PCAPNG, fechar, reabrir, ver, reconstruir, comparar com TShark): **aprovado** por teste automático com tráfego real e conferido na interface.
- Testes: 20 unitários + 25 de rede + 21 de integração = 66 aprovados, 0 ignorados.
- Detalhes: `docs/RELATORIO_FASE2.md`.

## Fases anteriores

- Fase 0: `docs/AUDITORIA_FASE0.md` · Fase 1: `docs/RELATORIO_FASES_0_1.md`.
