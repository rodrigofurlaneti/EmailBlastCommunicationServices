# CI/CD — EmailBlast Communication Services

Adaptado dos [workflows CloudShopping](https://github.com/rodrigofurlaneti/CloudShopping/tree/main/.github/workflows).
Estrutura conferida na VM em 16/09/2026: aplicações .NET executadas por systemd,
Nginx na porta pública e Kestrel acessível somente por loopback.

| Item | Valor |
| --- | --- |
| VM | `191.234.174.58` |
| Porta pública (Nginx) | `8201` |
| Kestrel interno | `127.0.0.1:5201` |
| Pasta do projeto | `/home/rodrigomadeira31/EmailBlastCommunicationServices` |
| WorkingDirectory | `Pasta do projeto/backend/src/EmailBlastCommunicationServices.Api` |
| Binários | `WorkingDirectory/bin/Release/net9.0` (link para a release ativa) |
| Releases | `Pasta do projeto/releases/SHA-RUN-ATTEMPT/api` |
| Serviço | `emailblast-api.service` |
| Usuário do serviço | `root`, conforme CloudShopping/WhatsApp existentes |
| Configurações | `/etc/emailblast/api.env`, root:root, modo 600 |
| Site Nginx exclusivo | `/etc/nginx/sites-available/emailblast` |

## Fluxo

PRs e pushes fora de main executam CI. Push em main executa CD, que chama o
mesmo CI antes de publicar. Execução manual também está disponível; deploy
somente a partir de main e com a variável de repositório `DEPLOY_ENABLED=true`.
O CI compila .NET 9, roda UnitTests, ArchTests e IntegrationTests com MySQL
8.0.43 descartável e publica o artefato da API sem appsettings locais.
Não há frontend implementado neste repositório para publicar.

O CD transfere o artefato da mesma execução por SSH com host verificado,
valida o pacote, cria uma release, troca o link dos binários, reinicia somente
`emailblast-api.service` e verifica `/health` nas portas 5201 e 8201.
Falha restaura a release anterior; no primeiro deploy, sem versão anterior,
o serviço fica parado. Há breve indisponibilidade. GitHub concurrency e flock
impedem dois deploys simultâneos desta API.

O deploy não modifica as outras aplicações, Nginx ou banco de produção.
Configurações ficam fora dos artefatos. `/health` verifica vida do processo,
não MySQL ou Azure. Releases e pacotes em `/tmp/emailblast-*` são preservados;
acompanhar espaço disponível.

## Preparação única da VM

A preparação já foi executada em 16/09/2026 na VM acima: pastas, template de
ambiente protegido, serviço habilitado (ainda não iniciado), site Nginx
separado e regra UFW TCP 8201. Nginx foi validado e recebeu apenas reload.
Os PIDs e horários de ativação de CloudShopping, DingFood, CheckPay, WhatsApp,
MySQL e Nginx permaneceram iguais. O arquivo compartilhado `dingfood` não mudou.

Para uma nova instalação com a mesma estrutura, copiar os arquivos de `deploy/`
para a VM e executar `sudo bash deploy/prepare-vm.sh`. O script recusa sobrescrever
uma instalação existente e exige as portas 8201 e 5201 livres. Não executar
novamente para atualizar: o workflow usa `deploy.sh`.

O script requer Linux/systemd, ASP.NET Core Runtime 9 em `/usr/bin/dotnet`,
Bash, Python 3, curl, flock, Nginx, UFW e ferramentas GNU.
Mudanças no `.service` ou site Nginx exigem instalação administrativa;
o CD publica somente os binários.

### Configuração necessária antes do primeiro deploy

```bash
sudoedit /etc/emailblast/api.env
```

Preencher os valores fictícios do template: conexão MySQL, Azure Communication
Services, remetente verificado e chave do webhook. Não enviar os segredos por
conversa nem versioná-los. O arquivo usa sintaxe EnvironmentFile do systemd,
não é script shell. O deploy recusa o template com os marcadores originais.
Não iniciar a API antes do primeiro deploy criar o link dos binários.

Preparar o banco separadamente conforme [backend/README.md](../backend/README.md),
aplicando os scripts SQL na ordem documentada ao banco correto. A preparação
da VM e o deploy não executam SQL nem migrations.

### Rede Azure

Além do UFW, o NSG associado à interface da VM precisa permitir entrada TCP
8201 para `172.16.0.4`, somente das origens autorizadas. A porta interna 5201
não deve ser liberada. Grupo de recursos: `RG-APP-FURLANETI-BRAZILSOUTH-SPOT-PROD`.
NSG: `VMFurlaneti-brazilsouth-Spot-Prod-NSG-SPOT-PROD`.

O webhook Event Grid requer HTTPS; certificado e proxy HTTPS são uma etapa
separada. As rotas internas da API não têm autenticação forte; considerar isso
ao escolher as origens autorizadas. Enquanto não houver primeiro deploy,
Nginx na porta 8201 responde 502 porque não há API executando na porta 5201.

## GitHub

Environment `production`, com os secrets:

| Secret | Valor |
| --- | --- |
| `SSH_HOST` | `191.234.174.58` |
| `SSH_USER` | `azureadmin` (acesso com a chave existente verificado) |
| `SSH_PRIVATE_KEY` | Chave privada cadastrada diretamente no GitHub |
| `SSH_KNOWN_HOSTS` | Chave pública de host conferida pela sessão confiável |

Secrets de outro repositório não são herdados. Variáveis de **repositório**:
`SSH_PORT=22` (opcional) e `DEPLOY_ENABLED=true` somente após preencher o
ambiente, preparar o banco e concluir a liberação de rede. Sem a segunda,
CI roda normalmente e o job de deploy fica desabilitado.

O usuário SSH precisa executar o script de deploy com `sudo -n`. Esse modelo,
herdado da referência, concede poder administrativo ao workflow; proteger
alterações em main. A preparação não altera sudoers ou chaves SSH.

## Verificação e recuperação

```bash
sudo systemctl status emailblast-api --no-pager
curl -f http://127.0.0.1:5201/health
curl -f http://127.0.0.1:8201/health
sudo journalctl -u emailblast-api -n 100 --no-pager
readlink -f /home/rodrigomadeira31/EmailBlastCommunicationServices/backend/src/EmailBlastCommunicationServices.Api/bin/Release/net9.0
```

Para rollback manual, selecionar uma release anterior existente, substituindo
`RELEASE_ANTERIOR` pelo identificador real. Não executar durante um workflow:

```bash
base=/home/rodrigomadeira31/EmailBlastCommunicationServices
current="$base/backend/src/EmailBlastCommunicationServices.Api/bin/Release/net9.0"
sudo test -s "$base/releases/RELEASE_ANTERIOR/api/EmailBlastCommunicationServices.Api.dll" || exit 1
sudo systemctl stop emailblast-api
sudo ln -sfn "$base/releases/RELEASE_ANTERIOR/api" "$current.next"
sudo mv -Tf "$current.next" "$current"
sudo systemctl start emailblast-api
curl -f http://127.0.0.1:8201/health
```
