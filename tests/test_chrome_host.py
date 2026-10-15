"""Simula o Chrome chamando o MyDownload como native messaging host (mesmo protocolo)."""
import hashlib, http.server, json, os, struct, subprocess, sys, tempfile, threading, time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EXE = Path(os.environ.get('DOWNLOAD_MANAGER_TEST_EXE', str(ROOT / 'MyDownload.exe')))
ORIGIN = 'chrome-extension://mnbmcnjoiikldfeefbacbnndinoalbep/'
DATA = bytes(range(256)) * 8192
WORK = Path(tempfile.gettempdir()) / 'MyDownload-tests' / ('chrome-' + time.strftime('%Y%m%d-%H%M%S'))
APPDATA = WORK / 'data'
APPDATA.mkdir(parents=True)
(APPDATA / 'settings.json').write_text(json.dumps({'Folder': str(WORK / 'downloads'), 'AskFolder': False, 'WatchClipboard': False, 'Notify': False, 'TrayHintShown': True}), encoding='utf-8')

class Handler(http.server.BaseHTTPRequestHandler):
    def log_message(self, *args): pass
    def do_GET(self):
        if self.path == '/login':
            body = b'<html>entre na sua conta</html>'
            self.send_response(200); self.send_header('Content-Type', 'text/html'); self.send_header('Content-Length', str(len(body))); self.end_headers(); self.wfile.write(body); return
        name = self.path.strip('/')
        start, end = 0, len(DATA) - 1
        value = self.headers.get('Range')
        if value:
            a, b = value[6:].split('-'); start = int(a); end = int(b) if b else end
        self.send_response(206 if value else 200)
        self.send_header('Content-Length', str(end - start + 1))
        self.send_header('Content-Type', 'application/octet-stream')
        self.send_header('Content-Disposition', f'attachment; filename="{name}"')
        self.send_header('ETag', '"fixo"')
        if value: self.send_header('Content-Range', f'bytes {start}-{end}/{len(DATA)}')
        self.end_headers()
        try: self.wfile.write(DATA[start:end + 1])
        except OSError: pass

server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler)
threading.Thread(target=server.serve_forever, daemon=True).start()
base = f'http://127.0.0.1:{server.server_port}'

def host(message):
    body = json.dumps(message).encode()
    process = subprocess.run([str(EXE), ORIGIN, '--parent-window=0', '--data', str(APPDATA)], input=struct.pack('<I', len(body)) + body,
                             stdout=subprocess.PIPE, timeout=60)
    out = process.stdout
    length = struct.unpack('<I', out[:4])[0]
    return json.loads(out[4:4 + length].decode())

def history():
    path = APPDATA / 'history.json'
    if not path.exists(): return []
    try: return json.loads(path.read_text(encoding='utf-8-sig'))['Downloads']
    except (ValueError, OSError): return []

def wait_completed(name, seconds=30):
    limit = time.time() + seconds
    while time.time() < limit:
        for entry in history():
            job = entry['Download']
            if job.get('Filename') == name and job.get('State') == 'Completed': return Path(job['Folder']) / name
        time.sleep(0.5)
    raise AssertionError(f'{name} nao concluiu: {history()}')

def stop_app():
    subprocess.run(['powershell', '-NoProfile', '-Command',
        f"Get-CimInstance Win32_Process -Filter \"Name='MyDownload.exe'\" | Where-Object {{ $_.CommandLine -like '*{APPDATA}*' }} | ForEach-Object {{ Stop-Process -Id $_.ProcessId -Force }}"])

outcomes = []
try:
    out = subprocess.run([str(EXE), '--install-extension', '--data', str(APPDATA)], stdout=subprocess.PIPE, timeout=60)
    folder = APPDATA / 'chrome-extension'
    files = sorted(p.name for p in folder.iterdir())
    assert files == ['background.js', 'icon128.png', 'icon16.png', 'icon32.png', 'icon48.png', 'manifest.json', 'popup.html', 'popup.js'], files
    manifest = json.loads((APPDATA / 'com.mydownload.app.json').read_text(encoding='utf-8'))
    assert manifest['path'] == str(EXE) and manifest['allowed_origins'] == [ORIGIN] and manifest['type'] == 'stdio', manifest
    key = json.loads((folder / 'manifest.json').read_text(encoding='utf-8'))['key']
    import base64
    digest = hashlib.sha256(base64.b64decode(key)).hexdigest()[:32]
    assert ORIGIN == 'chrome-extension://' + ''.join(chr(97 + int(c, 16)) for c in digest) + '/'
    outcomes.append('PASS: extensao extraida com icones; host aponta para o exe; ID da extensao bate com a chave')
    assert host({'action': 'ping'})['ok'] is True
    outcomes.append('PASS: ping do Chrome responde')
    answer = host({'action': 'add', 'url': base + '/login', 'check': True})
    assert answer['ok'] is False and not history(), answer
    outcomes.append('PASS: link que abre pagina de login fica no Chrome (' + answer['reason'] + ')')
    assert host({'action': 'add', 'url': base + '/fechado.bin', 'referrer': base + '/', 'filename': 'fechado.bin', 'size': len(DATA), 'check': True})['ok'] is True
    path = wait_completed('fechado.bin')
    assert hashlib.sha256(path.read_bytes()).digest() == hashlib.sha256(DATA).digest()
    referrer = [e['Download'].get('Referrer') for e in history() if e['Download'].get('Filename') == 'fechado.bin']
    assert referrer == [base + '/'], referrer
    outcomes.append('PASS: com o app fechado, o host abre o app e o download termina certo (com a pagina de origem)')
    assert host({'action': 'add', 'url': base + '/aberto.bin', 'check': True})['ok'] is True
    path = wait_completed('aberto.bin')
    assert hashlib.sha256(path.read_bytes()).digest() == hashlib.sha256(DATA).digest()
    outcomes.append('PASS: com o app aberto, o link vai direto para a janela e termina certo')
    assert host({'action': 'add', 'url': base + '/menu.bin', 'check': False})['ok'] is True
    wait_completed('menu.bin')
    outcomes.append('PASS: "Baixar com MyDownload" do botao direito')
finally:
    stop_app()
    server.shutdown()
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    print('\n'.join(outcomes))
    print('Resultados:', WORK)
