#!/usr/bin/env python3
"""Measure tracker module AudioClips (.mod, .xm, .s3m, .it): metadata, importer settings,
GetData/SetData, AudioSource playback/controls, and UnityWebRequestMultimedia downloads."""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import os
from pathlib import Path
import sys
import threading

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = ('Assets/Editor', 'Assets/Scenes', 'Packages', 'ProjectSettings')
AUDIO = PROBE / 'Assets' / 'Audio'
SERVED = {
    'tone.mod': lambda: (AUDIO / 'tone.mod').read_bytes(),
    'tone.xm': lambda: (AUDIO / 'tone.xm').read_bytes(),
    'tone.s3m': lambda: (AUDIO / 'tone.s3m').read_bytes(),
    'tone.it': lambda: (AUDIO / 'tone.it').read_bytes(),
    'invalid.mod': lambda: b'this is not a tracker module file at all\n',
}


class Tracks(BaseHTTPRequestHandler):
    def do_GET(self):
        name = self.path.split('?')[0].lstrip('/')
        if name not in SERVED:
            self.send_response(404)
            self.end_headers()
            return
        body = SERVED[name]()
        self.send_response(200)
        self.send_header('Content-Type', 'application/octet-stream')
        self.send_header('Content-Length', str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *args):
        pass


def serve():
    server = ThreadingHTTPServer(('127.0.0.1', 0), Tracks)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    os.environ['TRACKER_URL_BASE'] = f'http://127.0.0.1:{server.server_address[1]}/'
    return server


if __name__ == '__main__':
    serve()
    sys.exit(value_probe.main(
        PROBE, 'ProbeRunner.Record', 'AUDIO_TRACKER_OUT',
        tolerance=0.05,
        engine_args=['--frames', '20000', '--size', '320x180'],
        batch=True,
        record_timeout=300,
        base=(PROBE.parent / 'video', BASE),
    ))
