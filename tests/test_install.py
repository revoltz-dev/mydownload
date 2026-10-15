"""Instalacao e desinstalacao no modo de teste (--data): pasta, atalhos e registro proprios."""
import ctypes, hashlib, json, os, shutil, subprocess, sys, tempfile, time, winreg
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EXE = Path(os.environ.get('DOWNLOAD_MANAGER_TEST_EXE', str(ROOT / 'MyDownload.exe')))
T = Path(tempfile.gettempdir()) / 'MyDownload-tests' / ('install-' + time.strftime('%Y%m%d-%H%M%S'))
DATA = T / 'data'
DATA.mkdir(parents=True)
(DATA / 'settings.json').write_text(json.dumps({'Folder': str(T / 'downloads'), 'AskFolder': False, 'WatchClipboard': False, 'Notify': False, 'TrayHintShown': True}), encoding='utf-8')
KEY = r'Software\Microsoft\Windows\CurrentVersion\Uninstall\MyDownload-test-' + hashlib.sha1(str(DATA).upper().encode()).hexdigest().upper()[:12]
CHOSEN = T / 'Pasta escolhida'
INSTALLED = CHOSEN / 'MyDownload.exe'
LINKS = [DATA / 'StartMenu' / 'MyDownload.lnk', DATA / 'Desktop' / 'MyDownload.lnk']
TITLE = f'MyDownload ({DATA})'
user32 = ctypes.windll.user32

def wait(condition, seconds=20):
    limit = time.time() + seconds
    while time.time() < limit:
        if condition(): return True
        time.sleep(0.2)
    return False

def owner_path(hwnd):
    pid = ctypes.c_ulong(); user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    out = subprocess.run(['powershell', '-NoProfile', '-Command', f'(Get-Process -Id {pid.value}).Path'], stdout=subprocess.PIPE, text=True).stdout.strip()
    return Path(out) if out else None

def link_target(path):
    script = f"$l=(New-Object -ComObject WScript.Shell).CreateShortcut('{path}'); $l.TargetPath + '|' + $l.Arguments"
    return subprocess.run(['powershell', '-NoProfile', '-Command', script], stdout=subprocess.PIPE, text=True).stdout.strip()

def key_values():
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, KEY) as k:
            return {name: winreg.QueryValueEx(k, name)[0] for name in ('DisplayName', 'UninstallString', 'QuietUninstallString', 'InstallLocation')}
    except OSError: return None

def close_app():
    hwnd = user32.FindWindowW(None, TITLE)
    if hwnd: user32.PostMessageW(hwnd, 0x10, 0, 0)
    return wait(lambda: user32.FindWindowW(None, TITLE) == 0)

