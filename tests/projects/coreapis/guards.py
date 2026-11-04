#!/usr/bin/env python3
"""Validity and completeness guards for a core API probe capture.

    guards.py values VALUES.jsonl                     the record's schema/completeness problems
    guards.py capture CAPTURE_DIR --project P --release R   every problem of one Native capture
    guards.py source                                  the guard tables against the probe source

The guards check that a capture is a complete, uncontaminated record of the whole probe: the
exact schema of both line kinds (event and OnDemandRendering window line) in the probe's own
writer format, every scheduled scenario reached at its frame, the full 17-frame window with its
fixed members (the cameras, the cube, the canvas and the probe's Update/LateUpdate/OnGUI/
WaitForEndOfFrame rows), one completion line (`finish`) that is the last line, expected error data
(the `... threw T: message` rows of the probe's Try blocks) apart from unexpected exceptions (a
step that threw, an error/exception/assert log), no absolute path or device name in a compared
line, and instance ids only in the probe's explicit Id()/JsonIds() forms.

They never check an answer: the order of callbacks, which window frames render, counter values,
API results and exception texts are what the capture measures, and stay raw in the record.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import sys

PROBE = Path(__file__).resolve().parent
OUTPUT_VARIABLE = 'CORE_APIS_OUT'
SOURCE = PROBE/'Assets/Scripts/CoreProbe.cs'
WATCH_SOURCE = PROBE/'Assets/Scripts/RenderWatch.cs'

EVENT_KEYS = ('f', 's', 'who', 'e')
WINDOW_KEYS = ('w', 'm6', 'i', 'rendered', 'will', 'who', 'e')
# CoreProbe.Schedule: the frame of every step and the scenario it sets.
SCHEDULED = {1: 'active.batch-off', 2: 'active.batch-on', 3: 'active.duplicates', 4: 'active.invalid',
             5: 'active.hierarchy', 6: 'active.reentrant', 7: 'active.nativearray', 9: 'instantiate.scene',
             10: 'instantiate.prefab', 11: 'instantiate.inactive', 13: 'instantiate.errors',
             15: 'ondemand.settings'}
# Set in Start (frame 0) and by the window.
START_SCENARIOS = ('pose', 'lazy')
WINDOW_SCENARIO = 'ondemand'
# The initial scenario: records made before Start (an asset's load, for example).
INITIAL_SCENARIO = '-'
WINDOW_FRAMES = 16
WINDOW = tuple(range(WINDOW_FRAMES+1))
CAMERAS = ('ScreenCam', 'RtCam')
WATCHED = CAMERAS+('Cube',)
WINDOW_WHO = WATCHED+('Canvas', 'probe')
WATCH_EVENTS = ('OnPreCull', 'OnPreRender', 'OnPostRender', 'OnRenderObject', 'OnWillRenderObject')
WITH_CAMERA = ('OnRenderObject', 'OnWillRenderObject')
CAMERA_EVENTS = ('Camera.onPreCull', 'Camera.onPreRender', 'Camera.onPostRender')
PROBE_EVENTS = ('Update', 'LateUpdate', 'OnGUI Layout', 'OnGUI Repaint', 'WaitForEndOfFrame',
                'interval 2 at end of frame')
# Rows that run on every frame by the player loop's definition (once each per window frame).
EVERY_FRAME = ('Update', 'LateUpdate')
# Members that must occur at least once. OnGUI and WaitForEndOfFrame only run in a rendering
# windowed Editor: their presence is the evidence that the window was measured at all.
REQUIRED = {'probe': ('Update', 'LateUpdate', 'OnGUI Layout', 'OnGUI Repaint', 'WaitForEndOfFrame'),
            'ScreenCam': CAMERA_EVENTS+('OnPreCull', 'OnPreRender', 'OnPostRender', 'OnRenderObject'),
            'RtCam': CAMERA_EVENTS+('OnPreCull', 'OnPreRender', 'OnPostRender', 'OnRenderObject'),
            'Cube': ('OnWillRenderObject', 'OnRenderObject'),
            'Canvas': ('willRenderCanvases',)}
ALLOWED = {'probe': PROBE_EVENTS, 'ScreenCam': CAMERA_EVENTS+WATCH_EVENTS, 'RtCam': CAMERA_EVENTS+WATCH_EVENTS,
           'Cube': WATCH_EVENTS, 'Canvas': ('willRenderCanvases',)}
FINISH = ('ondemand', 'probe', 'finish')
LOG_FAULTS = ('Error', 'Assert', 'Exception')
LOG_TYPES = LOG_FAULTS+('Warning', 'Log')
# Machine data that must never reach a compared line.
CONTAMINATION = re.compile(r'(/home/|/mnt/|/tmp/|/usr/|/proc/|[A-Za-z]:\\|Library/|Temp/|NVIDIA|GeForce|'
                           r'Mesa|llvmpipe|OpenGL|Vulkan|timeSinceStartup|realtimeSinceStartup)')
# Explicit instance-id contexts and their allowed values: Id() gives 0, #name, #unknown+ or
# #unknown-; the batch-instantiate arrays keep the 99 the probe wrote into them.
ID_VALUE = r'(?:0|#[^ ,\]\[]+)'
ID_CONTEXTS = (
    re.compile(r'(?<![A-Za-z])id=(?!' + ID_VALUE + r'(?:[ ,\]]|$)|99(?:[ ,\]]|$))(\S*)'),
    re.compile(r'(?<![A-Za-z])transform=(?!' + ID_VALUE + r'(?:[ ,\]]|$)|99(?:[ ,\]]|$))(\S*)'),
    re.compile(r'(?<![A-Za-z])source=(?!' + ID_VALUE + r'(?:[ ,\]]|$))(\S*)'),
    re.compile(r'"(?:m_)?[iI]nstanceID":(?!"' + ID_VALUE + r'")([^,}]*)'),
)
SET_ACTIVE = re.compile(r'^SetGameObjectsActive \[([^\]]*)\] [01]$')
IDS_AFTER = re.compile(r'^ids after (.*)$')
EXPECTED_ERROR = re.compile(r' threw (System\.[A-Za-z0-9_.]+)(?::|$)')
# Editor log lines that mean a compile failure or an unfinished probe.
EDITOR_LOG_FATAL = (re.compile(r'error CS\d+'), re.compile(r'Scripts have compiler errors'),
                    re.compile(r'Play mode did not finish'))
# Files the Editor itself writes into the project copy's authored folders: none in Assets or
# Packages; in ProjectSettings only this one, created on first open (both Editor copies of the
# previous Native pair created it with the same contents and nothing else).
EDITOR_WRITTEN = ('settings/PackageManagerSettings.asset',)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def tree(root):
    root = Path(root)
    return {str(p.relative_to(root)): sha(p) for p in sorted(root.rglob('*')) if p.is_file()}


def is_int(v):
    return isinstance(v, int) and not isinstance(v, bool)


def esc(text):
    """The probe's P.Esc (ProbeFormat.cs)."""
    out = []
    for c in text:
        if c == '"':
            out.append('\\"')
        elif c == '\\':
            out.append('\\\\')
        elif c == '\n':
            out.append('\\n')
        elif c == '\r':
            continue
        elif c == '\t':
            out.append('\\t')
        elif c < ' ':
            out.append('\\u%04x' % ord(c))
        else:
            out.append(c)
    return ''.join(out)


