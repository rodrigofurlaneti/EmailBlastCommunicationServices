# EmailBlastCommunicationServices

Microserviço .NET 9 / Minimal API, Azure.Communication.Email 1.1.0, Dapper e MySqlConnector.
Um destinatário por solicitação, compatível com o modelo de EmailLogs existente.

## Configuração

1. Em um MySQL 8, execute manualmente `../sql/createdatabase.sql` e depois
   `../sql/002_delivery_report_types.sql`. A API não cria nem altera o banco automaticamente.
2. Configure as variáveis de ambiente abaixo ou os campos correspondentes em
   `src/EmailBlastCommunicationServices/appsettings.Local.json` (ignorado pelo Git).

| Variável | Finalidade |
| --- | --- |
| `ConnectionStrings__MySql` | Conexão MySQL com o banco emailblastdb |
| `COMMUNICATION_SERVICES_CONNECTION_STRING` | Credencial do recurso Azure Communication Services |
| `Email__SenderAddress` | Remetente de um domínio verificado no Azure |
| `EventGrid__WebhookKey` | Segredo compartilhado com a assinatura Event Grid |

Não versione credenciais. Variáveis de ambiente prevalecem sobre os arquivos JSON.

Execute a partir de `backend`:

```powershell
dotnet build src/EmailBlastCommunicationServices.sln -c Release
dotnet run --project src/EmailBlastCommunicationServices --urls http://localhost:5080
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
O fluxo insere Queued e obtém LAST_INSERT_ID antes de chamar o Azure.
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
