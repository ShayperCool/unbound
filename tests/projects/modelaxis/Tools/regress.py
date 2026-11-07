#!/usr/bin/env python3
"""Engine-only regression checks for the existing model-import and mesh contracts."""
import argparse
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[4]
PROBES = ROOT/'tests/projects'
sys.path.insert(0,str(ROOT/'tests'))
from probe_paths import run_root, reference_file


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--release',required=True)
    parser.add_argument('--label',required=True)
    args = parser.parse_args()
    env = dict(os.environ,UNBOUND_PROBE_UNITY_VERSION=args.release)
    def run(command):
        result = subprocess.run([sys.executable,*map(str,command)],cwd=ROOT,env=env)
        if result.returncode: raise SystemExit(result.returncode)
    for script in ('material_slots.py','mtl_properties.py'):
        run([PROBES/'modelimport'/script,'unbound','--release',args.release,'--label',args.label])
    run([PROBES/'lightmapuvs/run.py','unbound','--release',args.release,'--label',args.label])
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    reference = reference_file(PROBES/'lightmapuvs','jsonl')
    candidate = run_root(PROBES/'lightmapuvs',args.label)/'unbound/lightmapuvs.jsonl'
    run([PROBES/'lightmapuvs/compare.py',reference,candidate,'--uv2','--expect',
         'Cube,Multi,Pieces,Sphere,Ico,Barrel,Rock,Compound,Twins,Grid,TwoUV,Cylinder'])
    run([PROBES/'lightmapuvs/compare_normals.py',reference,candidate,'--expect',
         'Authored,Bare,Bridge,BridgeObj,Cross,Cube,Cylinder,Edges,Faces,FlatPoly,FlatQuad,FlatTri,Grid,Groups,Ico,Kite,Multi,Organic,Pieces,Pinch,Polys,Prism,Seam,Slit,SlitGroups,Sphere,Star,StarObj,Tent,Torus,Triangle,TwoUV,WeldDiag,WeldQuad,WeldTri'])
    return 0

if __name__ == '__main__': raise SystemExit(main())
