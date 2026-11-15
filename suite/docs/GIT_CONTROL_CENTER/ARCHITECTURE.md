# Arquitetura

```
Telas (REA.Desktop/ViewModels/GitViewModels.cs, Views/GitPages.xaml)
        │  (futuro: Claude Control Center usa o mesmo serviço)
        ▼
GitService (REA.Core) — regras, proteções, auditoria
        │ contratos IGitClient / IGitHubClient / IGitRepositoryRepository
        ▼
GitCli, GitHubCli (REA.ToolIntegration)      GitRepositoryRepository (REA.Infrastructure)
   git.exe / gh.exe via ProcessRunner            tabela git_repositories (SQLite)
```

- **Um serviço só.** Interface manual e Claude chamam o `GitService`; as proteções valem para os dois.
- **O Git é a fonte da verdade.** O banco guarda só o vínculo projeto ↔ pasta (nome, pasta, remoto no momento do vínculo,
  branch principal). Branch atual, alterações e sincronia são lidos do Git a cada consulta.
- **Processos externos** com argumentos em lista (nunca texto montado), sem prompts (`GIT_TERMINAL_PROMPT=0`,
  `GH_PROMPT_DISABLED=1`), saída do Git em inglês (`LC_ALL=C`) para leitura estável; falhas comuns traduzidas em
  `GitCli.Friendly`.
- **Leitura estruturada:** `status --porcelain=v2 -z`, `log` e `for-each-ref` com separadores de controle.
- **Banco:** migração aditiva `AddGitRepositories` (índice único projeto + pasta; FK sem cascata). Projeto com
  repositórios vinculados não pode ser excluído.
- **Vários repositórios por projeto** são permitidos.
- **Outros provedores:** `IGitHubClient` isola o GitHub; outro provedor (GitLab, Gitea) entra como nova implementação.
