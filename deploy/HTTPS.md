# HTTPS em 191.234.174.58:8201

O webhook sera `https://191.234.174.58:8201/api/webhooks/event-grid`.
O certificado fica no Nginx; a API continua em `http://127.0.0.1:5201`.
O certificado precisa conter **191.234.174.58 como iPAddress no SAN**.
A porta nao faz parte do certificado. O Azure Event Grid nao aceita autoassinado.

## Certificado

Se ja existir certificado publico para esse IP, use a cadeia completa PEM e a
chave correspondente, ajustando os caminhos do exemplo Nginx. Nunca coloque
chaves privadas no repositorio ou nos artefatos.

Let's Encrypt atualmente emite certificados para IP com validade de 160 horas.
A renovacao automatica precisa estar funcionando. Para emitir com Certbot,
instale versao 5.4 ou posterior e configure a validacao HTTP-01 na porta publica
**80**, mesmo que o webhook use 8201. DNS-01 nao valida IP.

No site HTTP que atende esse IP na porta 80, adicione:

```nginx
location ^~ /.well-known/acme-challenge/ {
    root /var/lib/letsencrypt;
    default_type text/plain;
    try_files $uri =404;
}
```

Se nao existir esse site, crie um bloco `server` na porta 80 com
`server_name 191.234.174.58`, a location acima e `location / { return 404; }`.
Confira os sites existentes antes de adicionar listeners. Crie o webroot com
`sudo install -d -m 755 /var/lib/letsencrypt`, valide com `sudo nginx -t` e,
se passar, recarregue com `sudo systemctl reload nginx`. Libere TCP 80 para a
validacao ACME no firewall/NSG. Mantenha a rota acessivel para futuras renovacoes.

```bash
certbot --version
sudo certbot certonly --preferred-profile shortlived \
  --webroot --webroot-path /var/lib/letsencrypt --ip-address 191.234.174.58
sudo certbot certificates
```

Confira os caminhos reais exibidos: o exemplo usa
`/etc/letsencrypt/live/191.234.174.58/`, mas certificados existentes podem usar
outro nome. Para ensaio, adicione `--staging`; esse certificado de teste nao e
confiavel e nao deve ser instalado no webhook final.

## Migracao do Nginx e do CD

Pause publicacoes (`DEPLOY_ENABLED=false`) durante a migracao. Inspecione o site
atual e preserve eventuais regras locais de acesso. O arquivo
`emailblast-webhook.nginx.conf.example` substitui o conteudo do site **emailblast**
existente; nao deve ser habilitado como segundo site HTTP/HTTPS na mesma porta.
Ele preserva as rotas existentes, incluindo `/health`. Continue restringindo as
rotas de envio conforme a politica atual: elas ainda nao possuem autenticacao forte.

Depois de emitir o certificado e conferir caminhos e regras locais, instale a
configuracao no site existente `/etc/nginx/sites-available/emailblast`. Execute
`sudo nginx -t` e somente se passar `sudo systemctl reload nginx`. Se falhar,
restaure o conteudo anterior antes de continuar. Nao execute `prepare-vm.sh`:
este provisionador destina-se a instalacao inicial com a API inativa.

HTTP e HTTPS nao coexistem nesse listener. Atualize os clientes que usam
`http://191.234.174.58:8201` para `https://191.234.174.58:8201`.

O CD deste repositorio aceita a variavel **PUBLIC_API_SCHEME=https** (padrao:
`http`, para manter a instalacao atual funcionando ate a migracao). Publique a
alteracao do workflow e de `deploy.sh` e configure essa variavel no repositorio
antes de retomar os deploys. Em HTTPS, o health check verifica o certificado para
191.234.174.58, conectando ao Nginx local via `--connect-to`, sem desativar TLS.
O health check interno permanece HTTP na porta 5201.

## Renovacao

Depois de instalar o site HTTPS, configure um deploy hook persistente do Certbot
em `/etc/letsencrypt/renewal-hooks/deploy/`, executavel e pertencente a root:

```sh
#!/bin/sh
set -eu
nginx -t
systemctl reload nginx
```

Confirme que o agendamento de `certbot renew` instalado (timer ou cron) esta
ativo. Teste com `sudo certbot renew --dry-run` e valide tambem o hook. Monitore
falhas e vencimento; um certificado renovado precisa ser recarregado pelo Nginx.

## Validacao e Azure

Primeiro, verifique localmente no servidor sem ignorar o certificado:

```bash
curl --fail --noproxy '*' \
  --connect-to 191.234.174.58:8201:127.0.0.1:8201 \
  https://191.234.174.58:8201/health
```

De uma maquina externa, teste o webhook sem segredo:

```bash
curl --silent --show-error --output /dev/null --write-out '%{http_code}\n' \
  -X POST -H 'Content-Type: application/json' --data '[]' \
  https://191.234.174.58:8201/api/webhooks/event-grid
```

Esperado: **401**, confirmando TLS e encaminhamento ate a autenticacao, sem gravar
eventos. Nao use `-k`. A porta 8201 deve estar acessivel ao Event Grid.

Na assinatura Event Grid, use **Event Grid Schema**, a URL HTTPS acima e o
cabecalho personalizado secreto `X-Webhook-Key` com o mesmo valor de
`EventGrid__WebhookKey` em `/etc/emailblast/api.env`, inclusive para validacao.
A API ja responde ao `Microsoft.EventGrid.SubscriptionValidationEvent`.
Confirme a aprovacao da assinatura no Azure e depois acompanhe os eventos reais.
Retome os deploys com `DEPLOY_ENABLED=true` apos validar a migracao.

Referencias: [certificados para IP](https://letsencrypt.org/2026/01/15/6day-and-ip-general-availability/),
[emissao por Certbot](https://letsencrypt.org/2026/03/11/shorter-certs-certbot/),
[validacao de IP](https://letsencrypt.org/2025/07/01/issuing-our-first-ip-address-certificate/),
[validacao Event Grid](https://learn.microsoft.com/en-us/azure/event-grid/troubleshoot-subscription-validation)
e [HTTPS no Nginx](https://nginx.org/en/docs/http/configuring_https_servers.html).
