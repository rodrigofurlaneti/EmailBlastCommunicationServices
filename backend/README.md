# EmailBlastCommunicationServices

Microserviço .NET 9 / Minimal API, Azure.Communication.Email 1.1.0 e EF Core 9 com MySQL/Pomelo.
Um destinatário por solicitação, compatível com o modelo de EmailLogs existente.

## Arquitetura

A solução contém quatro projetos com responsabilidades e dependências explícitas:

- **Domain**: validação do conteúdo e regras de transição dos status de entrega, sem dependências externas.
- **Application**: DTOs, interfaces IEmailStore/IEmailSender e casos de uso de envio, consulta e processamento de relatórios. Depende apenas de Domain.
- **Infrastructure**: DbContext, mapeamentos Fluent API, repositórios EF Core e integração Azure Communication Services.
- **Api**: composição, endpoints HTTP, autenticação do webhook e interpretação do contrato Event Grid. Usa Application e registra Infrastructure.

A validação de SystemId e a coordenação Queued → Sent/Failed ocorrem na Application.
As transições de entrega são definidas no Domain e aplicadas pela Infrastructure sob
atualizações condicionais e controle de concorrência, impedindo regressões com webhooks concorrentes.
O esquema SQL foi preservado. Datas nulas no banco agora são representadas como null também no DTO de status.

### Organização das pastas

```text
src/
├── EmailBlastCommunicationServices.Domain/
│   ├── Entities/                      # SourceSystem, DeliveryReportType, EmailLog
│   ├── Interfaces/                    # Contratos de repositório das entidades
│   │   └── Base/                      # ICrudRepository<TEntity, TKey, TQuery>
│   ├── Queries/                       # Escopo e paginação das consultas
│   ├── Rules/                         # Validação e transições de entrega
│   └── ValueObjects/                  # DeliveryReport
├── EmailBlastCommunicationServices.Application/
│   ├── Interfaces/
│   │   ├── Messaging/                 # IEmailSender
│   │   └── Persistence/               # IEmailStore: porta do fluxo de envio
│   ├── Contracts/Emails/              # DTOs de entrada e saída
│   ├── Commands/
│   │   ├── SendEmail/                 # Handler e resultado do envio
│   │   └── ProcessDeliveryReports/    # Processamento de entregas
│   └── Queries/GetEmail/              # Handler e resultado da consulta
├── EmailBlastCommunicationServices.Infrastructure/
│   ├── Integrations/AzureCommunicationServices/
│   ├── Persistence/Context/            # EmailBlastDbContext e DbSets
│   ├── Persistence/Configurations/     # Mapeamentos IEntityTypeConfiguration
│   ├── Persistence/MySql/Repositories/ # Implementações CRUD das três entidades
│   └── DependencyInjection.cs
└── EmailBlastCommunicationServices.Api/
    ├── Contracts/EventGrid/
    ├── Endpoints/Emails/
    ├── Endpoints/EventGrid/
    ├── Mappers/EventGrid/
    ├── Properties/
    └── Program.cs
```

Os namespaces acompanham as pastas. Commands e Queries separam os casos de uso;
os handlers são chamados diretamente, sem dependência de MediatR.
As regras genéricas de direção de dependências do AGENTS.md são verificadas pelos
testes de arquitetura. A persistência utiliza EF Core/Pomelo conforme a orientação
atual; a identificação continua por SystemId conforme o contrato do microserviço.

`Domain/Entities` representa todas as colunas das tabelas: `SourceSystem` corresponde
a `Systems` (o nome evita conflito com o namespace .NET `System`), `DeliveryReportType`
a `DeliveryReportTypes` e `EmailLog` a `EmailLogs`. Nomes de propriedades correspondem
às colunas mapeadas nas configurações do EF Core. Campos SQL nullable, inclusive os DATETIME
sem NOT NULL, usam tipos anuláveis. IDs estrangeiros permanecem como inteiros;
as entidades não dependem de atributos de ORM. Os DTOs HTTP continuam separados
das entidades, evitando expor corpo e detalhes internos do log na consulta de status.

No Domain, os contratos `ISourceSystemRepository`, `IDeliveryReportTypeRepository` e
`IEmailLogRepository` herdam os métodos CreateAsync, GetByIdAsync, ListAsync,
UpdateAsync e DeleteAsync de `Interfaces/Base/ICrudRepository`. Os parâmetros
genéricos definem entidade, chave e consulta paginada. Para logs, EmailLogKey e
EmailLogQuery exigem SystemId. Esses tipos também pertencem ao Domain e não dependem
dos DTOs da Application. As implementações SourceSystemRepository,
DeliveryReportTypeRepository e EmailLogRepository ficam em
`Infrastructure/Persistence/MySql/Repositories` e são registradas em AddInfrastructure.
Todas usam EmailBlastDbContext, LINQ e CancellationToken. Inserts usam SaveChangesAsync
e retornam o ID preenchido pelo EF Core. Atualizações em lote usam ExecuteUpdateAsync
e exclusões usam ExecuteDeleteAsync, que persistem imediatamente. Atualizações individuais
de logs usam SaveChangesAsync com tokens de concorrência para status e OperationId.
Listagens aceitam de 1 a 1000 registros por página. Datas são administradas pelo MySQL.
Exclusões são físicas; as chaves estrangeiras impedem remover sistemas e status em uso.
Logs são criados como Queued; atualizações respeitam as transições de status e não
alteram o conteúdo ou OperationId após o processamento. O fluxo de envio reutiliza
EmailLogRepository para a inserção da intenção. Não foram adicionadas rotas administrativas.

