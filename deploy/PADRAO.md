# EmailBlast: instalacao e CI/CD padronizados

API .NET 9: `EmailBlastCommunicationServices.Api.dll`. Sem frontend para publicar.
Nginx 8201 -> `127.0.0.1:5201`. Servico `emailblast-api.service`, usuario `emailblast`.
Publicacao em `/opt/emailblast-api/releases/<versao>`, link `current`, diretorio `shared`.
Configuracao preservada em `/etc/emailblast/api.env`, root:root 600.

## 1. Atualizar o repositorio

Copie `.github/workflows/ci.yml`, `.github/workflows/cd.yml` e os arquivos de `deploy/`
deste pacote para os mesmos caminhos no repositorio. Nao publique chaves ou api.env real.
Deixe a variavel de repositorio `DEPLOY_ENABLED=false` durante a preparacao.

O CI foi preservado: .NET 9, testes unitarios/arquitetura/integracao com MySQL 8.0.43
descartavel, artefato `emailblast-api` sem appsettings. Acrescentada validacao do
provisionador Python. O CD chama esse CI na mesma execucao.

## 2. Preparar somente EmailBlast na VM

Envie `deploy` inteira para `/home/azureadmin/emailblast-padrao/deploy` via WinSCP.
Requisitos: `/usr/bin/dotnet` com ASP.NET Core Runtime 9, Nginx ativo, Python3, jq,
curl, flock, tar, ss, find, readlink e ferramentas GNU. Nao instala pacotes nem
atualiza runtimes compartilhados. Nao executar junto de outro CD do EmailBlast.

Primeiro simule:

```bash
sudo bash /home/azureadmin/emailblast-padrao/deploy/prepare-vm.sh
```

Depois de `SIMULACAO OK`, aplique:

```bash
sudo bash /home/azureadmin/emailblast-padrao/deploy/prepare-vm.sh --apply
```

Reconhece a unidade antiga e o site Nginx original do repositorio apenas quando a
API esta inativa. Preserva api.env. Recusa API ativa, releases existentes, overrides,
sites customizados e conflitos de porta: envie o erro para revisar a migracao.
Nao apaga a pasta antiga em /home. Nao altera outros sites nem inicia a API.
Habilita o servico com ConditionPathExists e recarrega Nginx depois de nginx -t.
Pode haver 502 na porta 8201 ate o primeiro deploy. Se falhar, tenta restaurar os
arquivos originais a partir de memoria; nenhum arquivo de backup e gerado.

## 3. Configurar segredos e banco

```bash
sudoedit /etc/emailblast/api.env
```

Preencha `ConnectionStrings__MySql`, `Database__ServerVersion`,
`COMMUNICATION_SERVICES_CONNECTION_STRING`, `Email__SenderAddress` e
`EventGrid__WebhookKey`. Use remetente verificado no Azure. Nao execute com source:
o arquivo usa sintaxe EnvironmentFile do systemd. Nao envie valores por chat.
O CD rejeita campos vazios e marcadores do template sem mostrar os valores.

A API usa MySQL 8 e nao cria esquema automaticamente. Para um banco NOVO, seguir
`sql/createdatabase.sql` e depois `sql/002_delivery_report_types.sql` do repositorio.
Confirmar o banco destino: o primeiro cria tabelas e nao deve ser repetido sobre
um esquema existente. Usar usuario exclusivo. Nenhum SQL e executado pelo CD.

`/health` informa apenas que o processo responde; nao prova acesso ao MySQL/Azure.
Nenhum email de teste e enviado pelo deploy. Os dados atuais ficam no banco.
`shared` fica preparado para futuros arquivos persistentes: cada entrada direta
e ligada ao mesmo nome na release. Nao colocar codigo ali. Se houver necessidade
de gravar arquivos, configurar permissoes e ReadWritePaths especificos; o servico
usa ProtectSystem=strict e nao tem escrita em releases.

## 4. GitHub e rede

Environment `production`; secrets `SSH_HOST`, `SSH_USER`, `SSH_PRIVATE_KEY`,
`SSH_KNOWN_HOSTS`. Variavel `SSH_PORT` opcional, padrao 22. Verificar chave de host
por sessao confiavel. Usuario SSH precisa de `sudo -n` para publicar, como nos outros
projetos; este pacote nao altera sudoers. DLL, servico e /health estao fixos no CD.

Nao abre firewall/NSG automaticamente. Para acesso externo, liberar 8201 somente
das origens autorizadas; nao liberar 5201. As rotas de envio nao possuem autenticacao
forte; nao expor irrestritamente a Internet. Event Grid requer HTTPS. Siga
[HTTPS.md](HTTPS.md) para migrar 191.234.174.58:8201 para HTTPS com certificado
publico para o IP e configurar PUBLIC_API_SCHEME=https no CD.

Depois de preparar servidor, banco e configuracao, definir `DEPLOY_ENABLED=true`
em Variables do REPOSITORIO (nao Secrets). Actions -> CD -> Run workflow -> main.
Cada push em main chama CI e publica se passar. Regras de aprovacao do environment
production continuam sendo respeitadas.

## 5. Operacao e validacao

O CD valida pacote, troca current e reinicia somente EmailBlast. Verifica 2xx interno
e via Nginx. Mantem a versao anterior durante a troca para tentar recuperar em caso
de falha; no primeiro deploy sem anterior, para somente EmailBlast. Depois do sucesso
remove releases anteriores. Sem backups ou historico: recuperar uma versao depois
da limpeza exige republicar seu commit. Nao reverte banco ou chamadas externas.
Tentativas falhas podem deixar releases, limpas no proximo deploy bem-sucedido.

```bash
sudo systemctl status emailblast-api.service --no-pager
curl --fail http://127.0.0.1:5201/health
curl --fail http://127.0.0.1:8201/health
readlink -f /opt/emailblast-api/current
```

Validacao local: bash -n, ShellCheck, actionlint e sintaxe Python. Nao executado na
VM, GitHub Actions ou integracoes externas nesta entrega.
