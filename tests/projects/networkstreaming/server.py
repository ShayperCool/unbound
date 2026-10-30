#!/usr/bin/env python3
"""Loopback HTTP oracle. The client releases each body only after observing its first part."""
import argparse
import pathlib
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

FIRST = bytes((i % 251 for i in range(32768)))
LAST = bytes((i % 239 for i in range(8192)))

class Handler(BaseHTTPRequestHandler):
    protocol_version = 'HTTP/1.1'

    def do_GET(self):
        name = self.path.strip('/')
        self.send_response(404 if 'http-error' in name else 200)
        self.send_header('Content-Type', 'application/octet-stream')
        chunked = 'unknown' in name
        empty = 'empty' in name
        if chunked:
            self.send_header('Transfer-Encoding', 'chunked')
        else:
            self.send_header('Content-Length', str(0 if empty else len(FIRST) + len(LAST)))
        self.send_header('Connection', 'close')
        self.end_headers()
        self.close_connection = True
        if empty:
            return
        try:
            self.write_part(FIRST, chunked)
            (self.server.gates / (name + '.sent')).touch()
            while not (self.server.gates / (name + '.release')).exists():
                if self.server.stopping.wait(0.01):
                    return
            if 'truncated' not in name:
                self.write_part(LAST, chunked)
                if chunked:
                    self.wfile.write(b'0\r\n\r\n')
                    self.wfile.flush()
        except (BrokenPipeError, ConnectionResetError):
            pass

    def write_part(self, data, chunked):
        if chunked:
            self.wfile.write(('%x\r\n' % len(data)).encode() + data + b'\r\n')
        else:
            self.wfile.write(data)
        self.wfile.flush()

    def log_message(self, *args):
        pass

if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('directory', type=pathlib.Path)
    args = p.parse_args()
    args.directory.mkdir(parents=True, exist_ok=True)
    server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
    server.daemon_threads = True
    server.gates = args.directory
    server.stopping = threading.Event()
    (args.directory / 'url').write_text('http://127.0.0.1:%d/' % server.server_port)
    try:
        server.serve_forever()
    finally:
        server.stopping.set()
        server.server_close()
