#!/usr/bin/env python3
"""Loopback HTTP server for the probe: serves Content/ and logs every request path.

Each request appends its path (with the query) to <out>/requests.log before the response, so
the probe can count downloads. A path under /gated/ sends the first 16384 bytes of the file,
touches <out>/<name>[-<query>].sent and waits for the matching .release before sending the rest. A missing
file is a 404.
"""
import argparse
import pathlib
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

CONTENT = pathlib.Path(__file__).resolve().with_name('Content')
GATED_PART = 16384


class Handler(BaseHTTPRequestHandler):
    protocol_version = 'HTTP/1.1'

    def do_GET(self):
        with self.server.lock, (self.server.out/'requests.log').open('a') as log:
            log.write(self.path + '\n')
        path, _, query = self.path.partition('?')
        path = path.strip('/')
        gated = path.startswith('gated/')
        if gated:
            path = path[len('gated/'):]
        file = CONTENT/path
        if not file.is_file() or '..' in path:
            self.send_response(404)
            self.send_header('Content-Length', '0')
            self.send_header('Connection', 'close')
            self.end_headers()
            return
        data = file.read_bytes()
        self.send_response(200)
        self.send_header('Content-Type', 'application/octet-stream')
        self.send_header('Content-Length', str(len(data)))
        self.send_header('Connection', 'close')
        self.end_headers()
        self.close_connection = True
        try:
            if not gated:
                self.wfile.write(data)
                return
            name = pathlib.Path(path).name + ('-' + query if query else '')
            self.wfile.write(data[:GATED_PART])
            self.wfile.flush()
            (self.server.out/(name + '.sent')).touch()
            while not (self.server.out/(name + '.release')).exists():
                if self.server.stopping.wait(0.01):
                    return
            self.wfile.write(data[GATED_PART:])
        except (BrokenPipeError, ConnectionResetError):
            pass

    def log_message(self, *args):
        pass


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('out', type=pathlib.Path)
    a = p.parse_args()
    a.out.mkdir(parents=True, exist_ok=True)
    server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
    server.daemon_threads = True
    server.out = a.out
    server.lock = threading.Lock()
    server.stopping = threading.Event()
    (a.out/'url').write_text('http://127.0.0.1:%d/' % server.server_port)
    try:
        server.serve_forever()
    finally:
        server.stopping.set()
        server.server_close()