def written(row):
    """The line the probe's writer makes for `row` (CoreProbe.Record / RenderEvent)."""
    if tuple(row) == EVENT_KEYS:
        return f'{{"f":{row["f"]},"s":"{row["s"]}","who":"{esc(row["who"])}","e":"{esc(row["e"])}"}}'
    return (f'{{"w":{row["w"]},"m6":{row["m6"]},"i":{row["i"]},"rendered":{row["rendered"]},'
            f'"will":{row["will"]},"who":"{esc(row["who"])}","e":"{esc(row["e"])}"}}')


def parse(path):
    """(rows, problems) of a values file: complete UTF-8 JSON lines, each in the writer's format."""
    problems = []
    try:
        data = Path(path).read_bytes()
    except OSError as error:
        return [], [f'no record: {error}']
    try:
        text = data.decode('utf-8')
    except UnicodeDecodeError as error:
        return [], [f'the record is not UTF-8: {error}']
    if not text:
        return [], ['the record is empty']
    if not text.endswith('\n'):
        problems.append('the record does not end with a newline (not closed)')
    if '\r' in text:
        problems.append('the record contains a carriage return')
    rows = []
    for number, line in enumerate(text.split('\n')[:-1] if text.endswith('\n') else text.split('\n'), 1):
        if not line:
            problems.append(f'line {number}: empty')
            continue
        try:
            row = json.loads(line)
        except json.JSONDecodeError as error:
            problems.append(f'line {number}: not JSON ({error})')
            continue
        if not isinstance(row, dict):
            problems.append(f'line {number}: not an object')
            continue
        keys = tuple(row)
        if keys == EVENT_KEYS:
            bad = [k for k in ('f',) if not is_int(row[k])]+[k for k in ('s', 'who', 'e') if not isinstance(row[k], str)]
        elif keys == WINDOW_KEYS:
            bad = [k for k in ('w', 'm6', 'i', 'rendered', 'will') if not is_int(row[k])] + \
                  [k for k in ('who', 'e') if not isinstance(row[k], str)]
        else:
            problems.append(f'line {number}: keys {list(keys)} are neither {list(EVENT_KEYS)} nor {list(WINDOW_KEYS)}')
            continue
        if bad:
            problems.append(f'line {number}: wrong types for {bad}')
            continue
        if written(row) != line:
            problems.append(f"line {number}: not in the probe writer's format")
        row['_line'] = number
        rows.append(row)
    return rows, problems


