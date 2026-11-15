# Operações Git

| Operação                         | Onde                             | Regra de proteção                                                                                                          |
| -------------------------------- | -------------------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| Status                           | Todas as telas (faixa de estado) | Estado em palavras: Limpo, Alterado, Arquivos novos, N para enviar/receber, Divergente, Conflito, Sincronizado, Sem remoto |
| Diff                             | Commit Manager                   | Arquivo novo mostra o conteúdo; binário só o tamanho; limite de 400 mil caracteres                                         |
| Add / tirar da seleção           | Commit Manager                   | Caminhos fora do repositório são recusados                                                                                 |
| Commit                           | Commit Manager                   | Recusa sem mensagem, sem arquivos selecionados ou com conflito; verificação de segredos (ver SECURITY)                     |
| Stash / recuperar                | Commit Manager                   | Inclui arquivos novos                                                                                                      |
| Log / Show                       | Git History                      | Até 500 commits; detalhes com arquivos e diferenças                                                                        |
| Branch criar / renomear / trocar | Branch Manager                   | Nome validado pelo próprio Git (`check-ref-format`); troca nunca descarta alterações                                       |
| Branch excluir                   | Branch Manager                   | Ativo: recusado. Com commits só nele: segunda confirmação explicando a perda                                               |
| Merge                            | Branch Manager                   | Exige área limpa; em conflito, a operação fica pendente com aviso e botão _Cancelar merge_                                 |
| Fetch                            | Remote Sync                      | `--all --prune`                                                                                                            |
| Pull                             | Remote Sync                      | Só avanço rápido (`--ff-only`); se divergir, explica e não mistura nada escondido                                          |
| Push                             | Remote Sync                      | Resumo dos commits e confirmação; **nunca forçado**; primeiro envio liga o branch ao remoto                                |
| Init / Clone / Remote add        | Repositories                     | Ver REPOSITORIES                                                                                                           |

Mensagens de erro do Git são traduzidas (repositório inexistente, alterações que seriam perdidas, conflito, envio
recusado, divergência, autenticação, sem conexão, branch não mesclado, identidade não configurada, arquivo bloqueado).

**Próxima etapa:** rebase, tag, reset (com pré-visualização e confirmação), revert, cherry-pick, blame, clean (com
pré-visualização), bisect, submodule, resolução visual de conflitos, comparação de branches.
