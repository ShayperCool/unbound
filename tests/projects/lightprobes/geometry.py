#!/usr/bin/env python3
"""Record or replay diagnostic tetrahedralization layouts, retaining the complete index order.

The strict comparison is separate from the existing baked-lighting gate while mesh ordering is
open. It uses compare.py without changing its verdict or tolerances.
"""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import editor_runner
from probe_paths import reference_root


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('editor', 'unbound', 'publish'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--work', type=Path, required=True)
    args = parser.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = args.work.resolve()
    if args.mode == 'publish':
        outputs = [work / name / 'geometry.jsonl' for name in ('native1', 'native2')]
        staged = work / 'published-geometry.jsonl'
        editor_runner.publish([p.parent for p in outputs], staged, lambda directory: ['geometry.jsonl'],
                              lambda target, first: shutil.copyfile(first / 'geometry.jsonl', target),
                              complete=[editor_runner.jsonl_complete('geometry.jsonl', {'key': 'complete', 'value': 'true'})], gpu=True)
        target = reference_root(PROBE) / 'geometry.jsonl'
        if target.exists():
            new_lines = staged.read_text().splitlines()
            for line in target.read_text().splitlines():
                if new_lines.count(line) != 1:
                    raise SystemExit('an existing reference row changed; not publishing')
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(staged, target)
        return 0
    names = ('native1', 'native2') if args.mode == 'editor' else ('unbound',)
    for name in names:
        project = work / 'projects' / name
        if project.exists():
            raise SystemExit(f'refusing to overwrite {project}')
        for part in ('Assets', 'Packages', 'ProjectSettings'):
            shutil.copytree(PROBE / part, project / part)
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: ' + args.release + '\n')
        for path in (project / 'Assets/Scripts').glob('LightProbesProbe.cs*'):
            path.unlink()
        shutil.copyfile(PROBE / 'Geometry/ProbeGeometry.cs', project / 'Assets/Scripts/ProbeGeometry.cs')
        out = work / name
        out.mkdir(parents=True, exist_ok=True)
        env = {'LIGHT_PROBES_GEOMETRY_OUT': str(out / 'geometry.jsonl')}
        if args.mode == 'editor':
            job = editor_runner.EditorJob(project, out / 'editor.log', out,
                args=('-executeMethod', 'ProbeGeometry.Record', '-buildTarget', 'Standalone'),
                batch=True, graphics=True, private_display=False,
                env={**editor_runner.vulkan_display_env(), **env}, timeout=300,
                progress_files=[out / 'editor.log'], progress_timeout=180)
            result = editor_runner.run(job)
            if result['exit_code'] != 0:
                return 1
        else:
            cli = os.environ['UNBOUND_CLI']
            command = [str(Path.home() / '.cache/compat/with-heavy-job'), 'timeout', '180', cli,
                       'run', '-p', str(project), '--target', 'Standalone', '--no-watch', '--frames', '120']
            with (out / 'engine.log').open('w') as log:
                result = subprocess.run(command, env={**os.environ, **env}, stdout=log, stderr=subprocess.STDOUT)
            if result.returncode:
                return result.returncode
            problem = editor_runner.jsonl_complete('geometry.jsonl', {'key': 'complete', 'value': 'true'})(out)
            if problem:
                raise SystemExit(problem)
    return 0


if __name__ == '__main__':
    sys.exit(main())
