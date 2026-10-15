"""Testes reais do EXE contra um servidor HTTP local controlado."""
import hashlib
import http.server
import json
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import threading
import time
import os

RESULTS = (Path(sys.argv[1]) if len(sys.argv)>1 else Path(tempfile.gettempdir())/'MyDownload-tests') / ('run-' + time.strftime('%Y%m%d-%H%M%S'))
RESULTS.mkdir(parents=True)
ROOT = Path(__file__).resolve().parents[1]
EXE = Path(os.environ.get('DOWNLOAD_MANAGER_TEST_EXE', str(ROOT / 'MyDownload.exe')))
DATA = bytes(range(256)) * (2 * 4096)
EXPECTED = hashlib.sha256(DATA).hexdigest()
SPARSE = 0x200
events = []
dropped = set()
version = {'/change': 'one'}
lock = threading.Lock()

class Server(http.server.ThreadingHTTPServer):
    daemon_threads = True

class Handler(http.server.BaseHTTPRequestHandler):
    protocol_version = 'HTTP/1.1'
    def log_message(self, *args): pass
    def do_GET(self):
        self.connection.setsockopt(socket.IPPROTO_TCP,socket.TCP_NODELAY,1)
        path = self.path
        header = self.headers.get('Range')
        with lock: events.append((path, header, self.headers.get('If-Range')))
        if path == '/missing':
            self.send_error(404); return
        start, end = 0, len(DATA)-1
        ranged = bool(header) and path != '/plain' and path != '/stream'
        if path == '/ignores' and header != 'bytes=0-0': ranged = False
        if ranged:
            bounds = header.removeprefix('bytes=').split('-')
            start, end = int(bounds[0]), int(bounds[1]) if bounds[1] else len(DATA)-1
        self.send_response(206 if ranged else 200)
        if path != '/stream': self.send_header('Content-Length',str(end-start+1))
        self.send_header('Content-Type','application/octet-stream')
        self.send_header('Content-Disposition','attachment; filename="arquivo.bin"')
        self.send_header('ETag','"'+version.get(path,'one')+'"')
        self.send_header('Connection','close')
        if ranged:self.send_header('Content-Range',f'bytes {start}-{end}/{len(DATA)}')
        self.end_headers()
        try:
            if path == '/drop' and header != 'bytes=0-0':
                with lock:
                    drop = path not in dropped
                    dropped.add(path)
                if drop:
                    self.wfile.write(DATA[start:min(start+200000,end+1)])
                    self.wfile.flush()
                    self.connection.shutdown(socket.SHUT_RDWR)
                    self.connection.close()
                    return
            for position in range(start,end+1,32768):
                self.wfile.write(DATA[position:min(position+32768,end+1)])
                if path in ('/slow','/change'): time.sleep(.004)
                # Only the connection that starts at byte 0 is slow, so the others must help it.
                if path == '/skew' and start == 0 and end > 0: self.wfile.flush(); time.sleep(.05)
        except (BrokenPipeError,ConnectionResetError,ConnectionAbortedError,OSError): pass

server = Server(('127.0.0.1',0),Handler)
threading.Thread(target=server.serve_forever,daemon=True).start()
base = f'http://127.0.0.1:{server.server_port}'
outcomes=[]
def run(name,path,*options,expected=0):
    folder=RESULTS/name;folder.mkdir(exist_ok=True)
    args=[str(EXE),'--download-test',base+path,str(folder),*options]
    started=time.monotonic()
    process=subprocess.run(args,timeout=150,creationflags=subprocess.CREATE_NO_WINDOW)
    seconds=time.monotonic()-started
    print(name, 'exit',process.returncode,'seconds',round(seconds,2),flush=True)
    assert process.returncode==expected,(name,process.returncode,(folder/'result.json').read_text() if (folder/'result.json').exists() else 'no result')
    result=json.loads((folder/'result.json').read_text(encoding='utf-8-sig'))
    if expected==0:
        output=folder/result['Filename']
        assert hashlib.sha256(output.read_bytes()).hexdigest()==EXPECTED,name
        assert result['Total']==len(DATA),result
        assert not (folder/'.mydownload'/'test'/'data.part').exists(),(name,'data.part left behind')
        assert not os.stat(output).st_file_attributes & SPARSE,(name,'final file still sparse')
    return folder,result,seconds

