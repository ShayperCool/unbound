#!/usr/bin/env python3
"""Offline compile preflight of the witness with a real project's Editor compiler inputs.

    preflight.py --rsp-dir DAG --out NEW_DIR [--release R]

DAG is an Editor-written `Library/Bee/artifacts/<hash>.dag` directory of a copy of THIS fixture
(read only). Its Assembly-CSharp and Assembly-CSharp-Editor response files are used byte for byte:
the same references, defines, language version, analyzers and options. Only their relative
source/output paths are rebound, by compiling with NEW_DIR as the working directory. The one
absolute path, the source generators' additional file (the project directory), is rewritten to
NEW_DIR. The compiler is the Editor's bundled Roslyn, invoked as the Editor's build graph does
(`NetCoreRuntime/dotnet exec DotNetSdkRoslyn/csc.dll /nostdlib /noconfig @rsp @rsp2`, with
DOTNET_MULTILEVEL_LOOKUP=0), except `/shared`: no compiler server is started, which does not
change what is compiled.

Runs two compilations of each assembly set:
* `current`: this checkout's Assets/Scripts/IndirectSpecWitness.cs and Assets/Editor/ProbeRunner.cs
  (must succeed with no error);
* `control`: the witness as first predeclared (git eb25b98a), which the Editors rejected with
  CS1069 (Collider) at 234 and 246; the preflight must reproduce exactly that.
Writes NEW_DIR/preflight.json: rsp bytes and sha256, the expanded arguments, source hashes, every
referenced assembly's sha256, compiler outputs and exit codes. Exit 0 only if both hold.
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
REPO = PROBE.parents[2]
CONTROL_COMMIT = 'eb25b98ac5211e640c17f858a08494b39136ae08'
SOURCES = {'Assembly-CSharp': ['Assets/Scripts/IndirectSpecWitness.cs'],
           'Assembly-CSharp-Editor': ['Assets/Editor/ProbeRunner.cs']}
CONTROL_ERRORS = ['Assets/Scripts/IndirectSpecWitness.cs(234,35): error CS1069',
                  'Assets/Scripts/IndirectSpecWitness.cs(246,37): error CS1069']


def sha(data):
    return hashlib.sha256(data).hexdigest()


def arguments(rsp):
    """Unity writes one argument per line; quotes delimit a path."""
    return [line for line in rsp.decode('utf-8').splitlines() if line.strip()]


def unquote(value):
    return value[1:-1] if len(value) >= 2 and value[0] == value[-1] == '"' else value


def compile_set(work, dag_name, rsp_dir, data_root, record):
    """Rebinds and runs both assemblies in `work`; returns (problems, per-assembly records)."""
    dag = work/'Library/Bee/artifacts'/dag_name
    dag.mkdir(parents=True)
    dotnet = data_root/'NetCoreRuntime/dotnet'
    csc = data_root/'DotNetSdkRoslyn/csc.dll'
    record['compiler'] = {'dotnet': str(dotnet), 'dotnet_sha256': sha(dotnet.read_bytes()),
                          'csc': str(csc), 'csc_sha256': sha(csc.read_bytes())}
    results = {}
    for assembly in ('Assembly-CSharp', 'Assembly-CSharp-Editor'):
        rsp = (rsp_dir/f'{assembly}.rsp').read_bytes()
        rsp2 = (rsp_dir/f'{assembly}.rsp2').read_bytes()
        additional = rsp_dir/f'{assembly}.UnityAdditionalFile.txt'
        (dag/f'{assembly}.rsp').write_bytes(rsp)
        (dag/f'{assembly}.rsp2').write_bytes(rsp2)
        rebound = str(work.resolve()).encode()
        (dag/additional.name).write_bytes(rebound)
        args = arguments(rsp)+arguments(rsp2)
        sources = [unquote(a) for a in args if not a.startswith(('-', '/'))]
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
        out = work/f'Library/Bee/artifacts/{dag_name}/{assembly}.dll'
        results[assembly] = {
            'rsp': {'source': str(rsp_dir/f'{assembly}.rsp'), 'bytes': len(rsp), 'sha256': sha(rsp)},
            'rsp2': {'bytes': len(rsp2), 'sha256': sha(rsp2)},
            'additional_file': {'original_sha256': sha(additional.read_bytes()),
                                'original': additional.read_bytes().decode(), 'rebound': rebound.decode()},
            'effective_arguments': ['/nostdlib', '/noconfig']+args,
            'langversion': [a for a in args if a.startswith('-langversion')],
            'defines': [a.split(':', 1)[1] for a in args if a.startswith('-define:')],
            'sources': {s: sha((work/s).read_bytes()) if (work/s).is_file() else None for s in sources},
            'references': refs,
            'analyzers': {a: sha(Path(a).read_bytes()) for a in analyzers},
            'command': command, 'cwd': str(work), 'env': {'DOTNET_MULTILEVEL_LOOKUP': '0'},
            'exit': process.returncode, 'output': output,
            'errors': [line for line in output.splitlines() if re.search(r'\berror [A-Z]+\d+', line)],
            'dll_sha256': sha(out.read_bytes()) if out.is_file() else None}
        if process.returncode != 0:
            break    # the Editor assembly references Assembly-CSharp.ref.dll
    return results


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--rsp-dir', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--release', default='6000.0.71f1')
    args = parser.parse_args()
    if args.out.exists():
        sys.exit(f'refusing to reuse {args.out}')
    rsp_dir = args.rsp_dir.resolve(strict=True)
    if not rsp_dir.name.endswith('.dag'):
        sys.exit(f'{rsp_dir}: not a Bee .dag artifacts directory')
    data_root = Path.home()/'Unity/Hub/Editor'/args.release/'Editor/Data'
    problems = []
    # The fixture's scripts must be exactly the ones the response files compile.
    scripts = sorted(str(p.relative_to(PROBE)) for p in (PROBE/'Assets').rglob('*.cs'))
    if scripts != sorted(s for v in SOURCES.values() for s in v):
        problems.append(f'fixture scripts {scripts} are not the response files\' sources')
    args.out.mkdir(parents=True)
    record = {'release': args.release, 'rsp_dir': str(rsp_dir), 'out': str(args.out.resolve()),
              'not_used': ['/shared (compiler server)'], 'runs': {}}
    control_source = subprocess.run(
        ['git', '-C', str(REPO), 'show', f'{CONTROL_COMMIT}:tests/projects/indirectspec/Assets/Scripts/IndirectSpecWitness.cs'],
        capture_output=True, check=True).stdout
    for run in ('current', 'control'):
        work = args.out/run
        for sources in SOURCES.values():
            for source in sources:
                (work/source).parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(PROBE/source, work/source)
        if run == 'control':
            (work/'Assets/Scripts/IndirectSpecWitness.cs').write_bytes(control_source)
        results = compile_set(work, rsp_dir.name, rsp_dir, data_root, record)
        record['runs'][run] = results
        for assembly, result in results.items():
            sources = list(result['sources'])
            if sources != SOURCES[assembly]:
                problems.append(f'{run} {assembly}: sources {sources} != {SOURCES[assembly]}')
            missing = [r for r, h in result['references'].items() if h is None]
            missing = [r for r in missing if not (assembly == 'Assembly-CSharp-Editor' and r.endswith('Assembly-CSharp.ref.dll'))]
            if missing:
                problems.append(f'{run} {assembly}: missing references {missing}')
        current = results
        if run == 'current':
            for assembly in SOURCES:
                r = current.get(assembly)
                if r is None or r['exit'] != 0 or r['errors'] or r['dll_sha256'] is None:
                    problems.append(f'current {assembly}: '+('not compiled' if r is None else
                                    f"exit {r['exit']}, errors {r['errors']}"))
        else:
            r = current['Assembly-CSharp']
            found = sorted({re.match(r'(.*?: error CS\d+)', e).group(1) for e in r['errors']})
            if r['exit'] == 0 or found != sorted(CONTROL_ERRORS):
                problems.append(f'control did not reproduce the Editor failure: exit {r["exit"]}, errors {r["errors"]}')
    record['problems'] = problems
    record['passed'] = not problems
    (args.out/'preflight.json').write_text(json.dumps(record, indent=1)+'\n')
    for run, results in record['runs'].items():
        for assembly, r in results.items():
            print(f"{run} {assembly}: exit {r['exit']}, {len(r['references'])} references, "
                  f"rsp {r['rsp']['bytes']} B {r['rsp']['sha256'][:16]}, errors {r['errors'] or 'none'}")
    print('PASS' if not problems else 'FAIL: '+'; '.join(problems))
    return 0 if not problems else 1


if __name__ == '__main__':
    sys.exit(main())
