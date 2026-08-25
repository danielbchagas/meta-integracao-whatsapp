# language: pt

@templates
Funcionalidade: Administração dos templates da Meta
  O client deve consultar, criar e atualizar templates somente quando necessário

  Cenário: Listar todos os templates usando paginação
    Dado que a Meta possui duas páginas de templates
    Quando o sistema listar os templates
    Então todos os templates das duas páginas devem ser retornados

  Cenário: Consultar um template pelo identificador
    Dado que a Meta possui um template aprovado
    Quando o sistema buscar o template pelo identificador
    Então o template aprovado deve ser retornado

  Cenário: Criar um template inexistente
    Dado que o template desejado não existe na Meta
    Quando o sistema garantir a existência do template
    Então o template deve ser criado

  Cenário: Não atualizar um template idêntico
    Dado que o template desejado já existe com o mesmo conteúdo
    Quando o sistema garantir a existência do template
    Então o template deve permanecer inalterado
    E nenhuma atualização de template deve ser enviada

  Cenário: Atualizar um template com conteúdo diferente
    Dado que o template desejado existe com conteúdo diferente
    Quando o sistema garantir a existência do template
    Então o template deve ser atualizado

  Cenário: Expor erro da Meta ao criar um template
    Dado que a Meta rejeitará a criação do template
    Quando o sistema criar o template
    Então a falha de template deve preservar os detalhes da Meta
