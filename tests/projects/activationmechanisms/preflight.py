#!/usr/bin/env python3
"""Offline compile preflight of the probe scripts with an Editor-compiled fixture's own compiler inputs.

    preflight.py --graph CAPTURE_LABEL_DIR --out NEW_DIR [--release R] [--identity-only]
                 [--corrects FAILED/preflight.json]

The graph is the one the 6000.0.71f1 Editor wrote for the sprite import probe
(tests/projects/spriteimport), whose Packages/ selects both UnityEngine.Physics2DModule and
UnityEngine.ParticleSystemModule. CAPTURE_LABEL_DIR is that probe's capture label (`cap1`) with
projects/native1, projects/native2, native1/editor.log and native{1,2}/sprites.jsonl. This probe's
Packages/ and ProjectSettings/ copy that fixture byte for byte (read only; no setup Editor). That
capture has no editor_runner launch receipt (it predates it) and ran with -nographics, so the
copy must prove it is that fixture from what exists:
  * this probe's Packages/ProjectSettings are the committed sprite import fixture's bytes, and
    both capture copies still hold exactly these bytes and the committed fixture's Assets;
  * the Editor log of native1 names that copy, the release, -batchmode and ProbeRunner.Record, ran
    the Bee Csc steps of both assemblies and copied them to Library/ScriptAssemblies, logged a
    normal shutdown and no compile error or native fault; both copies have Library/ScriptAssemblies;
  * the independent repeat copy (native2) wrote byte-identical response files (the additional file
    naming its own copy), and both captures are the Root-published sprite reference byte for byte;
  * neither project has a compile input under Assets besides one script in Assets/Scripts
    (Assembly-CSharp) and Assets/Editor/ProbeRunner.cs (Assembly-CSharp-Editor) -- no .asmdef,
    .asmref, .rsp, managed or native plugin;
  * the runtime response file references both required modules and every reference exists.

The response files are then used byte for byte: the same references (the module graph these
Packages/ProjectSettings select), defines, language version, analyzers and options. Only paths are
rebound: the one source path of each assembly is replaced by this probe's script in the same folder,
the compile runs in NEW_DIR/project (relative output paths), and the source generators' additional
file names that directory. The compiler is the Editor's bundled Roslyn on its own runtime, invoked
as the Editor's build graph does (`NetCoreRuntime/dotnet exec DotNetSdkRoslyn/csc.dll /nostdlib
/noconfig @rsp @rsp2`, DOTNET_MULTILEVEL_LOOKUP=0), without `/shared` (no compiler server).

`--identity-only` writes the identity proof and the rebound response files' hashes and stops
before any compiler starts (a tool or identity failure then costs no compile).

Writes NEW_DIR/preflight.json (rsp bytes/hashes and rebound text, identity proof, source
substitution, expanded arguments, source/reference/analyzer/compiler hashes, compiler output, exit
codes, `passed`) and both compiler logs. Exit 0 only when both assemblies compile without an error
and every reference and analyzer exists. `--corrects` marks the single allowed correction run after
a preflight that failed with compile errors only; it refuses to correct a correction or a run that
failed for another reason (an identity or missing-input failure is a STOP).
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
FIXTURE = REPO/'tests/projects/spriteimport'
SOURCES = {'Assembly-CSharp': ['Assets/Scripts/ActivationMechanismsProbe.cs'],
           'Assembly-CSharp-Editor': ['Assets/Editor/ProbeRunner.cs']}
FOLDERS = {'Assembly-CSharp': 'Assets/Scripts/', 'Assembly-CSharp-Editor': 'Assets/Editor/'}
GRAPH_SOURCES = {'Assembly-CSharp': 'Assets/Scripts/SpriteImportProbe.cs', 'Assembly-CSharp-Editor': 'Assets/Editor/ProbeRunner.cs'}
REQUIRED_MODULES = ('UnityEngine.Physics2DModule.dll', 'UnityEngine.ParticleSystemModule.dll')
# Files under Assets/ that change what the Editor compiles (assembly definitions and references,
# response files, managed and native plugins).
COMPILE_INPUTS = ('.asmdef', '.asmref', '.rsp', '.dll', '.so', '.dylib', '.bundle', '.a', '.jslib')
LOG_FATAL = (r'error CS\d{4}', r'Scripts have compiler errors', r'Caught fatal signal', r'Native Crash Reporting',
             r'Crash!!!', r'Aborting batchmode due to failure')


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


def dag_of(copy):
    dags = [d for d in sorted((Path(copy)/'Library/Bee/artifacts').glob('*.dag')) if (d/'Assembly-CSharp.rsp').is_file()]
    return dags[0] if len(dags) == 1 else None


def log_facts(path, copy, release):
    """What the Editor log of the graph's capture shows (problems, facts)."""
    problems, facts = [], {}
    try:
        lines = Path(path).read_text(errors='replace').splitlines()
    except OSError as error:
        return [f'no Editor log: {error}'], facts
    try:
        start = lines.index('COMMAND LINE ARGUMENTS:')
    except ValueError:
        return ['the Editor log has no command line'], facts
    args = []
    for line in lines[start+1:]:
        if line.startswith('Successfully changed project path') or not line.strip():
            break
        args.append(line.strip())
    facts['command'] = args
    pairs = list(zip(args, args[1:]))
    project = next((b for a, b in pairs if a == '-projectPath'), None)
    if args[:1] and not args[0].endswith(f'/{release}/Editor/Unity'):
        problems.append(f'the log names Editor {args[:1]}, not release {release}')
    if project is None or Path(project).resolve() != Path(copy).resolve():
        problems.append(f'the log names project {project}, not the graph copy {copy}')
    if '-batchmode' not in args or ('-executeMethod', 'ProbeRunner.Record') not in pairs:
        problems.append('the log is not a batchmode ProbeRunner.Record capture')
    facts['nographics'] = '-nographics' in args
    facts['renderer'] = next((line.split(':', 1)[1].strip() for line in lines if line.strip().startswith('Renderer:')), None)
    for assembly in ('Assembly-CSharp', 'Assembly-CSharp-Editor'):
        csc = [line for line in lines if re.search(r'\] Csc Library/Bee/artifacts/[^/]+\.dag/' + re.escape(assembly) + r'\.dll', line)]
        copied = [line for line in lines if line.rstrip().endswith(f'CopyFiles Library/ScriptAssemblies/{assembly}.dll')]
        facts[assembly] = {'csc': csc, 'copied': copied}
        if not csc or not copied:
            problems.append(f'the log shows no compile and copy of {assembly}')
    fatal = [line for line in lines if any(re.search(p, line) for p in LOG_FATAL)]
    if fatal:
        problems.append(f'the log has compile errors or faults: {fatal[:3]}')
    facts['shutdown_logged'] = any('Cleanup mono' in line for line in lines)
    if not facts['shutdown_logged']:
        problems.append('the log has no normal shutdown (Cleanup mono)')
    facts['version_in_log'] = any(release in line for line in lines[:start])
    if not facts['version_in_log']:
        problems.append(f'the log header does not name {release}')
    return problems, facts


