"""Records probes with the macOS Editor (arm64), where its one graphics device is Metal.

`tests/editor_runner.py` supervises Linux Editors (displays, process scopes, /proc); on macOS
the Editor runs directly in batchmode, which draws with Metal on the GPU. A capture directory gets
the Editor's log (`editor.log`), its output (`editor.out`) and exit code (`editor-exit`).

    capture(project, out, args, env)       one batchmode Editor run (windowed=True: with its windows)
    publish(directories, target, name)     two or more captures that exited 0, are complete and
                                           byte-identical in `name` become `target`
    agreeing(directories, complete, names) the checks of publish over several files: the first
                                           capture's directory, to publish from
"""
import os
import subprocess
from pathlib import Path

from editor_runner import jsonl_complete
from probe_paths import editor_path

EXIT = 'editor-exit'


def editor():
    """The Hub's Editor of the probe's release ($UNBOUND_PROBE_UNITY overrides)."""
    return editor_path()


def capture(project, out, args, env, timeout=900, windowed=False, log=None, editor_binary=None,
            nographics=False, cwd=None):
    """Runs the Editor (`editor_binary`, default the release's) on `project` with `args` (in
    batchmode; `windowed`: with its windows, so Play mode has a Game view; `nographics`: without
    a graphics device), logging to `log` (default out/editor.log), from `cwd`; returns its exit
    code."""
    if os.environ.get('UNBOUND_SUITE_NO_EDITOR'):
        raise SystemExit('macos_editor: refusing to start the Unity Editor in a suite run (UNBOUND_SUITE_NO_EDITOR)')
    out = Path(out)
    out.mkdir(parents=True, exist_ok=True)
    if (out/EXIT).exists():
        raise SystemExit(f'refusing to overwrite an Editor result: {out/EXIT}')
    mode = ([] if windowed else ['-batchmode'])+(['-nographics'] if nographics else [])
    command = ['timeout', '-s', 'KILL', str(timeout), str(editor_binary or editor()), *mode, '-projectPath',
               str(project), *args, '-logFile', str(log or out/'editor.log')]
    with (out/'editor.out').open('w') as stream:
        code = subprocess.run(command, env={**os.environ, **env}, stdout=stream, stderr=subprocess.STDOUT,
                              cwd=cwd).returncode
    (out/EXIT).write_text(f'{code}\n')
    return code


def agreeing(directories, complete, names):
    """The first of two or more captures when every one exited 0, `complete` (a JSONL file whose
    last row is complete) is so in each, and each has the same `names(directory)` files,
    byte-identical; SystemExit otherwise."""
    directories = [Path(d) for d in directories]
    if len(directories) < 2:
        raise SystemExit('publishing needs two independent repeats')
    for directory in directories:
        exit_file = directory/EXIT
        code = exit_file.read_text().strip() if exit_file.exists() else 'missing'
        problem = jsonl_complete(complete)(directory)
        if code != '0' or problem:
            raise SystemExit(f'{directory.name}: Editor exit {code}, {problem or "complete"}; not publishing')
    first = names(directories[0])
    if any(names(d) != first or any((d/n).read_bytes() != (directories[0]/n).read_bytes() for n in first)
           for d in directories[1:]):
        raise SystemExit('the repeats differ; not publishing')
    return directories[0]


def publish(directories, target, name):
    """Writes the first capture's `name` to `target` when every capture exited 0 and is complete,
    all are byte-identical, and `target` is new."""
    first = agreeing(directories, name, lambda directory: [name])
    target = Path(target)
    if target.exists():
        raise SystemExit(f'refusing to overwrite reference: {target}')
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes((first/name).read_bytes())
    return 0
