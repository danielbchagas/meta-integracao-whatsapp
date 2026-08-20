# language: pt

@webhooks @seguranca
Funcionalidade: Recebimento seguro de webhooks da Meta
  Para reativar sessões sem aceitar chamadas forjadas
  Como sistema consumidor da biblioteca
  Quero autenticar e processar respostas recebidas da Meta

  Cenário: Uma resposta autêntica reativa a sessão
    Dado que existe uma sessão aguardando resposta ao template
    E que a Meta enviará um webhook de resposta com assinatura válida
    Quando o webhook seguro for processado
    Então o processamento deve indicar que a sessão foi reativada
    E a resposta processada deve estar correlacionada ao template

  Cenário: Uma assinatura inválida não altera a sessão
    Dado que existe uma sessão aguardando resposta ao template
    E que a Meta enviará um webhook de resposta com assinatura inválida
    Quando o webhook seguro for processado
    Então o webhook deve ser rejeitado por assinatura inválida
    E a sessão deve continuar aguardando resposta

  Cenário: Uma notificação de outro canal é ignorada
    Dado que a Meta enviará um webhook válido de outro canal
    Quando o webhook seguro for processado
    Então a notificação deve ser ignorada
    E nenhuma sessão deve ser criada pelo webhook
