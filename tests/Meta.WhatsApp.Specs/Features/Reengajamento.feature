# language: pt

@reengajamento
Funcionalidade: Reengajamento depois do fechamento da janela
  Depois da expiração o sistema deve poder contatar novamente o mesmo cliente
  pelo mesmo número WhatsApp usando um template aprovado e sem duplicar contatos

  Cenário: Reengajar pelo mesmo canal com template aprovado
    Dado que o cliente possui uma sessão expirada
    E que o template de retomada está aprovado
    E que a Meta aceitará uma mensagem
    Quando o sistema solicitar o reengajamento com a chave tentativa-1
    Então o reengajamento deve ser submetido pelo mesmo canal
    E a sessão deve ficar aguardando resposta do cliente
    E o template não deve reutilizar o contexto vencido

  Esquema do Cenário: Impedir template que não está aprovado
    Dado que o cliente possui uma sessão expirada
    E que o template de retomada possui status <status>
    Quando o sistema solicitar o reengajamento com a chave tentativa-1
    Então o reengajamento deve falhar porque o template não está aprovado
    E nenhuma mensagem de reengajamento deve ter sido enviada

    Exemplos:
      | status     |
      | PENDING    |
      | REJECTED   |
      | PAUSED     |
      | NOT_FOUND  |

  Cenário: Não reengajar sem uma sessão anterior
    Dado que não existe sessão para o cliente
    Quando o sistema solicitar o reengajamento com a chave tentativa-1
    Então o reengajamento deve falhar porque a sessão não existe

  Cenário: Não reengajar enquanto a janela ainda está aberta
    Dado que o cliente possui uma sessão aberta
    Quando o sistema solicitar o reengajamento com a chave tentativa-1
    Então o reengajamento deve falhar porque a sessão ainda está aberta

  Cenário: Repetir a mesma chave não envia outro template
    Dado que o cliente possui uma sessão expirada
    E que o template de retomada está aprovado
    E que a Meta aceitará uma mensagem
    Quando o sistema solicitar duas vezes o reengajamento com a mesma chave
    Então a segunda solicitação deve retornar o envio anterior
    E somente uma mensagem de reengajamento deve ter sido enviada

  Cenário: Uma chave diferente durante o cooldown é bloqueada
    Dado que o cliente já foi reengajado
    E que o template de retomada está aprovado
    Quando o sistema solicitar o reengajamento com a chave tentativa-2
    Então o reengajamento deve falhar por cooldown
    E somente uma mensagem de reengajamento deve ter sido enviada

  Cenário: Uma nova tentativa é aceita depois do cooldown
    Dado que o cliente já foi reengajado
    E que o template de retomada está aprovado
    E que o cooldown de reengajamento terminou
    E que a Meta aceitará uma mensagem
    Quando o sistema solicitar o reengajamento com a chave tentativa-2
    Então duas tentativas de reengajamento devem estar armazenadas
    E duas mensagens de reengajamento devem ter sido enviadas

  Cenário: Uma rejeição síncrona da Meta marca a tentativa como falha
    Dado que o cliente possui uma sessão expirada
    E que o template de retomada está aprovado
    E que a Meta rejeitará o reengajamento
    Quando o sistema solicitar o reengajamento com a chave tentativa-1
    Então a tentativa deve estar falha e a sessão expirada

  Cenário: Uma falha de transporte deixa o resultado desconhecido
    Dado que o cliente possui uma sessão expirada
    E que o template de retomada está aprovado
    E que ocorrerá uma falha de transporte
    Quando o sistema solicitar o reengajamento com a chave tentativa-1
    Então a tentativa deve ficar com status desconhecido
    E uma repetição com a mesma chave não deve reenviar a mensagem

  Cenário: A resposta do cliente reabre a janela depois do reengajamento
    Dado que o cliente já foi reengajado
    Quando o cliente responder ao reengajamento
    Então a sessão deve estar aberta
    E o novo contexto deve ser a resposta do cliente
    E o registro deve indicar que a sessão foi reativada
    E a resposta deve estar correlacionada ao template de reengajamento

  Cenário: Repetir o webhook da resposta não reativa a sessão duas vezes
    Dado que o cliente já foi reengajado
    Quando o webhook entregar duas vezes a mesma resposta do cliente
    Então o primeiro registro deve indicar que a sessão foi reativada
    E o segundo registro deve ser ignorado como duplicado
    E a sessão deve estar aberta

  Cenário: Uma nova mensagem sem contexto também reativa a sessão
    Dado que o cliente já foi reengajado
    Quando o cliente iniciar uma nova mensagem sem responder ao template
    Então o registro deve indicar que a sessão foi reativada
    E a resposta não deve estar correlacionada ao template de reengajamento
    E a sessão deve estar aberta

  Cenário: Duas instâncias concorrentes não duplicam o reengajamento
    Dado que duas instâncias compartilham o armazenamento de sessões
    E que o cliente possui uma sessão expirada
    E que o template de retomada está aprovado
    Quando as duas instâncias solicitarem o mesmo reengajamento simultaneamente
    Então somente uma mensagem de reengajamento deve ter sido enviada
    E uma instância deve observar o resultado já reservado