def job(folder):
    return json.loads((folder/'.mydownload'/'test'/'job.json').read_text(encoding='utf-8-sig'))

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

try:
    folder,state,_=run('network-drop','/drop')
    assert state['Sparse'] and len(state['Starts'])>=4,(state['Sparse'],state['Starts'])
    assert any(p=='/drop' and h and h!='bytes=0-0' and int(h[6:].split('-')[0])%(len(DATA)//4)!=0 for p,h,_ in events)
    outcomes.append('PASS: queda de conexao, retomada parcial e arquivo exato, sem etapa de montagem')
    folder,state,_=run('pause','/slow','bytes:65536',expected=2)
    data=folder/'.mydownload'/'test'/'data.part'
    saved=sum(job(folder)['Done'])
    assert 0<saved<len(DATA),saved
    assert os.stat(data).st_file_attributes & SPARSE,'data.part should be sparse'
    assert data.stat().st_size==len(DATA)
    run('pause','/slow')
    outcomes.append('PASS: pausa, arquivo esparso, encerramento do processo e retomada do disco')
    folder,state,_=run('ignored-range','/ignores',expected=1)
    assert not (folder/'arquivo.bin').exists()
    assert sum(job(folder)['Done'])==0
    outcomes.append('PASS: servidor ignora Range; resposta recusada sem gravar bytes')
    run('simple-server','/plain')
    run('unknown-length','/stream')
    outcomes.append('PASS: servidores sem Range e sem tamanho informado')
    folder,state,_=run('changed-server','/change','bytes:65536',expected=2)
    data=folder/'.mydownload'/'test'/'data.part'
    before=(digest(data),job(folder)['Done'])
    version['/change']='two'
    run('changed-server','/change',expected=1)
    assert before==(digest(data),job(folder)['Done'])
    outcomes.append('PASS: ETag alterado; dados salvos preservados sem misturar versoes')
    run('not-found','/missing',expected=1)
    outcomes.append('PASS: HTTP 404 para com mensagem, sem repeticao infinita')
    # Name collision must preserve an existing user file.
    folder=RESULTS/'collision';folder.mkdir();(folder/'arquivo.bin').write_bytes(b'arquivo do usuario')
    folder,state,_=run('collision','/drop')
    assert state['Filename']=='arquivo (1).bin'
    assert (folder/'arquivo.bin').read_bytes()==b'arquivo do usuario'
    outcomes.append('PASS: arquivo existente preservado com nome alternativo')
    folder,state,_=run('work-stealing','/skew','split:65536')
    assert len(state['Starts'])>4,('no range was split',state['Starts'])
    outcomes.append(f"PASS: conexoes livres dividem o trecho mais lento ({len(state['Starts'])} trechos a partir de 4)")
    folder,state,_=run('no-sparse','/drop','sparse:off')
    assert not state['Sparse'] and len(state['Starts'])==1,(state['Sparse'],state['Starts'])
    outcomes.append('PASS: disco sem arquivo esparso usa uma conexao em ordem, retoma e nao monta nada')
    folder,state,seconds=run('speed-limit','/plain','limit:700000')
    assert seconds>=2.0,seconds
    outcomes.append(f'PASS: limite de velocidade respeitado ({len(DATA)} bytes em {seconds:.1f} s a 700 KB/s)')
    folder,state,seconds=run('job-limit','/slow','joblimit:700000')
    assert seconds>=2.0,seconds
    outcomes.append(f'PASS: limite so deste download respeitado ({seconds:.1f} s a 700 KB/s)')
    folder,state,_=run('custom-name','/slow','name:meu nome.bin')
    assert state['Filename']=='meu nome.bin' and (folder/'meu nome.bin').exists(),state['Filename']
    outcomes.append('PASS: nome escolhido pelo usuario substitui o do servidor')
finally:
    server.shutdown()
    (RESULTS/'results.txt').write_text('\n'.join(outcomes),encoding='utf-8')
    print('\n'.join(outcomes),flush=True)
    print('Resultados:',RESULTS,flush=True)
