#!/usr/bin/env bash
set -Eeuo pipefail
[[ $EUID -eq 0 ]] || { echo 'Execute com sudo.' >&2; exit 1; }
release_id=${1:?Informe SHA-run-attempt}
[[ $release_id =~ ^[a-f0-9]{40}-[0-9]+-[0-9]+$ ]] || exit 1
archive="/tmp/emailblast-$release_id/release.tar.gz"
base=/home/rodrigomadeira31/EmailBlastCommunicationServices
working_directory="$base/backend/src/EmailBlastCommunicationServices.Api"
current="$working_directory/bin/Release/net9.0"
service=emailblast-api.service
release="$base/releases/$release_id"
for command in python3 curl systemctl flock realpath; do command -v "$command" >/dev/null; done
exec 9>/run/lock/emailblast-deploy.lock
flock -n 9 || { echo 'Outro deploy está em execução.' >&2; exit 1; }
[[ -f $archive && -f /etc/emailblast/api.env && -d $base/releases && ! -e $release ]] || exit 1
if grep -Eq 'HOST_MYSQL|RECURSO\.communication\.azure\.com|DOMINIO_VERIFICADO|SUBSTITUIR_POR_SEGREDO' /etc/emailblast/api.env; then
  echo 'Preencher /etc/emailblast/api.env antes do primeiro deploy.' >&2
  exit 1
fi
[[ $(realpath "$base") == "$base" && $(realpath "$base/releases") == "$base/releases" ]] || exit 1
[[ $(systemctl show "$service" -p WorkingDirectory --value) == "$working_directory" ]] || exit 1
systemctl show "$service" -p ExecStart --value | grep -Fq "$current/EmailBlastCommunicationServices.Api.dll"
systemctl show "$service" -p Environment --value | grep -Fq 'ASPNETCORE_URLS=http://127.0.0.1:5201'
previous=''
if [[ -L $current ]]; then
  previous=$(realpath -e "$current")
  [[ $previous == "$base/releases/"* && -s $previous/EmailBlastCommunicationServices.Api.dll ]] || exit 1
  systemctl is-active --quiet "$service"
  curl -fsS --max-time 10 http://127.0.0.1:5201/health >/dev/null
else
  [[ ! -e $current ]] || exit 1
  if systemctl is-active --quiet "$service"; then exit 1; fi
fi
umask 022
install -d -m 755 "$release"
# Validate the complete archive before extraction; never accept links/configuration.
python3 - "$archive" "$release" <<'PY'
import pathlib, sys, tarfile
with tarfile.open(sys.argv[1], 'r:gz') as bundle:
    for item in bundle.getmembers():
        path = pathlib.PurePosixPath(item.name)
        if path.is_absolute() or '..' in path.parts or not (item.isfile() or item.isdir()):
            raise SystemExit('Invalid archive member')
        if not path.parts or path.parts[0] != 'api':
            raise SystemExit('Unexpected archive directory')
        if any(part.startswith(('.env', '.local', 'appsettings')) for part in path.parts):
            raise SystemExit('Configuration is not allowed in deployment artifacts')
    bundle.extractall(sys.argv[2])
PY
test -s "$release/api/EmailBlastCommunicationServices.Api.dll"
test "$(cat "$release/api/version.txt")" = "${release_id:0:40}"
chmod -R u=rwX,go=rX "$release"
switched=false
rollback() {
  result=$?
  trap - ERR INT TERM
  if [[ $switched == true ]]; then
    echo 'Falha no deploy; restaurando release anterior.' >&2
    systemctl stop "$service" || true
    if [[ -n $previous ]]; then
      ln -sfn "$previous" "$current.next"
      mv -Tf "$current.next" "$current"
      if ! systemctl start "$service" || ! curl -fsS --retry 10 --retry-connrefused --retry-delay 2 --max-time 5 http://127.0.0.1:5201/health >/dev/null; then
        echo 'Rollback não recuperou a API; intervenção necessária.' >&2
      fi
    else
      rm -f "$current" "$current.next"
      echo 'Primeiro deploy falhou; serviço parado, sem release anterior.' >&2
    fi
  fi
  exit "$result"
}
trap rollback ERR
trap 'false' INT TERM
ln -s "$release/api" "$current.next"
switched=true
systemctl stop "$service"
mv -Tf "$current.next" "$current"
systemctl start "$service"
curl -fsS --retry 15 --retry-connrefused --retry-delay 2 --max-time 5 http://127.0.0.1:5201/health >/dev/null
systemctl is-active --quiet "$service"
curl -fsS --max-time 10 http://127.0.0.1:8201/health >/dev/null
trap - ERR INT TERM
echo "EmailBlast atualizado na porta 8201: $release_id. Release anterior: ${previous:-nenhuma}"
