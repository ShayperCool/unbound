#!/usr/bin/env python3
"""The built-in pipeline mixing baked shadow masks with realtime shadows; see README.md.

    run.py setup --label L      makes and bakes the scene in the Editor, copied back into the probe
    run.py record|publish|gate  tests/value_probe.py
"""
from pathlib import Path
import shutil
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
from probe_paths import cache_artifact_path, project_copy, run_root  # noqa: E402
import editor_runner  # noqa: E402
import value_probe  # noqa: E402


def setup(label):
    """ShadowmaskSetup.Create in a copy of the probe, then its scene, bake and build settings back."""
    out = cache_artifact_path(run_root(PROBE, label))
    project = out/'project'
    project_copy(PROBE, project, fresh=True, package_cache=False)
    shutil.rmtree(project/'Assets/Scenes', ignore_errors=True)
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'ShadowmaskSetup.Create'],
                                  batch=True, graphics=True, timeout=3600, label='shadowmaskmix-'+label)
    result = editor_runner.run(job)
    print(editor_runner.summary(result))
    if result.get('exit_code') != 0:
        return 1
    shutil.rmtree(PROBE/'Assets/Scenes', ignore_errors=True)
    shutil.copytree(project/'Assets/Scenes', PROBE/'Assets/Scenes')
    shutil.copy2(project/'Assets/Scenes.meta', PROBE/'Assets/Scenes.meta')
    shutil.copy2(project/'ProjectSettings/EditorBuildSettings.asset', PROBE/'ProjectSettings/EditorBuildSettings.asset')
    return 0


if __name__ == '__main__':
    if len(sys.argv) > 1 and sys.argv[1] == 'setup':
        label = sys.argv[sys.argv.index('--label') + 1] if '--label' in sys.argv else 'setup'
        sys.exit(setup(label))
    sys.exit(value_probe.main(PROBE, 'ProbeRunner.Record', 'SHADOWMASK_OUT', tolerance=0,
                              engine_args=['--frames', '400', '--size', '320x180']))
