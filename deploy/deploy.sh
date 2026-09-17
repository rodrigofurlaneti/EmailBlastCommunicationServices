#!/usr/bin/env bash
set -Eeuo pipefail
[[ $EUID -eq 0 ]] || { echo "Execute com sudo." >&2; exit 1; }
stage=${1:?Informe o diretorio temporario gerado pelo CD}
[[ "$stage" =~ ^/tmp/emailblast-deploy\.[a-zA-Z0-9]+$ ]]
for cmd in jq curl flock nginx systemctl tar find readlink ss python3; do command -v "$cmd" >/dev/null; done
exec 9>/run/lock/emailblast-deploy.lock
flock -n 9 || { echo 'Outro deploy do EmailBlast esta em andamento.'; exit 1; }
base=/opt/emailblast-api
release=$(jq -er .release "$stage/deploy-config.json")
service=$(jq -er .service "$stage/deploy-config.json")
entry=$(jq -er .entry "$stage/deploy-config.json")
health=$(jq -er .health "$stage/deploy-config.json")
[[ "$release" =~ ^[0-9]{14}-[0-9]+-[0-9]+$ ]]
[[ "$service" == emailblast-api.service ]]
[[ "$entry" == EmailBlastCommunicationServices.Api.dll ]]
[[ "$health" == /health ]]
internal_url="http://127.0.0.1:5201$health"
public_url="http://127.0.0.1:8201$health"
trap 'echo "Falha na linha $LINENO. Verifique preparacao do servidor, servico, portas e health check." >&2' ERR

# Configuracoes ficam fora dos artefatos; nao imprimir seus valores.
test -s /etc/emailblast/api.env
python3 - <<'PY'
from pathlib import Path
import re
p=Path('/etc/emailblast/api.env')
values={}
for line in p.read_text().splitlines():
    s=line.strip()
    if s and not s.startswith('#') and '=' in s:
        k,v=s.split('=',1)
        values[k.strip()]=v.strip().strip('"').strip("'")
required=('ConnectionStrings__MySql','COMMUNICATION_SERVICES_CONNECTION_STRING','Email__SenderAddress','EventGrid__WebhookKey')
markers=('HOST_MYSQL','RECURSO.communication.azure.com','DOMINIO_VERIFICADO','SUBSTITUIR_POR_SEGREDO','USUARIO','SENHA','accesskey=CHAVE')
for k in required:
    value=values.get(k,'')
    if not value or any(m in value for m in markers):
        raise SystemExit('Preencha a configuracao '+k+' em /etc/emailblast/api.env (nao envie segredos aos logs).')
if values.get('ASPNETCORE_URLS','http://127.0.0.1:5201') != 'http://127.0.0.1:5201':
    raise SystemExit('ASPNETCORE_URLS deve manter a porta interna 5201.')
if any(k.startswith('Kestrel__Endpoints__') for k in values):
    raise SystemExit('Revise endpoints Kestrel adicionais antes de publicar.')
PY

# Provisioning is separate: the CD never invents runtime/configuration.
[[ -d "$base/releases" && ! -L "$base/releases" ]]
[[ -d "$base/shared" && ! -L "$base/shared" ]]
[[ "$(readlink -f "$base")" == "$base" ]]
[[ "$(systemctl show "$service" -p LoadState --value)" == loaded ]]
[[ "$(systemctl show "$service" -p WorkingDirectory --value)" == "$base/current" ]]
start_command=$(systemctl show "$service" -p ExecStart --value)
[[ "$start_command" == *"$base/current/$entry"* ]]
[[ "$(systemctl show "$service" -p User --value)" == emailblast ]]
[[ "$(systemctl show "$service" -p EnvironmentFiles --value)" == *"/etc/emailblast/api.env"* ]]
nginx -t
nginx_config=$(nginx -T 2>&1)
grep -Eq 'listen[[:space:]]+(127\.0\.0\.1:)?8201([[:space:];])' <<< "$nginx_config"
grep -Eq 'proxy_pass[[:space:]]+http://127\.0\.0\.1:5201([/;])' <<< "$nginx_config"
old=''
if [[ -L "$base/current" ]]; then
  old=$(readlink -f "$base/current")
  [[ "$old" == "$base/releases/"* && -d "$old" ]]
  test -s "$old/$entry"
elif [[ -e "$base/current" ]]; then
  echo 'current deve ser link simbolico, nao pasta comum.'; exit 1
