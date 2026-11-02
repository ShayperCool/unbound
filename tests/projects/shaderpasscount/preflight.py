#!/usr/bin/env python3
"""Offline compile preflight of the probe scripts with an Editor-compiled fixture's own compiler inputs.

    preflight.py --rsp-dir DAG --launch LAUNCH_DIR --out NEW_DIR [--release R] [--corrects FAILED/preflight.json]

DAG is an Editor-written `Library/Bee/artifacts/<hash>.dag` directory of a batchmode GPU capture
copy of the shader keyword fixture whose Packages/ and ProjectSettings/ this probe's fixture copies
byte for byte (read only; no setup Editor). LAUNCH_DIR is that capture's output directory (its
prelaunch-inputs.json, inputs.json and editor-run.json). The copy must prove it is that fixture:
  * the launch receipt (prelaunch-inputs.json, written inside the admitted lease before the Editor
    started) lists exactly this probe's Packages/ and ProjectSettings/ files with these bytes, for
    this copy, release and a strict exit-0 NVIDIA batchmode GPU Editor result;
  * the copy's Packages/ are still these bytes; its ProjectSettings/ too, except the settings the
    shader keyword runner changes in Edit mode after the compile (EditorSettings.asset, only
    m_AsyncShaderCompilation 1 -> 0, saved at shutdown), each listed with its exact line change;
  * neither this fixture nor the copy has a compile input under Assets/ besides the two scripts
    (no .asmdef, .asmref, .rsp, managed or native plugin), so their other Assets (shaders, scene)
    do not enter the compile; the copy has exactly one script in Assets/Scripts (Assembly-CSharp) and
    Assets/Editor/ProbeRunner.cs (Assembly-CSharp-Editor), the folders of this probe's two scripts;
  * the Editor compiled its scripts (Library/ScriptAssemblies).

The Assembly-CSharp and Assembly-CSharp-Editor response files are then used byte for byte: the
same references (the module graph these Packages/ProjectSettings select), defines, language
version, analyzers and options. Only paths are rebound: the one source path of each assembly is replaced by this probe's
script in the same folder (the Editor lists the scripts it finds in the assembly's folders), the
compile runs in NEW_DIR/project (relative output paths), and the source generators' additional
file names that directory. The compiler is the Editor's bundled Roslyn on its own runtime, invoked
as the Editor's build graph does (`NetCoreRuntime/dotnet exec DotNetSdkRoslyn/csc.dll /nostdlib
/noconfig @rsp @rsp2`, DOTNET_MULTILEVEL_LOOKUP=0), without `/shared` (no compiler server).

Writes NEW_DIR/preflight.json (rsp bytes/hashes and rebound text, identity proof, source
substitution, expanded arguments, source/reference/analyzer/compiler hashes, compiler output, exit
codes, `passed`) and both compiler logs. Exit 0 only when both assemblies compile without an
error and every reference and analyzer exists. `--corrects` marks the single allowed correction
run after a preflight that failed with compile errors only; it refuses to correct a correction or
a run that failed for another reason (an identity or missing-input failure is a STOP).
"""
import argparse
import difflib
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
SOURCES = {'Assembly-CSharp': ['Assets/Scripts/ShaderPassCountProbe.cs'],
           'Assembly-CSharp-Editor': ['Assets/Editor/ProbeRunner.cs']}
FOLDERS = {'Assembly-CSharp': 'Assets/Scripts/', 'Assembly-CSharp-Editor': 'Assets/Editor/'}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def arguments(rsp):
    """Unity writes one argument per line; quotes delimit a path."""
    return [line for line in rsp.decode('utf-8').splitlines() if line.strip()]


def unquote(value):
    return value[1:-1] if len(value) >= 2 and value[0] == value[-1] == '"' else value


def files(root, predicate=lambda p: True):
    root = Path(root)
    return {str(p.relative_to(root)): sha(p.read_bytes()) for p in sorted(root.rglob('*'))
            if p.is_file() and predicate(p)}


def is_source(argument):
    return not argument.startswith(('-', '/'))


# Files under Assets/ that change what the Editor compiles (assembly definitions and references,
# response files, managed and native plugins).
COMPILE_INPUTS = ('.asmdef', '.asmref', '.rsp', '.dll', '.so', '.dylib', '.bundle', '.a', '.jslib')
# Settings the shader keyword runner changes after the compile; the Editor saves them at shutdown.
RUNTIME_SETTINGS = {'EditorSettings.asset': [('-  m_AsyncShaderCompilation: 1', '+  m_AsyncShaderCompilation: 0')]}


