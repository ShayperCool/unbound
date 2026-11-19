#!/usr/bin/env python3
"""Compile setup of real projects: duplicate plugins, test, Editor-only and package assemblies,
a package naming a moved type, CodeDom and a project without InputManager.asset; see README.md."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

# The Editor updates a package's scripts in place only when the compiler names them by their
# project-relative path (Library/PackageCache/...): it runs in the project's resolved directory,
# so a project path through a symbolic link (the run cache may be one) would make them absolute.
_run_root = value_probe.run_root
value_probe.run_root = lambda probe, label: _run_root(probe, label).resolve()

if __name__ == '__main__':
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'COMPILE_SETUP_OUT',
                              tolerance=0, engine_args=['--headless', '600']))