def window_problems(rows):
    problems = []
    frames = sorted({r['w'] for r in rows})
    if frames != list(WINDOW):
        problems.append(f'window frames {frames} are not {WINDOW[0]}..{WINDOW[-1]}')
    for r in rows:
        n = r['_line']
        if r['w'] not in WINDOW:
            continue
        if not 0 <= r['m6'] < 6 or r['m6'] != (1+r['w']) % 6:
            # The window starts on a frame with frameCount % 6 == 1 (CoreProbe.Update).
            problems.append(f'line {n}: m6={r["m6"]} at w={r["w"]} (window must start at frameCount % 6 == 1)')
        if r['i'] not in (1, 2, 3):
            problems.append(f'line {n}: interval {r["i"]} was never set in the window')
        if r['will'] not in (0, 1):
            problems.append(f'line {n}: will={r["will"]}')
        if r['rendered'] < 0:
            problems.append(f'line {n}: rendered={r["rendered"]}')
        who, e = r['who'], r['e']
        if who not in WINDOW_WHO:
            problems.append(f'line {n}: unexpected window member {who!r}')
            continue
        name, _, camera = e.partition(' cam=')
        if name not in ALLOWED[who]:
            problems.append(f'line {n}: {who} has no {e!r} handler')
        if (name in WITH_CAMERA) != bool(_):
            problems.append(f'line {n}: {e!r} camera field')
        elif name in WITH_CAMERA and camera not in CAMERAS:
            problems.append(f'line {n}: {e!r} renders with a camera that is not the probe\'s')
    for w in WINDOW:
        for name in EVERY_FRAME:
            count = sum(1 for r in rows if r['w'] == w and r['who'] == 'probe' and r['e'] == name)
            if count != 1:
                problems.append(f'w={w}: {count} probe {name} rows, not 1')
    for who, names in REQUIRED.items():
        seen = {r['e'].partition(' cam=')[0] for r in rows if r['who'] == who}
        missing = [name for name in names if name not in seen]
        if missing:
            problems.append(f'window member {who} never records {missing}')
    late = sum(1 for r in rows if r['who'] == 'probe' and r['e'] == 'interval 2 at end of frame')
    if late > 1:
        problems.append(f'{late} end-of-frame interval changes, at most 1')
    return problems


def id_problems(row):
    e, problems = row['e'], []
    for pattern in ID_CONTEXTS:
        for m in pattern.finditer(e):
            problems.append(f'line {row["_line"]}: raw instance id {m.group(0)!r}')
    lists = [SET_ACTIVE.match(e), IDS_AFTER.match(e) if row['who'] == 'probe' else None]
    for m, extra in zip(lists, ((), ('99',))):
        if m:
            for value in m.group(1).split(','):
                if value not in extra and not re.fullmatch(ID_VALUE, value):
                    problems.append(f'line {row["_line"]}: raw instance id {value!r} in an id list')
    return problems


