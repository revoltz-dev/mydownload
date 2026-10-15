import http.server, threading, time, subprocess, sys, json, tempfile
from pathlib import Path
data=bytes(range(256))*16384
other=bytes(reversed(data))
queue_gate=threading.Event()
class Handler(http.server.BaseHTTPRequestHandler):
    def log_message(self,*args): pass
    def do_GET(self):
        if self.path=='/release':
            queue_gate.set();self.send_response(200);self.send_header('Content-Length','0');self.end_headers();return
        # /alt-* serves the same bytes from "another mirror"; /diff the same size with other bytes.
        body=other if self.path=='/diff' else data[:len(data)//2] if self.path=='/short' else data
        start,end=0,len(body)-1
        value=self.headers.get('Range')
        if value:
            a,b=value[6:].split('-');start=int(a);end=int(b) if b else end
        self.send_response(206 if value else 200)
        self.send_header('Content-Length',str(end-start+1))
        self.send_header('Content-Disposition','attachment; filename="same-name.bin"')
        self.send_header('ETag','"alt"' if self.path.startswith('/alt') else '"diff"' if self.path=='/diff' else '"stable"')
        if value:self.send_header('Content-Range',f'bytes {start}-{end}/{len(body)}')
        self.end_headers()
        try:
            if self.path=='/stall' and end>start:
                time.sleep(20);return
            if self.path.startswith('/queue-') and end>start:queue_gate.wait(25)
            for n in range(start,end+1,32768):
                self.wfile.write(body[n:min(end+1,n+32768)])
                self.wfile.flush()
                if end>start and not self.path.startswith('/alt'):time.sleep(.045)
        except (OSError,ConnectionError):pass
server=http.server.ThreadingHTTPServer(('127.0.0.1',0),Handler)
threading.Thread(target=server.serve_forever,daemon=True).start()
# build-tests.ps1 compiles the test programs here; results also stay out of the project folder.
work=Path(tempfile.gettempdir())/'MyDownload-tests'
out=work/('coordinator-'+time.strftime('%Y%m%d-%H%M%S'));out.mkdir(parents=True)
executable='UiTests.exe' if '--ui' in sys.argv else 'CoordinatorTests.exe'
result=subprocess.run([str(work/executable),str(out),f'http://127.0.0.1:{server.server_port}'],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=240,creationflags=subprocess.CREATE_NO_WINDOW)
(out/'results.txt').write_bytes(result.stdout)
sys.stdout.reconfigure(encoding='utf-8',errors='replace')
print(result.stdout.decode('cp850',errors='replace'),flush=True)
print('RESULTS',out)
server.shutdown()
sys.exit(result.returncode)