## Configuração

1. Em um MySQL 8, execute manualmente `../sql/createdatabase.sql` e depois
   `../sql/002_delivery_report_types.sql`. A API não cria nem altera o banco automaticamente.
2. Configure as variáveis de ambiente abaixo ou os campos correspondentes em
   `src/EmailBlastCommunicationServices.Api/appsettings.Local.json` (ignorado pelo Git).

| Variável | Finalidade |
| --- | --- |
| `ConnectionStrings__MySql` | Conexão MySQL com o banco emailblastdb |
| `Database__ServerVersion` | Versão MySQL usada pelo provider; padrão 8.0.0, sem conexão automática para detecção |
| `COMMUNICATION_SERVICES_CONNECTION_STRING` | Credencial do recurso Azure Communication Services |
| `Email__SenderAddress` | Remetente de um domínio verificado no Azure |
| `EventGrid__WebhookKey` | Segredo compartilhado com a assinatura Event Grid |

Não versione credenciais. Variáveis de ambiente prevalecem sobre os arquivos JSON.

Execute a partir de `backend`:

```powershell
dotnet build src/EmailBlastCommunicationServices.sln -c Release
dotnet run --project src/EmailBlastCommunicationServices.Api --urls http://localhost:5080
```

## Contratos

`POST /api/emails`

```json
{
  "systemId": 1,
  "recipient": "destinatario@example.com",
  "subject": "Teste de envio",
  "bodyText": "Olá!",
  "bodyHtml": "<p>Olá!</p>"
}
```

Pelo menos um corpo é obrigatório. Assunto e destinatário: até 255 caracteres.
Cada corpo: até 65535 bytes UTF-8, conforme a coluna MySQL TEXT.
SystemId deve existir em Systems. Dados inválidos retornam 400.
O fluxo salva Queued com SaveChangesAsync e obtém o ID gerado antes de chamar o Azure.
Retorna 202 com `{ "id": 1, "operationId": "...", "status": "Sent" }` e Location.
Sent significa que a solicitação foi aceita pelo SDK; a entrega é confirmada pelo webhook.
Uma exceção de envio grava Failed e retorna erro 500 sem detalhes internos.
Cancelamento grava Failed com indicação de resultado indeterminado, pois o provedor pode já ter aceitado.

`GET /api/emails/{id}?systemId=1` retorna ID, sistema, DeliveryReportTypeId,
status, OperationId e datas. ID de outro sistema retorna 404; sistema inexistente retorna 400.
As datas seguem o fuso da sessão MySQL; configure o servidor em UTC para uniformidade.

`GET /health` informa que o processo está ativo; não testa conexões externas.

## Event Grid

Configure uma assinatura do evento `Microsoft.Communication.EmailDeliveryReportReceived`,
no formato **Event Grid Schema**, com destino HTTPS `/api/webhooks/event-grid`.
Configure o cabeçalho de entrega personalizado **X-Webhook-Key** como secreto,
com o mesmo valor de EventGrid__WebhookKey, inclusive para a validação da assinatura.
O handshake SubscriptionValidationEvent responde com validationResponse.

Relatórios usam data.messageId + data.recipient para localizar o log. Status são
resolvidos pelo nome na tabela DeliveryReportTypes, sem IDs fixos no código.
Eventos desconhecidos são ignorados; relatórios malformados retornam 400.
Relatórios anteriores à gravação de OperationId retornam 503 para permitir retry.
Duplicatas são aceitas e estados finais não regridem. O primeiro estado final prevalece.
Habilite dead-letter na assinatura para investigar eventos não processados.

Referências: [envio pelo SDK](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/communication.email-readme)
e [contrato de eventos](https://learn.microsoft.com/en-us/azure/event-grid/communication-services-email-events).

## Testes

```powershell
dotnet test test/EmailBlastCommunicationServices.UnitTests -c Release
dotnet test test/EmailBlastCommunicationServices.IntegrationTests -c Release
dotnet test test/EmailBlastCommunicationServices.ArchTests -c Release
```

Unitários e testes HTTP em memória usam substitutos do EmailClient e da persistência;
nenhum email é enviado. Integração usa MySQL real: configure TEST_MYSQL_CONNECTION_STRING
para um servidor **exclusivo de testes**, com permissão de criar/remover bancos.
O teste cria um banco emailblast_test_GUID, aplica os scripts e remove somente esse banco.
Sem essa variável, o teste de integração é explicitamente pulado.

## Limitações operacionais

SystemId identifica um sistema e não é um segredo ou autenticação forte. Restrinja o acesso
de rede à API até implementar as API Keys previstas. O webhook exige seu próprio segredo.
Não há reenvio automático nem chave de idempotência: repetir POST pode enviar outro email.
Se o processo cair, ou o banco falhar após a aceitação pelo Azure, o log pode permanecer
Queued; investigue antes de reenviar para evitar duplicação. Não existe transação distribuída
entre MySQL e Azure. A atualização final tem um token independente de até 15 segundos
para persistir mesmo quando o cliente HTTP desconecta.