def values_problems(path):
    """Every validity/completeness problem of a values file, and the listed (non-blocking) notes."""
    rows, problems = parse(path)
    notes = []
    if not rows:
        return problems or ['no rows'], notes
    events = [r for r in rows if 'f' in r]
    window = [r for r in rows if 'w' in r]
    last = rows[-1]
    finishes = [r for r in events if (r['s'], r['who'], r['e']) == FINISH]
    if len(finishes) != 1:
        problems.append(f'{len(finishes)} completion lines, not exactly one')
    if 'f' not in last or (last['s'], last['who'], last['e']) != FINISH:
        problems.append('the last line is not the completion line')
    scenarios = {r['s'] for r in events}
    known = set(SCHEDULED.values()) | set(START_SCENARIOS) | {WINDOW_SCENARIO, INITIAL_SCENARIO}
    for s in sorted(scenarios-known):
        problems.append(f'unknown scenario {s!r}')
    for s in START_SCENARIOS:
        if not any(r['s'] == s and r['f'] == 0 for r in events):
            problems.append(f'scenario {s} has no row at frame 0')
    for frame, s in SCHEDULED.items():
        if not any(r['s'] == s and r['f'] == frame for r in events):
            problems.append(f'step {s} has no row at its frame {frame}')
    for r in events:
        n, e = r['_line'], r['e']
        if r['f'] < -1:
            problems.append(f'line {n}: frame {r["f"]}')
        if r['who'] == 'probe' and e.startswith('step threw '):
            problems.append(f'line {n}: unexpected exception: {e}')
        if r['who'] == 'log':
            kind = e.split(':', 1)[0]
            if kind not in LOG_TYPES or kind in LOG_FAULTS:
                problems.append(f'line {n}: unexpected log {e!r}')
            else:
                notes.append(f'line {n}: log {e!r}')
        if EXPECTED_ERROR.search(e):
            notes.append(f'line {n}: expected error data')
    for r in rows:
        for field in ('who', 'e') + (('s',) if 's' in r else ()):
            m = CONTAMINATION.search(r[field])
            if m:
                problems.append(f'line {r["_line"]}: machine data {m.group(0)!r} in {field}')
        problems += id_problems(r)
    problems += window_problems(window)
    return problems, notes


# ---------------------------------------------------------------- the guard tables against the source

def source_problems(source=SOURCE, watch=WATCH_SOURCE):
    """Whether the tables above still describe the probe source (scenarios, window, handlers)."""
    text, problems = Path(source).read_text(), []
    steps = {int(f): s for f, s in re.findall(r'At\((\d+), "([^"]+)"', text)}
    if steps != SCHEDULED:
        problems.append(f'CoreProbe.Schedule has {steps}')
    starts = re.findall(r'^ +s_Scenario = "([^"]+)";', text, re.M)
    if starts != list(START_SCENARIOS)+[WINDOW_SCENARIO]:
        problems.append(f'CoreProbe sets scenarios {starts}')
    if f'const int WindowFrames = {WINDOW_FRAMES};' not in text:
        problems.append('the window length differs')
    if 'Time.frameCount % 6 == 1' not in text:
        problems.append('the window start rule differs')
    if f'static string s_Scenario = "{INITIAL_SCENARIO}";' not in text:
        problems.append('the initial scenario differs')
    probe_events = re.findall(r'RenderEvent\("probe", "([^"]+)"\)', text)
    if sorted(set(probe_events)) != sorted(PROBE_EVENTS):
        problems.append(f'probe window rows {sorted(set(probe_events))}')
    camera = re.findall(r'RenderEvent\(c\.name, "([^"]+)"\)', text)
    if sorted(camera) != sorted(CAMERA_EVENTS):
        problems.append(f'camera callbacks {camera}')
    if 'RenderEvent("Canvas", "willRenderCanvases")' not in text:
        problems.append('the canvas row differs')
    if 'c.name == "ScreenCam" || c.name == "RtCam"' not in text:
        problems.append('the probe cameras differ')
    if 'Record("probe", "finish");' not in text or 'File.WriteAllText(output, s_Log.ToString());' not in text:
        problems.append('the completion line or the single final write differs')
    handlers = re.findall(r'void (On\w+)\(\) => Ev\("(\w+)', Path(watch).read_text())
    if sorted(h for h, _ in handlers) != sorted(WATCH_EVENTS) or any(h != e for h, e in handlers):
        problems.append(f'RenderWatch handlers {handlers}')
    if 'Environment.GetEnvironmentVariable("CORE_APIS_OUT")' not in text:
        problems.append('the output variable differs')
    return problems


