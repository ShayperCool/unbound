"""Losslessly compress full captures, keeping every frame and checking the round trip."""
import gzip
from pathlib import Path

def pack(root):
    for file in sorted(root.glob('*.f32')):
        content=file.read_bytes()
        packed=file.with_suffix('.f32.gz')
        with packed.open('xb') as out:
            out.write(gzip.compress(content,mtime=0))
        assert gzip.decompress(packed.read_bytes())==content,file
        file.unlink()