def setting_changes(mine, theirs):
    """The changed lines of a settings file as (removed, added) pairs."""
    diff = [line for line in difflib.unified_diff(mine.splitlines(), theirs.splitlines(), lineterm='', n=0)
            if line[:1] in '+-' and not line.startswith(('+++', '---'))]
    removed = [line for line in diff if line.startswith('-')]
    added = [line for line in diff if line.startswith('+')]
    return list(zip(removed, added)) if len(removed) == len(added) else [('unpaired', '\n'.join(diff))]


def identity(copy, launch, release, written, proof):
    """Why the copy is not an Editor-compiled copy of this fixture's Packages/ProjectSettings
    (empty list: it is)."""
    problems = []
    try:
        receipt = json.loads((launch/'prelaunch-inputs.json').read_text())
        result = json.loads((launch/'editor-run.json').read_text())
    except (OSError, json.JSONDecodeError) as error:
        return [f'no launch receipt in {launch}: {error}']
    proof['launch'] = {'dir': str(launch), 'prelaunch_sha256': sha((launch/'prelaunch-inputs.json').read_bytes()),
                       'editor_run_sha256': sha((launch/'editor-run.json').read_bytes()),
                       'selected_release': receipt.get('selected_release'), 'exit_code': result.get('exit_code'),
                       'forced_exit': result.get('forced_exit'), 'renderer': result.get('renderer'),
                       'kind': result.get('kind'), 'command': result.get('command')}
    if receipt.get('selected_release') != release:
        problems.append(f"the launch selected {receipt.get('selected_release')}, not {release}")
    if result.get('exit_code') != 0 or result.get('forced_exit') or result.get('kind') != 'editor-gpu' \
            or 'NVIDIA' not in (result.get('renderer') or '') or '-batchmode' not in (result.get('command') or []) \
            or Path((result.get('command') or ['', '', '', '', '', '', ''])[6]).resolve() != copy.resolve():
        problems.append('the launch is not a strict exit-0 NVIDIA batchmode GPU Editor run of this copy')
    launched = {}
    for key, entry in receipt.get('files', {}).items():
        label, _, rel = key.partition('/')    # runner/tool inputs have plain names
        if label in ('packages', 'settings') and rel:
            launched[label+'/'+rel] = entry.get('sha256')
            if Path(entry.get('path', '')).resolve() != (copy/{'packages': 'Packages', 'settings': 'ProjectSettings'}[label]/rel).resolve():
                problems.append(f'the receipt names {entry.get("path")} for {key}, not this copy')
    mine = {'packages/'+k: v for k, v in files(PROBE/'Packages').items()}
    mine.update({'settings/'+k: v for k, v in files(PROBE/'ProjectSettings').items()})
    if launched != mine:
        problems.append('the launched Packages/ProjectSettings are not this fixture\'s: '
                        + str(sorted(k for k in set(launched) | set(mine) if launched.get(k) != mine.get(k))[:5]))
    proof['launched_files_equal'] = launched == mine
    changes = {}
    for part, label in (('Packages', 'packages'), ('ProjectSettings', 'settings')):
        theirs = files(copy/part)
        for rel, digest in files(PROBE/part).items():
            if theirs.get(rel) == digest:
                continue
            if label == 'settings' and rel in RUNTIME_SETTINGS and (copy/part/rel).is_file():
                change = setting_changes((PROBE/part/rel).read_text(), (copy/part/rel).read_text())
                changes[part+'/'+rel] = change
                if change != RUNTIME_SETTINGS[rel]:
                    problems.append(f'{part}/{rel} changed beyond the runner\'s setting: {change}')
            else:
                problems.append(f'{part}/{rel} differs from the fixture in the copy')
        written.update({part+'/'+k: v for k, v in theirs.items() if k not in files(PROBE/part)})
    proof['copy_settings_changed_after_compile'] = changes
    for root, name in ((PROBE, 'fixture'), (copy, 'copy')):
        extra = sorted(str(p.relative_to(root)) for p in (root/'Assets').rglob('*')
                       if p.is_file() and p.suffix.lower() in COMPILE_INPUTS)
        if extra:
            problems.append(f'the {name} has compile inputs under Assets: {extra[:5]}')
    scripts = sorted(str(p.relative_to(copy)) for p in (copy/'Assets').rglob('*.cs'))
    proof['copy_scripts'] = scripts
    for assembly, folder in FOLDERS.items():
        theirs = [s for s in scripts if s.startswith(folder)]
        if len(theirs) != 1:
            problems.append(f'the copy has {theirs} in {folder}, not exactly one {assembly} script')
    if [s for s in scripts if not s.startswith(tuple(FOLDERS.values()))]:
        problems.append(f'the copy compiles scripts outside {list(FOLDERS.values())}: {scripts}')
    if 'Assets/Editor/ProbeRunner.cs' not in scripts:
        problems.append('the copy has no Assets/Editor/ProbeRunner.cs')
    mine_scripts = sorted(str(p.relative_to(PROBE)) for p in (PROBE/'Assets').rglob('*.cs'))
    if mine_scripts != sorted(s for v in SOURCES.values() for s in v):
        problems.append(f'this fixture has scripts {mine_scripts}, not the two probe scripts')
    for name in SOURCES:
        if not (copy/'Library/ScriptAssemblies'/(name+'.dll')).is_file():
            problems.append(f'the Editor did not compile {name} in the copy')
    return problems


