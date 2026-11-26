#!/usr/bin/env python3
"""Measure a VideoPlayer playing from http URLs (a clip, a missing file, an unreachable host) in
Editor Play mode, on the video probe's project. The runner serves the video probes' clips over
http on 127.0.0.1 (VIDEO_URL_BASE) while the Editor or the engine runs."""
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import os
from pathlib import Path
import sys
import threading

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = ('Assets/Editor', 'Assets/Scenes', 'Packages', 'ProjectSettings')
# The served clips: the video probe's (no audio) and the video audio probe's (one Vorbis track).
SERVED = {'clip.webm': PROBE.parent/'video/Assets/Video/clip.webm',
          'tone.webm': PROBE.parent/'videoaudio/Assets/Video/tone.webm'}


class Clips(SimpleHTTPRequestHandler):
    """Serves the clips by name (whole files: no Range requests); anything else is 404."""

    def translate_path(self, path):
        name = path.split('?')[0].lstrip('/')
        return str(SERVED.get(name, PROBE/'no-such-file'))

    def log_message(self, *args):
        pass


def serve():
    server = ThreadingHTTPServer(('127.0.0.1', 0), Clips)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    os.environ['VIDEO_URL_BASE'] = f'http://127.0.0.1:{server.server_address[1]}/'
    return server


if __name__ == '__main__':
    serve()
    sys.exit(value_probe.main(PROBE, 'ProbeRunner.Record', 'VIDEO_URL_OUT', tolerance=0,
                              engine_args=['--frames', '20000', '--size', '320x180'], batch=True, record_timeout=300,
                              base=(PROBE.parent/'video', BASE)))