outcomes = []
try:
    CHOSEN.mkdir(parents=True); (CHOSEN / 'outro arquivo.txt').write_text('do usuario')
    assert subprocess.run([str(EXE), '--data', str(DATA), '--install', '--to', str(CHOSEN)], timeout=60).returncode == 0
    assert INSTALLED.read_bytes() == EXE.read_bytes()
    for link in LINKS:
        target, args = link_target(link).split('|', 1)
        assert Path(target) == INSTALLED and '--data' in args, (link, target, args)
    values = key_values()
    assert values and values['DisplayName'] == 'MyDownload (teste)' and Path(values['InstallLocation']) == CHOSEN, values
    assert str(INSTALLED) in values['UninstallString'] and values['QuietUninstallString'].endswith('--uninstall --quiet'), values
    outcomes.append('PASS: instala na pasta escolhida, com atalhos e entrada de desinstalar no Windows')

    subprocess.Popen([str(EXE), '--data', str(DATA)])
    assert wait(lambda: user32.FindWindowW(None, TITLE) != 0), 'app nao abriu'
    assert owner_path(user32.FindWindowW(None, TITLE)) == INSTALLED, 'deveria abrir pela copia instalada'
    outcomes.append('PASS: abrir o exe de fora abre a copia instalada')

    second = subprocess.Popen([str(INSTALLED), '--data', str(DATA), '--after-install'])
    time.sleep(2)
    assert second.poll() is None, 'a copia instalada deveria esperar a outra fechar'
    assert close_app() or wait(lambda: owner_path(user32.FindWindowW(None, TITLE)) == INSTALLED)
    assert wait(lambda: user32.FindWindowW(None, TITLE) != 0), 'a copia que esperava nao abriu'
    outcomes.append('PASS: depois de instalar, a copia instalada abre assim que a anterior termina de fechar')
    close_app()

    other = T / 'outra versao' / 'MyDownload.exe'
    other.parent.mkdir(); other.write_bytes(EXE.read_bytes() + b'\0')
    updating = subprocess.Popen([str(other), '--data', str(DATA)])
    assert wait(lambda: user32.FindWindowW(None, 'Atualizar MyDownload') != 0), 'tela de atualizar nao apareceu'
    user32.PostMessageW(user32.FindWindowW(None, 'Atualizar MyDownload'), 0x0100, 0x1B, 0)
    assert wait(lambda: updating.poll() is not None) and user32.FindWindowW(None, TITLE) == 0
    outcomes.append('PASS: um exe diferente do instalado mostra a tela de atualizar (e Esc fecha sem abrir nada)')

    assert subprocess.run([str(INSTALLED), '--data', str(DATA), '--uninstall', '--quiet'], timeout=60).returncode == 0
    assert key_values() is None and not any(l.exists() for l in LINKS)
    assert wait(lambda: not INSTALLED.exists(), 10), 'exe ficou'
    assert (CHOSEN / 'outro arquivo.txt').read_text() == 'do usuario' and (DATA / 'settings.json').exists()
    outcomes.append('PASS: desinstalar apaga so o que e do app: o outro arquivo da pasta escolhida e o historico ficam')

    work = T / 'downloads' / '.mydownload' / 'abc123'
    work.mkdir(parents=True)
    (work / 'data.part').write_bytes(b'x' * 1000); (work / 'job.json').write_text('{}')
    job = {'Id': 'abc123', 'Url': 'https://exemplo.com/a.zip', 'Folder': str(T / 'downloads'), 'State': 'Paused', 'WorkFolder': str(work), 'Total': 5000}
    (DATA / 'history.json').write_text(json.dumps({'Version': 3, 'Updated': '', 'Downloads': [{'Checkpoint': str(work / 'job.json'), 'Download': job, 'Bytes': 1000}]}), encoding='utf-8')
    keep = T / 'downloads' / 'baixado.zip'; keep.write_bytes(b'arquivo do usuario')
    assert subprocess.run([str(EXE), '--data', str(DATA), '--install'], timeout=60).returncode == 0
    installed = Path(key_values()['InstallLocation']) / 'MyDownload.exe'
    assert subprocess.run([str(installed), '--data', str(DATA), '--uninstall', '--quiet', '--remove-data'], timeout=60).returncode == 0
    assert not (DATA / 'history.json').exists() and not (DATA / 'settings.json').exists() and not work.exists()
    assert keep.read_bytes() == b'arquivo do usuario'
    outcomes.append('PASS: desinstalar apagando dados remove historico, configuracoes e downloads incompletos, sem tocar nos arquivos baixados')
finally:
    subprocess.run(['powershell', '-NoProfile', '-Command',
        f"Get-CimInstance Win32_Process -Filter \"Name='MyDownload.exe'\" | Where-Object {{ $_.CommandLine -like '*{DATA}*' }} | ForEach-Object {{ Stop-Process -Id $_.ProcessId -Force }}"])
    try: winreg.DeleteKey(winreg.HKEY_CURRENT_USER, KEY)
    except OSError: pass
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    print('\n'.join(outcomes))
    print('Resultados:', T)
