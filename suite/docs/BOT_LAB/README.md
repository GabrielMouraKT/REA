# Bot Lab AI — plano de implementação

Status: **planejado** (pedido recebido em 15/11/2026). Nada deste módulo está implementado ainda; o menu do aplicativo
mostra a seção BOT LAB AI com a fase de cada item.

## Distribuição nas fases (respeitando dependências)

| Fase | Itens                                                                                                                                  | Depende de                                                                                                                         |
| ---- | -------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| 4    | **Manual Gameplay**, Session Recorder, Session History, Evidence Explorer, Dashboard do Bot Lab, Session Intelligence (timeline única) | Network Capture (Fase 2, pronto), Session Timeline e correlação de evidências (Fase 4)                                             |
| 7    | AI Gameplay, Live Observer, Visual Recognition (OCR, elementos), Scenario Builder, Agent Configuration                                 | Processo auxiliar Python via Named Pipes (JSON versionado); referências _AI.game.agent_ e _Pixeler_ (links a confirmar com o dono) |

## Decisões já tomadas

- **Manual Gameplay é prioritário e não depende de IA**: captura de tela periódica (FPS configurável), eventos marcados pelo usuário com atalhos globais que **não interceptam** teclas do jogo, captura de rede passiva, relógio monotônico + UTC, timeline única.
- **Nenhuma injeção no processo do jogo**: só identificação de processo/janela (PID, executável, título, resolução, encerramento).
- **AI Gameplay só age em ambientes do tipo Laboratório local ou Emulador** e com aprovação humana por ação. Em ambientes _Oficial_ (ex.: MIR4 Global) o agente apenas observa. Sem automação de farm/combate/progressão em servidores de terceiros e sem contornar anti-cheat.
- **Python não acessa o SQLite**: toda persistência passa pelo .NET.
- **Correlação nunca presume causalidade**: pacotes no intervalo de um evento são "observados no intervalo", não "causados por".
