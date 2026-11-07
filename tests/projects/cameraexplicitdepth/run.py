#!/usr/bin/env python3
"""Explicit-target depth probe. Reuse the stacked-camera GPU capture/publish driver.

run.py editor|publish|check|unbound --release R --space Linear|Gamma --label L
run.py gate --release R
The gate runs only Unbound and compares against the committed release reference.
"""
import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import time

PROBE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('stack_driver', PROBE.parent/'camerastack/run.py')
driver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(driver)
driver.PROBE = PROBE


def main():
    if len(sys.argv) > 1 and sys.argv[1] == 'gate':
        import argparse
        p = argparse.ArgumentParser()
        p.add_argument('mode')
        p.add_argument('--release', required=True)
        a = p.parse_args()
        os.environ['UNBOUND_PROBE_UNITY_VERSION'] = a.release
        os.environ.setdefault('UNBOUND_CLI', os.environ.get('UNBOUND', str(driver.REPO/'target/release/unbound')))
        status = 0
        for space in ('Linear', 'Gamma'):
            work = driver.run_root(PROBE, 'gate-'+str(time.time_ns())+'-'+space)
            result = driver.unbound(work, space)
            status |= result != 0
            status |= subprocess.call([sys.executable, str(PROBE.parent/'camerastack/compare.py'),
                                       str(driver.reference_root(PROBE)/space), str(work/'unbound')]) != 0
        return int(status)
    return driver.main()


if __name__ == '__main__':
    sys.exit(main())