# ---------------------------------------------------------------- one Native capture

def log_problems(path, release):
    try:
        text = Path(path).read_text(errors='replace')
    except OSError as error:
        return [f'no Editor log: {error}']
    lines = text.splitlines()
    problems = []

    def first(prefix):
        return next((line for line in lines if line.startswith(prefix)), None)
    version = first('Unity Editor version:')
    if version is None or version.split(':', 1)[1].split()[0] != release:
        problems.append(f'Editor version line {version!r} is not {release}')
    if (first('Batch mode:') or '').split(':', 1)[-1].strip() != 'NO':
        problems.append('the Editor was not windowed (Batch mode: NO)')
    if 'NVIDIA' not in (first('Renderer:') or ''):
        problems.append(f'renderer {first("Renderer:")!r} is not the NVIDIA GPU')
    if 'NVIDIA' not in (first('Vendor:') or ''):
        problems.append(f'vendor {first("Vendor:")!r} is not NVIDIA')
    if 'NVIDIA' not in (first('Version:') or ''):
        problems.append(f'GL version {first("Version:")!r} is not the NVIDIA driver')
    if (first('GLES:') or '').split(':', 1)[-1].strip() != '0':
        problems.append('the device is not desktop OpenGL (GLES: 0)')
    if not any('OpenGL Core' in line for line in lines):
        problems.append('the device is not OpenGL Core')
    if not any('Cleanup mono' in line for line in lines):
        problems.append('the Editor log has no shutdown line')
    for pattern in EDITOR_LOG_FATAL:
        hits = [line for line in lines if pattern.search(line)]
        if hits:
            problems.append(f'Editor log: {hits[0][:160]!r} ({len(hits)} lines)')
    return problems


def editor_result_problems(directory, project, hard_timeout, progress_timeout):
    """A strict exit-0 windowed NVIDIA GPU Editor run without any forced stop."""
    directory = Path(directory)
    try:
        result = json.loads((directory/'editor-run.json').read_text())
        spec = json.loads((directory/'editor-spec.json').read_text())
    except (OSError, json.JSONDecodeError) as error:
        return [f'no Editor result/spec: {error}']
    problems = []
    if result.get('forced_exit'):
        problems.append(f"the Editor was stopped ({result['forced_exit']}: {result.get('forced_detail')})")
    if result.get('exit_code') != 0 or result.get('signal'):
        problems.append(f"the Editor exited with {result.get('exit_code')} (signal {result.get('signal')})")
    if (result.get('launcher') or {}).get('exit') != 0:
        problems.append(f"the launcher exited with {(result.get('launcher') or {}).get('exit')}")
    if result.get('kind') != 'editor-gpu' or not result.get('require_gpu') or not result.get('display'):
        problems.append('not a GPU Editor on a private display')
    if not result.get('renderer_ok') or 'NVIDIA' not in (result.get('renderer') or ''):
        problems.append(f"renderer {result.get('renderer')!r} is not the NVIDIA GPU")
    if not result.get('shutdown_logged'):
        problems.append('the Editor did not log its shutdown')
    if result.get('followed'):
        problems.append(f"the project was handed to another Editor process: {result['followed']}")
    command = result.get('command') or []
    if command[:3] != ['vglrun', '-d', 'egl']:
        problems.append('the Editor did not start through vglrun -d egl')
    for flag in ('-batchmode', '-nographics', '-quit', '-executeMethod'):
        if flag in command:
            problems.append(f'the Editor command has {flag}')
    if '-projectPath' not in command or Path(command[command.index('-projectPath')+1]).resolve() != Path(project).resolve():
        problems.append('the Editor did not open this capture\'s project copy')
    if spec.get('timeout') != hard_timeout or spec.get('progress_timeout') != progress_timeout:
        problems.append(f"deadlines {spec.get('timeout')}/{spec.get('progress_timeout')} s, not {hard_timeout}/{progress_timeout}")
    if spec.get('progress_files') != [str((directory/'values.jsonl').resolve())]:
        problems.append(f"progress files {spec.get('progress_files')}")
    exit_s = (result.get('times') or {}).get('exit_s')
    if exit_s is None or exit_s > hard_timeout:
        problems.append(f'Editor time {exit_s} s')
    values = directory/'values.jsonl'
    admitted = (result.get('times') or {}).get('admitted')
    if values.exists() and admitted is not None and exit_s is not None and \
            values.stat().st_mtime > admitted+exit_s+1:
        problems.append('values.jsonl changed after the Editor exited')
    return problems