def rebind_sources(rsp, assembly):
    """The response file with its one source path replaced by this probe's script (same folder)."""
    lines = rsp.decode('utf-8').split('\n')
    sources = [i for i, line in enumerate(lines) if line.strip() and is_source(line.strip())]
    if len(sources) != 1:
        raise ValueError(f'{assembly}.rsp lists {len(sources)} sources, not one')
    original = unquote(lines[sources[0]].strip())
    if not original.startswith(FOLDERS[assembly]):
        raise ValueError(f'{assembly}.rsp compiles {original}, not a script in {FOLDERS[assembly]}')
    lines[sources[0]] = '"'+SOURCES[assembly][0]+'"'
    return '\n'.join(lines).encode('utf-8'), {'from': original, 'to': SOURCES[assembly][0]}


def compile_set(work, rsp_dir, data_root):
    dag_name = rsp_dir.name
    dag = work/'Library/Bee/artifacts'/dag_name
    dag.mkdir(parents=True)
    dotnet = data_root/'NetCoreRuntime/dotnet'
    csc = data_root/'DotNetSdkRoslyn/csc.dll'
    compiler = {'dotnet': str(dotnet), 'dotnet_sha256': sha(dotnet.read_bytes()),
                'csc': str(csc), 'csc_sha256': sha(csc.read_bytes())}
    results = {}
    for assembly in ('Assembly-CSharp', 'Assembly-CSharp-Editor'):
        original = (rsp_dir/f'{assembly}.rsp').read_bytes()
        rsp, substitution = rebind_sources(original, assembly)
        rsp2 = (rsp_dir/f'{assembly}.rsp2').read_bytes()
        additional = rsp_dir/f'{assembly}.UnityAdditionalFile.txt'
        (dag/f'{assembly}.rsp').write_bytes(rsp)
        (dag/f'{assembly}.rsp2').write_bytes(rsp2)
        rebound = str(work.resolve()).encode()
        (dag/additional.name).write_bytes(rebound)
        args = arguments(rsp)+arguments(rsp2)
        sources = [unquote(a) for a in args if is_source(a)]
        references = [unquote(a.split(':', 1)[1]) for a in args if a.startswith('-r:')]
        analyzers = [unquote(a.split(':', 1)[1]) for a in args if a.startswith('-analyzer:')]
        refs = {}
        for ref in references:
            path = Path(ref) if Path(ref).is_absolute() else work/ref
            refs[ref] = sha(path.read_bytes()) if path.is_file() else None
        command = [str(dotnet), 'exec', str(csc), '/nostdlib', '/noconfig',
                   f'@Library/Bee/artifacts/{dag_name}/{assembly}.rsp',
                   f'@Library/Bee/artifacts/{dag_name}/{assembly}.rsp2']
        env = dict(os.environ, DOTNET_MULTILEVEL_LOOKUP='0')
        process = subprocess.run(command, cwd=work, env=env, capture_output=True)
        output = (process.stdout+process.stderr).decode(errors='replace')
        (work.parent/f'{assembly}.log').write_text(output)
        out = work/f'Library/Bee/artifacts/{dag_name}/{assembly}.dll'
        results[assembly] = {
            'rsp': {'source': str(rsp_dir/f'{assembly}.rsp'), 'bytes': len(original), 'sha256': sha(original),
                    'rebound_sha256': sha(rsp), 'rebound_text': rsp.decode('utf-8'), 'substitution': substitution},
            'rsp2': {'bytes': len(rsp2), 'sha256': sha(rsp2), 'rebound_text': rsp2.decode('utf-8')},
            'additional_file': {'original_sha256': sha(additional.read_bytes()),
                                'original': additional.read_bytes().decode(), 'rebound': rebound.decode()},
            'effective_arguments': ['/nostdlib', '/noconfig']+args,
            'langversion': [a for a in args if a.startswith('-langversion')],
            'defines': [a.split(':', 1)[1] for a in args if a.startswith('-define:')],
            'sources': {s: sha((work/s).read_bytes()) if (work/s).is_file() else None for s in sources},
            'references': refs,
            'analyzers': {a: sha(Path(a).read_bytes()) if Path(a).is_file() else None for a in analyzers},
            'command': command, 'cwd': str(work), 'env': {'DOTNET_MULTILEVEL_LOOKUP': '0'},
            'exit': process.returncode, 'output': output,
            'errors': [line for line in output.splitlines() if re.search(r'\berror [A-Z]+\d+', line)],
            'warnings': [line for line in output.splitlines() if re.search(r'\bwarning [A-Z]+\d+', line)],
            'dll_sha256': sha(out.read_bytes()) if out.is_file() else None}
        if process.returncode != 0:
            break    # the Editor assembly references Assembly-CSharp.ref.dll
    return compiler, results


