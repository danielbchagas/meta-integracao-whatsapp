# language: pt

@mensagens
Funcionalidade: Envio de mensagens ao cliente
  A biblioteca deve montar corretamente todos os tipos de mensagem suportados
  e respeitar a janela de atendimento e o contexto escolhido pelo sistema

  Cenário: Enviar texto com pré-visualização de URL
    Dado que o cliente possui uma sessão aberta
    E que a Meta aceitará uma mensagem
    Quando o sistema enviar um texto com pré-visualização de URL
    Então a mensagem enviada deve ser do tipo texto
    E a pré-visualização de URL deve estar habilitada

  Cenário: Enviar template fora da janela de atendimento
    Dado que não existe sessão para o cliente
    E que a Meta aceitará uma mensagem
    Quando o sistema enviar diretamente um template
    Então a mensagem enviada deve ser do tipo template
    E a mensagem enviada não deve possuir contexto de resposta

  Esquema do Cenário: Enviar conteúdo suportado dentro da sessão
    Dado que o cliente possui uma sessão aberta
    E que a Meta aceitará uma mensagem
    Quando o sistema enviar uma mensagem do tipo <tipo>
    Então o payload deve conter o conteúdo <tipo>

    Exemplos:
      | tipo        |
      | imagem      |
      | vídeo       |
      | áudio       |
      | documento   |
      | localização |
      | customizada |

  Cenário: Uma resposta explícita substitui o contexto automático
    Dado que o cliente possui uma sessão aberta
    E que a Meta aceitará uma mensagem
    Quando o sistema responder explicitamente a outra mensagem
    Então a mensagem deve usar o contexto explícito

  Cenário: O contexto automático pode ser desabilitado
    Dado que o contexto automático está desabilitado
    E que o cliente possui uma sessão aberta
    E que a Meta aceitará uma mensagem
    Quando o sistema enviar uma mensagem de texto livre
    Então a mensagem enviada não deve possuir contexto de resposta

  Esquema do Cenário: Rejeitar referência de mídia inválida
    Dado que o cliente possui uma sessão aberta
    Quando o sistema enviar uma mídia com referência <referência>
    Então o envio deve falhar por argumento inválido
    E nenhuma mensagem deve ter sido enviada para a Meta

    Exemplos:
      | referência |
      | ausente    |
      | duplicada  |

  Cenário: Mapear os detalhes de uma falha retornada pela Meta
    Dado que não existe sessão para o cliente
    E que a Meta rejeitará a mensagem com erro estruturado
    Quando o sistema enviar diretamente um template
    Então a falha deve expor código subcódigo e trace da Meta
