# Arquitetura — REA Advanced Suite

## Visão geral

```text
REA (repositório)
├── src/, scripts/rea.mjs   Motor REA (TypeScript, MCP/CLI) — fork de morluto/rea, MIT
└── suite/                  REA Advanced Suite (.NET 10, WPF)
    ├── src/
    │   ├── REA.Domain          Entidades puras (sem dependências)
    │   ├── REA.Core            Casos de uso, contratos (interfaces), validação, catálogo de navegação
    │   ├── REA.Infrastructure  EF Core + SQLite, repositórios, backup, detecção de ferramentas
    │   └── REA.Desktop         WPF + MVVM (CommunityToolkit), Generic Host, Serilog
    └── tests/
        ├── REA.UnitTests       Regras puras (Core)
        └── REA.IntegrationTests  Composição real + SQLite em disco
```

Dependências apontam para dentro: `Desktop → Infrastructure → Core → Domain`. O Core não conhece EF nem WPF.
Novos módulos (Fase 2+) entram como projetos `REA.<Módulo>` que dependem de Core/Domain e são registrados
no Desktop por extensões `AddRea<Módulo>()`.

## Decisões principais

| Tema                       | Decisão                                                                                                                          |
| -------------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| Persistência               | SQLite (`rea.db`) via EF Core; migrações aplicadas na inicialização; `DateTimeOffset` gravado em binário para permitir ordenação |
| Transação                  | Serviços registram entidade + auditoria no mesmo `DbContext`; `IUnitOfWork.SaveChangesAsync` grava tudo junto ou nada            |
| Escopo                     | Uma operação de UI = um escopo de DI = um `DbContext` (`ScopedRunner`)                                                           |
| Isolamento                 | Ambiente é sempre buscado por `(projectId, id)`; de outro projeto responde “não encontrado”                                      |
| Exclusão                   | Sem cascata (`DeleteBehavior.Restrict`); projeto com histórico só arquiva                                                        |
| Auditoria                  | `activity_log` com operador, ação, resumo e antes/depois em texto legível                                                        |
| Busca                      | `LIKE` parametrizado com escape de `%`, `_` e `\`                                                                                |
| Paginação                  | No banco; tamanhos 50/100/200/500                                                                                                |
| Backup                     | API de backup online do SQLite; restauração valida integridade e cria cópia de segurança antes                                   |
| Ferramentas externas       | Detectadas executando a própria ferramenta; stdin fechado (REA CLI é servidor MCP)                                               |
| Navegação                  | `NavigationCatalog` (Core) define o menu completo; item não implementado abre tela de status, nunca dados falsos                 |
| Integração com o motor REA | Via CLI/MCP (`scripts/rea.mjs`), sem portar código TypeScript                                                                    |

Registro completo: [`../../ARCHITECTURE_DECISIONS.md`](../../ARCHITECTURE_DECISIONS.md).