def identity(graph, release, written, proof):
    """Why the graph is not an Editor-compiled copy of this fixture's Packages/ProjectSettings
    (empty list: it is)."""
    problems = []
    copy, repeat = graph/'projects/native1', graph/'projects/native2'
    proof['graph'] = str(graph)
    for part in ('Packages', 'ProjectSettings'):
        mine, committed = files(PROBE/part), files(FIXTURE/part)
        if mine != committed:
            problems.append(f'this probe\'s {part} are not the sprite import fixture\'s bytes: '
                            + str(sorted(k for k in set(mine) | set(committed) if mine.get(k) != committed.get(k))[:5]))
        for name, project in (('copy', copy), ('repeat copy', repeat)):
            theirs = files(project/part)
            if theirs != mine:
                problems.append(f'the {name} {part} differ from this fixture: '
                                + str(sorted(k for k in set(mine) | set(theirs) if mine.get(k) != theirs.get(k))[:5]))
    for name, project in (('copy', copy), ('repeat copy', repeat)):
        if files(project/'Assets') != files(FIXTURE/'Assets'):
            problems.append(f'the {name} Assets are not the committed sprite import fixture\'s')
        written[name] = {'Library/ScriptAssemblies': files(project/'Library/ScriptAssemblies', lambda p: p.suffix == '.dll')}
        for assembly in SOURCES:
            if not (project/'Library/ScriptAssemblies'/(assembly+'.dll')).is_file():
                problems.append(f'the Editor did not compile {assembly} in the {name}')
    for root, name in ((PROBE, 'fixture'), (copy, 'copy'), (repeat, 'repeat copy')):
        extra = sorted(str(p.relative_to(root)) for p in (root/'Assets').rglob('*')
                       if p.is_file() and p.suffix.lower() in COMPILE_INPUTS)
        if extra:
            problems.append(f'the {name} has compile inputs under Assets: {extra[:5]}')
        scripts = sorted(str(p.relative_to(root)) for p in (root/'Assets').rglob('*.cs'))
        proof.setdefault('scripts', {})[name] = scripts
        for assembly, folder in FOLDERS.items():
            theirs = [s for s in scripts if s.startswith(folder)]
            if len(theirs) != 1:
                problems.append(f'the {name} has {theirs} in {folder}, not exactly one {assembly} script')
        if [s for s in scripts if not s.startswith(tuple(FOLDERS.values()))]:
            problems.append(f'the {name} compiles scripts outside {list(FOLDERS.values())}: {scripts}')
    if proof['scripts'].get('fixture') != sorted(s for v in SOURCES.values() for s in v):
        problems.append(f"this fixture has scripts {proof['scripts'].get('fixture')}, not the two probe scripts")
    log_problems, facts = log_facts(graph/'native1/editor.log', copy, release)
    problems += log_problems
    proof['log'] = {'path': str(graph/'native1/editor.log'), 'sha256': sha((graph/'native1/editor.log').read_bytes())
                    if (graph/'native1/editor.log').is_file() else None, **facts}
    dag, dag2 = dag_of(copy), dag_of(repeat)
    if dag is None or dag2 is None:
        problems.append('a graph copy has not exactly one Bee graph with Assembly-CSharp.rsp')
    else:
        proof['dag'] = str(dag)
        for assembly in SOURCES:
            for suffix in ('.rsp', '.rsp2'):
                a, b = (d/(assembly+suffix) for d in (dag, dag2))
                if not a.is_file() or not b.is_file() or a.read_bytes() != b.read_bytes():
                    problems.append(f'the two capture copies wrote different {assembly}{suffix}')
            for d, project in ((dag, copy), (dag2, repeat)):
                extra = d/(assembly+'.UnityAdditionalFile.txt')
                if not extra.is_file() or Path(extra.read_text().strip()).resolve() != project.resolve():
                    problems.append(f'{extra} does not name its own copy')
            text = (dag/(assembly+'.rsp')).read_text()
            if f'"{GRAPH_SOURCES[assembly]}"' not in text.splitlines():
                problems.append(f'{assembly}.rsp does not compile {GRAPH_SOURCES[assembly]}')
        refs = [unquote(a.split(':', 1)[1]) for a in arguments((dag/'Assembly-CSharp.rsp').read_bytes()) if a.startswith('-r:')]
        proof['required_modules'] = {m: next((r for r in refs if Path(r).name == m), None) for m in REQUIRED_MODULES}
        for m, path in proof['required_modules'].items():
            if path is None or not Path(path).is_file():
                problems.append(f'the runtime graph has no {m}')
    reference = FIXTURE/f'Reference/unity-{release}.jsonl'
    captures = [graph/r/'sprites.jsonl' for r in ('native1', 'native2')]
    if not reference.is_file() or any(not c.is_file() or c.read_bytes() != reference.read_bytes() for c in captures):
        problems.append('the graph captures are not the published sprite import reference')
    proof['reference'] = {'path': str(reference), 'sha256': sha(reference.read_bytes()) if reference.is_file() else None,
                          'captures': {str(c): sha(c.read_bytes()) if c.is_file() else None for c in captures}}
    version = (PROBE/'ProjectSettings/ProjectVersion.txt').read_text()
    if not version.startswith(f'm_EditorVersion: {release}\n'):
        problems.append(f'the fixture is not a {release} project')
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


