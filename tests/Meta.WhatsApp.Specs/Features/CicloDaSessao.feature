# language: pt

@sessao
Funcionalidade: Ciclo de vida da sessão de atendimento
  Para continuar uma conversa pelo WhatsApp de forma compatível com as regras da Meta
  Como sistema consumidor da biblioteca
  Quero controlar abertura, renovação e expiração da janela por canal e cliente

  Cenário: A primeira mensagem recebida abre a sessão
    Quando o cliente enviar a primeira mensagem
    Então a sessão deve estar aberta
    E a sessão deve pertencer ao mesmo canal e cliente

  Cenário: Mensagens livres sucessivas reutilizam a sessão aberta
    Dado que o cliente possui uma sessão aberta
    E que a Meta aceitará duas mensagens
    Quando o sistema enviar duas mensagens de texto livre
    Então as duas mensagens devem usar o último contexto recebido
    E duas mensagens devem ter sido enviadas para a Meta

  Cenário: Uma nova mensagem do cliente renova a janela e o contexto
    Dado que o cliente possui uma sessão aberta
    E que se passaram 23 horas
    Quando o cliente enviar uma nova mensagem
    E se passarem mais 2 horas
    Então a sessão deve continuar aberta
    E o contexto atual deve ser a nova mensagem recebida

  Cenário: A sessão expirada é preservada e bloqueia texto livre
    Dado que o cliente possui uma sessão aberta
    E que a janela de atendimento expirou
    Quando o sistema tentar enviar texto livre
    Então o envio deve falhar porque a sessão está fechada
    E a sessão deve estar preservada como expirada
    E nenhuma mensagem deve ter sido enviada para a Meta

  Cenário: O fechamento manual preserva a sessão para reengajamento
    Dado que o cliente possui uma sessão aberta
    Quando o sistema fechar a sessão manualmente
    Então a sessão deve estar preservada como expirada

  Cenário: Uma mensagem recebida fora de ordem não substitui a mais recente
    Dado que o cliente possui uma sessão aberta
    Quando chegar uma mensagem mais nova seguida de uma mais antiga
    Então o contexto atual deve ser a mensagem mais nova

  Cenário: O mesmo cliente em outro canal não compartilha a sessão
    Dado que o cliente possui uma sessão aberta no canal principal
    Quando outro canal consultar a sessão do cliente
    Então o outro canal não deve encontrar uma sessão