else
  # First publication: refuse to take over an existing running process.
  [[ "$(systemctl show "$service" -p ActiveState --value)" =~ ^(inactive|failed)$ ]]
  [[ -z "$(ss -H -lnt 'sport = :5201')" ]]
fi
check_url() {
  local code
  code=$(curl --silent --show-error --output /dev/null --connect-timeout 5 \
    --max-time 10 --write-out '%{http_code}' "$1") || return 1
  [[ "$code" =~ ^2[0-9][0-9]$ ]]
}
if [[ -n "$old" ]]; then
  systemctl is-active --quiet "$service"
  check_url "$internal_url"
  check_url "$public_url"
fi
new="$base/releases/$release"
[[ ! -e "$new" && ! -L "$new" ]]
install -d -m 755 "$new"
# Validar todos os membros antes de extrair. Nenhum link, caminho externo ou segredo.
python3 - "$stage/backend.tar.gz" <<'PY'
import pathlib,sys,tarfile
with tarfile.open(sys.argv[1], 'r:gz') as archive:
    for item in archive.getmembers():
        p=pathlib.PurePosixPath(item.name)
        if p.is_absolute() or '..' in p.parts or not (item.isfile() or item.isdir()):
            raise SystemExit('Arquivo de publicacao invalido.')
        if any(n.lower().startswith(('appsettings', '.env', '.local')) for n in p.parts):
            raise SystemExit('Configuracao nao pode estar no artefato.')
PY
tar --no-same-owner --no-same-permissions -xzf "$stage/backend.tar.gz" -C "$new"
[[ -z "$(find "$new" -type l -print -quit)" ]]
find "$new" -type d -exec chmod 755 {} +
find "$new" -type f -exec chmod 644 {} +
executable=${entry%.dll}
if [[ "$entry" == *.dll && -f "$new/$executable" ]]; then chmod 755 "$new/$executable"; fi
test -s "$new/$entry"
shopt -s dotglob nullglob
for item in "$base/shared"/*; do
  name=${item##*/}
  case "$name" in
    "$entry"|"$executable"|*.dll|*.so|*.deps.json|*.runtimeconfig.json)
      echo "Entrada de shared nao permitida: $name"; exit 1 ;;
  esac
  rm -rf -- "${new:?}/${name:?}"
  ln -s -- "$item" "$new/$name"
done
switched=0
committed=0
switch_link() {
  local target=$1 temp="$base/.current-$release"
  ln -sfnT -- "$target" "$temp" || return 1
  mv -Tf -- "$temp" "$base/current" || return 1
}
finish() {
  status=$?
  trap - EXIT
  if (( status != 0 && switched == 1 && committed == 0 )); then
    set +e
    if [[ -n "$old" ]]; then
      switch_link "$old"
      link_status=$?
      # Do not restart a new release when restoring its link failed.
      if (( link_status == 0 )); then
        systemctl restart "$service"
        restart_status=$?
        if (( restart_status != 0 )); then echo 'ERRO: versao anterior restaurada, mas reinicio falhou.'; fi
      else
        systemctl stop "$service"
        echo 'ERRO: nao foi possivel restaurar current; intervencao necessaria.'
      fi
    else
      systemctl stop "$service"
      if [[ -L "$base/current" && "$(readlink -f "$base/current")" == "$new" ]]; then
        rm -- "$base/current"
      fi
      echo 'Primeira publicacao falhou; nao ha versao anterior para restaurar.'
    fi
  fi
  exit "$status"
}
trap finish EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
switched=1
switch_link "$new"
systemctl restart "$service"
healthy=0
for attempt in {1..12}; do
  echo "Verificando API e proxy: tentativa $attempt/12"
  if systemctl is-active --quiet "$service" && check_url "$internal_url" && check_url "$public_url"; then
    healthy=1
    break
  fi
  sleep 5
done
(( healthy == 1 ))
committed=1
# No backups: delete older releases only after validation.
active=$(readlink -f "$base/current")
[[ "$active" == "$new" ]]
while IFS= read -r -d '' path; do
  [[ "$path" == "$active" ]] && continue
  [[ ! -L "$path" && "$(readlink -f "$path")" == "$path" && "$path" == "$base/releases/"* ]]
  rm -rf -- "$path"
done < <(find "$base/releases" -mindepth 1 -maxdepth 1 -type d -print0)
echo "EmailBlast publicado e validado: $release"
