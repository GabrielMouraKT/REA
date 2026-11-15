# Segurança

- **Sem credenciais no REA:** login do GitHub fica no `gh`/cofre do Windows; o REA não lê nem grava tokens.
- **Verificação de segredos antes do commit** (`SecretScanner`), só sobre o que vai no commit:
  - formatos conhecidos: chave privada PEM, tokens do GitHub, chaves AWS, Anthropic, `sk-…`, Slack, Google;
  - atribuições explícitas como `senha = "…"` / `api_key: "…"` com valor de 6+ caracteres; cadeia de conexão com senha;
  - arquivos sensíveis: `.env` (exceto `.env.example`), `id_rsa` e similares, `.npmrc`, `.pypirc`, `credentials`,
    `.pem`, `.key`, `.pfx`, `.p12`, `.jks`, `.keystore`, `.ppk`.
    O commit é **recusado** até o usuário ver a lista e confirmar. O valor encontrado aparece **mascarado** e nunca vai
    para o log de auditoria.
- **Confirmações:** criar repositório no GitHub, desvincular, excluir branch (dupla se houver trabalho só nele), merge,
  cancelar merge, push (com resumo).
- **Nunca automático:** push forçado, pull com merge escondido, exclusão no GitHub.
- **URLs de clone permitidas:** `https://`, `ssh://`, `git@host:dono/repo` e `file://`. Recusa `http://`, `ext::` e
  qualquer valor começando com `-` (injeção de opção). URLs com usuário/senha embutidos são mascaradas no log.
- **Caminhos** de arquivo são verificados para não sair da pasta do repositório.
- **Auditoria:** cada operação (com sucesso ou falha) vira uma entrada legível em Evidence › Atividades, com projeto,
  repositório e resultado.
