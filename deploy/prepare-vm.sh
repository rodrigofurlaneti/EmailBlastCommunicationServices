#!/usr/bin/env bash
# One-time provisioning of the EmailBlast installation only.
set -Eeuo pipefail
[[ $EUID -eq 0 ]] || { echo 'Execute com sudo.' >&2; exit 1; }
source_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
base=/home/rodrigomadeira31/EmailBlastCommunicationServices
working_directory="$base/backend/src/EmailBlastCommunicationServices.Api"
service=/etc/systemd/system/emailblast-api.service
nginx_available=/etc/nginx/sites-available/emailblast
nginx_enabled=/etc/nginx/sites-enabled/emailblast
exec 9>/run/lock/emailblast-deploy.lock
flock -n 9 || exit 1
for command in dotnet python3 curl systemctl nginx ufw; do command -v "$command" >/dev/null; done
dotnet --list-runtimes | grep -q '^Microsoft.AspNetCore.App 9\.'
[[ -d /home/rodrigomadeira31 && ! -e $base && ! -e $service && ! -e /etc/emailblast ]] || {
  echo 'Instalação já existe; revisar antes de alterar.' >&2; exit 1;
}
[[ ! -e $nginx_available && ! -L $nginx_enabled && ! -e $nginx_enabled ]] || exit 1
[[ -z $(ss -H -ltn 'sport = :8201') && -z $(ss -H -ltn 'sport = :5201') ]] || {
  echo 'Porta 8201 ou 5201 já está ocupada.' >&2; exit 1;
}
nginx -t
# Record running processes and original shared Nginx file for the final check.
services=(cloudshopping-api dingfood-api checkpay-api whatsapp-api mysql nginx)
before=$(systemctl show "${services[@]}" -p Id -p MainPID -p ActiveEnterTimestamp)
shared_hash=$(sha256sum /etc/nginx/sites-available/dingfood)
install -d -o root -g root -m 755 "$base" "$base/releases" "$working_directory/bin/Release"
install -d -o root -g root -m 700 /etc/emailblast
install -o root -g root -m 600 "$source_dir/api.env.example" /etc/emailblast/api.env
install -o root -g root -m 644 "$source_dir/emailblast-api.service" "$service"
systemctl daemon-reload
systemctl enable emailblast-api.service
install -o root -g root -m 644 "$source_dir/emailblast.nginx.conf" "$nginx_available"
ln -s "$nginx_available" "$nginx_enabled"
if ! nginx -t; then
  rm -f "$nginx_enabled" "$nginx_available"
  echo 'Configuração do novo site removida; Nginx não foi recarregado.' >&2
  exit 1
fi
systemctl reload nginx
ufw allow 8201/tcp comment 'EmailBlast API'
[[ $(sha256sum /etc/nginx/sites-available/dingfood) == "$shared_hash" ]]
[[ $(systemctl show "${services[@]}" -p Id -p MainPID -p ActiveEnterTimestamp) == "$before" ]] || {
  echo 'Estado dos serviços existentes mudou; conferir antes de prosseguir.' >&2; exit 1;
}
systemctl is-active "${services[@]}"
echo 'VM preparada. Preencher /etc/emailblast/api.env antes de ativar o CD.'
echo 'Porta pública 8201 -> 127.0.0.1:5201. API ainda não publicada/iniciada.'
