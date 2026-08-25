# language: pt

@webhooks
Funcionalidade: Status do reengajamento recebido por webhook
  Os eventos de entrega devem atualizar a tentativa sem abrir uma janela de texto livre
  e sem regredir um status mais avançado

  Esquema do Cenário: Status de sucesso mantém a sessão aguardando resposta
    Dado que o cliente já foi reengajado
    Quando o webhook informar o status <status>
    Então a tentativa deve possuir o status <status>
    E a sessão deve continuar aguardando resposta do cliente
    E texto livre deve continuar bloqueado

    Exemplos:
      | status    |
      | Sent      |
      | Delivered |
      | Read      |

  Cenário: Status de falha devolve a sessão ao estado expirado
    Dado que o cliente já foi reengajado
    Quando o webhook informar falha com código e mensagem
    Então a tentativa deve estar falha e a sessão expirada
    E os detalhes da falha devem ser preservados

  Cenário: Um webhook atrasado não regride o status
    Dado que o cliente já foi reengajado
    Quando o webhook informar o status Read
    E o webhook informar o status Sent
    Então a tentativa deve possuir o status Read

  Cenário: Um status desconhecido não altera outra sessão
    Dado que o cliente já foi reengajado
    Quando chegar um status para um wamid desconhecido
    Então a tentativa deve possuir o status Accepted

  Cenário: Uma falha tardia não fecha uma janela reaberta pelo cliente
    Dado que o cliente já foi reengajado
    E que o cliente respondeu ao reengajamento
    Quando o webhook informar falha com código e mensagem
    Então a sessão deve continuar aberta
