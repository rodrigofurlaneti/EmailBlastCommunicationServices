#!/usr/bin/env python3
"""Provisiona somente EmailBlast. Padrao: simular; --apply para aplicar."""
import argparse
import fcntl
import grp
import os
import pathlib
import pwd
import re
import shutil
import subprocess
import sys

HERE = pathlib.Path(__file__).resolve().parent
BASE = pathlib.Path('/opt/emailblast-api')
UNIT = pathlib.Path('/etc/systemd/system/emailblast-api.service')
SITE = pathlib.Path('/etc/nginx/sites-available/emailblast')
LINK = pathlib.Path('/etc/nginx/sites-enabled/emailblast')
ENV = pathlib.Path('/etc/emailblast/api.env')
SERVICE = 'emailblast-api.service'

def run(*args):
    return subprocess.run(args, check=True, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE).stdout.strip()

def require(condition, message):
    if not condition:
        raise RuntimeError(message)

def prop(name):
    return run('systemctl', 'show', SERVICE, '-p', name, '--value')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    require(os.geteuid() == 0, 'Execute com sudo.')
    for cmd in ('dotnet', 'nginx', 'systemctl', 'ss', 'useradd', 'jq', 'curl', 'flock', 'tar', 'find', 'readlink'):
        require(shutil.which(cmd), 'Dependencia ausente: ' + cmd)
    with open('/run/lock/emailblast-deploy.lock', 'w') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        runtimes = run('/usr/bin/dotnet', '--list-runtimes')
        require(re.search(r'^Microsoft.AspNetCore.App 9\.', runtimes, re.M), 'Instale ASP.NET Core Runtime 9 antes de continuar.')
        require(run('systemctl', 'is-active', 'nginx') == 'active', 'Nginx precisa estar ativo.')
        run('nginx', '-t')
        require(prop('ActiveState') in ('inactive', 'failed'), 'EmailBlast ativo: esta preparacao nao migra uma API em execucao.')
        require(prop('LoadState') != 'loaded' or prop('FragmentPath') == str(UNIT), 'Servico EmailBlast carregado de local inesperado; revisar.')
        require(not prop('DropInPaths'), 'Existem overrides do EmailBlast; revisar antes de preparar.')
        require(not run('ss', '-H', '-lnt', 'sport = :5201'), 'Porta 5201 ocupada; nenhum processo sera interrompido.')
        for p in (BASE, BASE / 'releases', BASE / 'shared', ENV.parent, UNIT, SITE, ENV):
            require(not p.is_symlink(), 'Caminho simbolico inesperado: ' + str(p))
        require(not (BASE / 'current').exists() and not (BASE / 'current').is_symlink(), 'Ja existe current; usar CD para atualizar, nao a preparacao.')
        require(not (BASE / 'releases').exists() or not any((BASE / 'releases').iterdir()), 'Existem releases: revisar instalacao existente.')
        if UNIT.exists():
            old_unit = UNIT.read_text()
            require('EmailBlastCommunicationServices.Api.dll' in old_unit and '/etc/emailblast/api.env' in old_unit, 'Servico existente nao reconhecido.')
        if LINK.exists() or LINK.is_symlink():
            require(LINK.is_symlink() and LINK.resolve() == SITE, 'Link Nginx existente nao reconhecido.')
        config = run('nginx', '-T')
        origin = ''
        listeners = []
        for line in config.splitlines():
            match = re.match(r'# configuration file (.+):$', line)
            if match:
                origin = match.group(1)
            if re.search(r'^\s*listen\s+(?:[^\s;]+:)?8201(?:\s|;)', line):
                listeners.append(origin)
        require(all(pathlib.Path(p).resolve() == SITE for p in listeners), 'Porta 8201 configurada em outro site; preservar e revisar.')
        if SITE.exists():
            existing = SITE.read_text()
            require(existing == (HERE / 'emailblast.nginx.conf').read_text(), 'Site EmailBlast foi customizado; revisar antes de substituir.')
        if run('ss', '-H', '-lnt', 'sport = :8201'):
            require(bool(listeners), 'Porta 8201 ocupada fora do site EmailBlast conhecido.')
        try:
            account = pwd.getpwnam('emailblast')
            require(account.pw_uid != 0 and account.pw_shell in ('/usr/sbin/nologin', '/sbin/nologin'), 'Usuario emailblast existente nao e uma conta de servico reconhecida.')
        except KeyError:
            account = None
        if account is not None:
            require(grp.getgrnam('emailblast').gr_gid == account.pw_gid, 'Grupo primario do usuario emailblast inesperado.')
        print('Plano: /opt/emailblast-api/{releases,shared}; current sera criado pelo primeiro CD.')
        print('Servico emailblast-api, usuario emailblast, Nginx 8201 -> 127.0.0.1:5201.')
        print('Preserva api.env existente e todos os arquivos da instalacao antiga em /home.')
        print('Nao inicia APIs, nao altera banco/firewall e nao reinicia outros servicos.')
        if not args.apply:
            print('SIMULACAO OK. Para aplicar: sudo bash deploy/prepare-vm.sh --apply')
            return
        originals = {}
        created_dirs = []
        new_link = False
        was_enabled = subprocess.run(['systemctl', 'is-enabled', SERVICE], capture_output=True, text=True).stdout.strip() == 'enabled'
        def mkdir(p, mode):
            if not p.exists():
                p.mkdir(mode=mode)
                created_dirs.append(p)
            require(p.is_dir(), 'Nao e diretorio: ' + str(p))
        def write(p, content, mode):
            originals[p] = (p.read_bytes(), p.stat().st_mode & 0o777) if p.exists() else None
            p.write_bytes(content)
            p.chmod(mode)
        try:
            if account is None:
                run('useradd', '--system', '--user-group', '--home-dir', str(BASE), '--no-create-home', '--shell', '/usr/sbin/nologin', 'emailblast')
            mkdir(BASE, 0o755)
            mkdir(BASE / 'releases', 0o755)
            mkdir(BASE / 'shared', 0o755)
            mkdir(ENV.parent, 0o700)
            if not ENV.exists():
                write(ENV, (HERE / 'api.env.example').read_bytes(), 0o600)
            require(ENV.stat().st_uid == 0 and ENV.stat().st_mode & 0o077 == 0, 'api.env deve pertencer a root e nao permitir acesso a grupo/outros.')
            write(UNIT, (HERE / 'emailblast-api.service').read_bytes(), 0o644)
            write(SITE, (HERE / 'emailblast.nginx.conf').read_bytes(), 0o644)
            if not LINK.is_symlink():
                LINK.symlink_to(SITE)
                new_link = True
            run('nginx', '-t')
            run('systemctl', 'daemon-reload')
            run('systemctl', 'enable', SERVICE)
            run('systemctl', 'reload', 'nginx')
        except Exception:
            if not was_enabled:
                subprocess.run(['systemctl', 'disable', SERVICE], capture_output=True)
            if new_link:
                LINK.unlink(missing_ok=True)
            for p, previous in reversed(list(originals.items())):
                if previous is None:
                    p.unlink(missing_ok=True)
                else:
                    p.write_bytes(previous[0])
                    p.chmod(previous[1])
            for p in reversed(created_dirs):
                try:
                    p.rmdir()
                except OSError:
                    pass
            run('systemctl', 'daemon-reload')
            run('nginx', '-t')
            run('systemctl', 'reload', 'nginx')
            print('Arquivos restaurados. Uma conta emailblast criada permanece disponivel.', file=sys.stderr)
            raise
        print('PREPARACAO CONCLUIDA. Nenhum backup criado. API ainda nao iniciada.')
        print('Preencha /etc/emailblast/api.env, prepare o banco e habilite DEPLOY_ENABLED=true.')
        print('Nginx pode responder 502 ate o primeiro deploy. Nenhum outro servico foi reiniciado.')

if __name__ == '__main__':
    try:
        main()
    except Exception as exc:
        # Nao imprimir stdout de comandos/configuracoes: pode conter segredos.
        print('INTERROMPIDO: ' + str(exc), file=sys.stderr)
        sys.exit(1)