def prelaunch_problems(directory, release):
    """The receipt editor_runner writes inside the lease must name this release and the bytes the
    runner prepared (inputs.json)."""
    directory = Path(directory)
    try:
        receipt = json.loads((directory/'prelaunch-inputs.json').read_text())
        prepared = json.loads((directory/'inputs.json').read_text())
    except (OSError, json.JSONDecodeError) as error:
        return [f'no launch receipt: {error}']
    problems = []
    if receipt.get('selected_release') != release:
        problems.append(f"the launch selected {receipt.get('selected_release')}")
    launched = {k: v['sha256'] for k, v in receipt.get('files', {}).items()
                if k.split('/', 1)[0] in ('assets', 'packages', 'settings')}
    if launched != prepared.get('files'):
        problems.append('the launched project bytes differ from the prepared copy')
    return problems


def authored_delta(directory, project):
    prepared = json.loads((Path(directory)/'inputs.json').read_text())['files']
    now = {}
    for label, part in (('assets', 'Assets'), ('packages', 'Packages'), ('settings', 'ProjectSettings')):
        now.update({label+'/'+k: v for k, v in tree(Path(project)/part).items()})
    return {'changed': sorted(k for k in prepared if k in now and now[k] != prepared[k]),
            'removed': sorted(k for k in prepared if k not in now),
            'added': sorted(k for k in now if k not in prepared)}


def authored_problems(directory, project):
    try:
        delta = authored_delta(directory, project)
    except (OSError, KeyError, json.JSONDecodeError) as error:
        return [f'no prepared input list: {error}']
    problems = [f'the Editor changed {k}' for k in delta['changed']]
    problems += [f'the Editor removed {k}' for k in delta['removed']]
    problems += [f'the Editor added {k}' for k in delta['added'] if k not in EDITOR_WRITTEN]
    return problems


def capture_problems(directory, project, release, hard_timeout, progress_timeout):
    directory = Path(directory)
    problems = editor_result_problems(directory, project, hard_timeout, progress_timeout)
    problems += prelaunch_problems(directory, release)
    problems += log_problems(directory/'editor.log', release)
    if (directory/'values.jsonl.watchdog').exists():
        problems.append('the probe runner\'s Play mode watchdog fired')
    problems += authored_problems(directory, project)
    problems += values_problems(directory/'values.jsonl')[0]
    return problems


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest='command', required=True)
    v = sub.add_parser('values')
    v.add_argument('path', type=Path)
    c = sub.add_parser('capture')
    c.add_argument('directory', type=Path)
    c.add_argument('--project', type=Path, required=True)
    c.add_argument('--release', required=True)
    c.add_argument('--timeout', type=int, default=1200)
    c.add_argument('--progress-timeout', type=int, default=300)
    sub.add_parser('source')
    args = parser.parse_args()
    if args.command == 'values':
        problems, notes = values_problems(args.path)
    elif args.command == 'capture':
        problems, notes = capture_problems(args.directory, args.project, args.release, args.timeout,
                                           args.progress_timeout), []
    else:
        problems, notes = source_problems(), []
    for note in notes:
        print('note:', note)
    for problem in problems:
        print(problem)
    print('passes' if not problems else f'{len(problems)} problems')
    return int(bool(problems))


if __name__ == '__main__':
    sys.exit(main())
