#!/usr/bin/env python3
"""Offline compile preflight of the probe scripts with an Editor-compiled fixture's own compiler inputs.

    preflight.py --rsp-dir DAG --out NEW_DIR [--release R] [--corrects FAILED/preflight.json]

DAG is an Editor-written `Library/Bee/artifacts/<hash>.dag` directory of an earlier Editor-compiled
copy of the particles fixture this probe's fixture is made of (read only; no setup Editor). The
copy must prove it is that fixture: its Packages/ and ProjectSettings/ and every non-script file
committed under this probe's Assets/ are byte-identical; the Editor compiled its scripts
(Library/ScriptAssemblies); it has exactly one script in Assets/Scripts (Assembly-CSharp) and
Assets/Editor/ProbeRunner.cs (Assembly-CSharp-Editor), which are the folders of this probe's two
scripts. Files the Editor wrote besides the committed ones are listed as outputs.

The Assembly-CSharp and Assembly-CSharp-Editor response files are then used byte for byte: the
same references (the fixture's own module graph), defines, language version, analyzers and
options. Only paths are rebound: the one source path of each assembly is replaced by this probe's
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
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
SOURCES = {'Assembly-CSharp': ['Assets/Scripts/SubEmitterTimingProbe.cs'],
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


def identity(copy, written):
    """Why the copy is not an Editor-compiled copy of this fixture (empty list: it is)."""
    problems = []
    not_script = lambda p: p.suffix != '.cs'  # noqa: E731
    for part, predicate in (('Packages', lambda p: True), ('ProjectSettings', lambda p: True), ('Assets', not_script)):
        mine, theirs = files(PROBE/part, predicate), files(copy/part, predicate)
        changed = sorted(k for k in mine if theirs.get(k) != mine[k])
        if changed:
            problems.append(f'{part} differs from the fixture: {changed[:5]}')
        written.update({part+'/'+k: v for k, v in theirs.items() if k not in mine})
    scripts = sorted(str(p.relative_to(copy)) for p in (copy/'Assets').rglob('*.cs'))
    for assembly, folder in FOLDERS.items():
        theirs = [s for s in scripts if s.startswith(folder)]
        if len(theirs) != 1:
            problems.append(f'the copy has {theirs} in {folder}, not exactly one {assembly} script')
    if len([s for s in scripts if not s.startswith(tuple(FOLDERS.values()))]):
        problems.append(f'the copy compiles scripts outside {list(FOLDERS.values())}: {scripts}')
    if 'Assets/Editor/ProbeRunner.cs' not in scripts:
        problems.append('the copy has no Assets/Editor/ProbeRunner.cs')
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
    record = {'release': args.release, 'rsp_dir': str(rsp_dir), 'copy': str(copy),
              'out': str(args.out.resolve()), 'not_used': ['/shared (compiler server)'],
              'corrects': corrects,
              'sources': {s: sha((PROBE/s).read_bytes()) for v in SOURCES.values() for s in v},
              'fixture': {'packages': files(PROBE/'Packages'), 'settings': files(PROBE/'ProjectSettings'),
                          'assets_non_script': files(PROBE/'Assets', lambda p: p.suffix != '.cs')}}
    written = {}
    record['identity_problems'] = identity(copy, written)
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