def identity_rebound(rsp_dir, assembly):
    """The response files' hashes and the source substitution, without compiling."""
    original = (rsp_dir/f'{assembly}.rsp').read_bytes()
    rsp, substitution = rebind_sources(original, assembly)
    rsp2 = (rsp_dir/f'{assembly}.rsp2').read_bytes()
    return {'rsp_sha256': sha(original), 'rebound_sha256': sha(rsp), 'rsp2_sha256': sha(rsp2),
            'substitution': substitution}


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
    if record.get('passed') is not False or record.get('identity_problems') or record.get('corrects') \
            or record.get('identity_only'):
        return False
    failures = [r for r in record['results'].values() if r['exit'] != 0]
    return bool(failures) and all(r['errors'] for r in failures) and \
        all(' missing ' not in p for p in record['problems'])


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--graph', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--release', default='6000.0.71f1')
    parser.add_argument('--corrects', type=Path)
    parser.add_argument('--identity-only', action='store_true')
    args = parser.parse_args()
    if args.out.exists():
        sys.exit(f'refusing to reuse {args.out}')
    graph = args.graph.resolve(strict=True)
    corrects = None
    if args.corrects:
        failed = json.loads(args.corrects.read_text())
        if not compile_errors_only(failed):
            sys.exit(f'{args.corrects}: not a first preflight that failed with compile errors only')
        corrects = {'path': str(args.corrects.resolve()), 'sha256': sha(args.corrects.read_bytes()),
                    'errors': {a: r['errors'] for a, r in failed['results'].items()}}
    data_root = Path.home()/'Unity/Hub/Editor'/args.release/'Editor/Data'
    record = {'release': args.release, 'graph': str(graph), 'out': str(args.out.resolve()),
              'not_used': ['/shared (compiler server)'], 'corrects': corrects,
              'sources': {s: sha((PROBE/s).read_bytes()) for v in SOURCES.values() for s in v},
              'fixture': {'packages': files(PROBE/'Packages'), 'settings': files(PROBE/'ProjectSettings'),
                          'assets_non_script': files(PROBE/'Assets', lambda p: p.suffix != '.cs')}}
    written, proof = {}, {}
    record['identity_problems'] = identity(graph, args.release, written, proof)
    record['identity_proof'] = proof
    record['editor_written_in_copies'] = written
    rsp_dir = Path(proof['dag']) if proof.get('dag') else None
    record['rsp_dir'] = str(rsp_dir) if rsp_dir else None
    args.out.mkdir(parents=True)
    work = args.out/'project'
    for sources in SOURCES.values():
        for source in sources:
            (work/source).parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(PROBE/source, work/source)
    if record['identity_problems']:
        record.update(compiler=None, results={}, problems=['not this fixture: '+p for p in record['identity_problems']])
    elif args.identity_only:
        record.update(compiler=None, results={}, problems=[], identity_only=True,
                      rebound={a: identity_rebound(rsp_dir, a) for a in SOURCES})
    else:
        try:
            record['compiler'], record['results'] = compile_set(work, rsp_dir, data_root)
            record['problems'] = judge(record['results'])
        except (OSError, ValueError) as error:
            record.update(compiler=None, results={}, problems=[f'no valid compile graph: {error}'])
    # An identity-only record is never a passing preflight.
    record['passed'] = not record['problems'] and not args.identity_only
    (args.out/'preflight.json').write_text(json.dumps(record, indent=1)+'\n')
    for assembly, r in record['results'].items():
        print(f"{assembly}: exit {r['exit']}, {len(r['references'])} references, {len(r['analyzers'])} analyzers, "
              f"rsp {r['rsp']['bytes']} B {r['rsp']['sha256'][:16]}, sources {r['rsp']['substitution']}, "
              f"errors {r['errors'] or 'none'}, warnings {len(r['warnings'])}")
    if args.identity_only:
        print('IDENTITY', 'OK' if not record['problems'] else 'FAIL: '+'; '.join(record['problems']),
              json.dumps(record.get('rebound', {})))
        return 0 if not record['problems'] else 1
    print('PASS' if record['passed'] else 'FAIL: '+'; '.join(record['problems']))
    return 0 if record['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
