# REA Advanced Suite

Plataforma Windows (.NET 10 / WPF) para investigação técnica de clientes e servidores MMORPG: projetos, ambientes,
evidências e, nas próximas fases, captura de rede, análise de protocolos e binários, Launcher Studio, Portal Studio,
Wiki e administração de servidores próprios. Usa o motor **REA** (raiz deste repositório) para engenharia reversa.

**Estado:** 0.1.0 — Fases 0 e 1 concluídas. Veja `DEVELOPMENT_STATUS.md` e `FEATURE_MATRIX.md`.

## Começar

```powershell
cd suite
.\scripts\check-environment.ps1
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\run.ps1
```

## Documentação

| Documento                                                                                                                              | Conteúdo                                                 |
| -------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------- |
| [docs/GENERAL/USER_GUIDE.md](docs/GENERAL/USER_GUIDE.md)                                                                               | **Como usar a ferramenta inteira** (desktop + motor REA) |
| [docs/GENERAL/INSTALLATION.md](docs/GENERAL/INSTALLATION.md)                                                                           | Instalação para usuário e desenvolvedor                  |
| [docs/GENERAL/ARCHITECTURE.md](docs/GENERAL/ARCHITECTURE.md)                                                                           | Camadas e decisões técnicas                              |
| [docs/RELATORIO_FASES_0_1.md](docs/RELATORIO_FASES_0_1.md)                                                                             | Relatório do que foi feito e como                        |
| [docs/AUDITORIA_FASE0.md](docs/AUDITORIA_FASE0.md)                                                                                     | Auditoria inicial do repositório e do ambiente           |
| `DEVELOPMENT_STATUS.md`, `IMPLEMENTATION_PLAN.md`, `ARCHITECTURE_DECISIONS.md`, `KNOWN_ISSUES.md`, `FEATURE_MATRIX.md`, `CHANGELOG.md` | Continuidade do desenvolvimento                          |

Uso permitido: ambientes próprios, laboratórios e sistemas com autorização. Ver seção 7 do guia do usuário.