def judge(results):
    """Problems of a compile set (shared with the guard controls)."""
    problems = []
    for assembly, expected in SOURCES.items():
        r = results.get(assembly)
        if r is None:
            problems.append(f'{assembly}: not compiled')
            continue
        if list(r['sources']) != expected:
            problems.append(f'{assembly}: sources {list(r["sources"])} != {expected}')
        missing = [ref for ref, h in r['references'].items() if h is None]
        if missing:
            problems.append(f'{assembly}: missing references {missing}')
        missing = [a for a, h in r.get('analyzers', {}).items() if h is None]
        if missing:
            problems.append(f'{assembly}: missing analyzers {missing}')
        if r['exit'] != 0 or r['errors'] or r['dll_sha256'] is None:
            problems.append(f"{assembly}: exit {r['exit']}, errors {r['errors']}")
    return problems


def compile_errors_only(record):
    """Whether a failed preflight failed only with compiler errors (the correctable case)."""
    if record.get('passed') is not False or record.get('identity_problems') or record.get('corrects'):
        return False
    failures = [r for r in record['results'].values() if r['exit'] != 0]
    return bool(failures) and all(r['errors'] for r in failures) and \
        all(' missing ' not in p for p in record['problems'])


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--rsp-dir', type=Path, required=True)
    parser.add_argument('--launch', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--release', default='6000.0.71f1')
    parser.add_argument('--corrects', type=Path)
    args = parser.parse_args()
    if args.out.exists():
        sys.exit(f'refusing to reuse {args.out}')
    rsp_dir = args.rsp_dir.resolve(strict=True)
    if not rsp_dir.name.endswith('.dag') or rsp_dir.parent.name != 'artifacts':
        sys.exit(f'{rsp_dir}: not a Bee .dag artifacts directory')
    corrects = None
    if args.corrects:
        failed = json.loads(args.corrects.read_text())
        if not compile_errors_only(failed):
            sys.exit(f'{args.corrects}: not a first preflight that failed with compile errors only')
        corrects = {'path': str(args.corrects.resolve()), 'sha256': sha(args.corrects.read_bytes()),
                    'errors': {a: r['errors'] for a, r in failed['results'].items()}}
    copy = rsp_dir.parents[3]
    data_root = Path.home()/'Unity/Hub/Editor'/args.release/'Editor/Data'
    launch = args.launch.resolve(strict=True)
    record = {'release': args.release, 'rsp_dir': str(rsp_dir), 'copy': str(copy), 'launch': str(launch),
              'out': str(args.out.resolve()), 'not_used': ['/shared (compiler server)'],
              'corrects': corrects,
              'sources': {s: sha((PROBE/s).read_bytes()) for v in SOURCES.values() for s in v},
              'fixture': {'packages': files(PROBE/'Packages'), 'settings': files(PROBE/'ProjectSettings'),
                          'assets_non_script': files(PROBE/'Assets', lambda p: p.suffix != '.cs')}}
    written, proof = {}, {}
    record['identity_problems'] = identity(copy, launch, args.release, written, proof)
    record['identity_proof'] = proof
    record['editor_written_in_copy'] = written
    record['copy_scripts'] = files(copy/'Assets', lambda p: p.suffix == '.cs')
    args.out.mkdir(parents=True)
    work = args.out/'project'
    for sources in SOURCES.values():
        for source in sources:
            (work/source).parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(PROBE/source, work/source)
    if record['identity_problems']:
        record.update(compiler=None, results={}, problems=['not this fixture: '+p for p in record['identity_problems']])
    else:
        try:
            record['compiler'], record['results'] = compile_set(work, rsp_dir, data_root)
            record['problems'] = judge(record['results'])
        except (OSError, ValueError) as error:
            record.update(compiler=None, results={}, problems=[f'no valid compile graph: {error}'])
    record['passed'] = not record['problems']
    (args.out/'preflight.json').write_text(json.dumps(record, indent=1)+'\n')
    for assembly, r in record['results'].items():
        print(f"{assembly}: exit {r['exit']}, {len(r['references'])} references, {len(r['analyzers'])} analyzers, "
              f"rsp {r['rsp']['bytes']} B {r['rsp']['sha256'][:16]}, sources {r['rsp']['substitution']}, "
              f"errors {r['errors'] or 'none'}, warnings {len(r['warnings'])}")
    print('PASS' if record['passed'] else 'FAIL: '+'; '.join(record['problems']))
    return 0 if record['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
