#!/usr/bin/env python3
"""Offline compile preflight of the probe scripts with an Editor-generated graph of this fixture.

    preflight.py --copy COPY --launch LAUNCH --out NEW_DIR [--release R] [--corrects FAILED/preflight.json]
    preflight.py --copy COPY --launch LAUNCH --dry-run     identity checks and the plan; compiles nothing

COPY is the project copy of an earlier windowed Native capture of this probe (read only; no setup
Editor runs): `coreapis/<label>/projects/native1`. LAUNCH is that capture's output directory
(`coreapis/<label>/native1`, its editor.log, exit.txt and values.jsonl). The copy must prove that
its Editor-written build graph belongs to this fixture:
  * its Packages/ are this fixture's bytes; its ProjectSettings/ too, plus only the file the Editor
    writes on first open (PackageManagerSettings.asset), listed;
  * every file of this fixture's Assets/ has the same bytes in the copy and the copy has no other,
    except the one declared source change (KNOWN_CHANGE: Assets/Scripts/CoreProbe.cs, whose copy
    bytes must be the declared base and whose fixture bytes are compiled here);
  * neither has another compile input under Assets/ (.asmdef, .asmref, .rsp, plugins);
  * that launch was a windowed NVIDIA GPU Editor of this release that exited with 0 after its
    shutdown line, opened this copy (its log names the copy's Assets/), and recorded exactly the
    bytes of this fixture's existing reference;
  * the Editor compiled both assemblies with this graph: Library/ScriptAssemblies holds the graph's
    own Assembly-CSharp(.Editor).dll, the graph's Csc actions run the release's bundled Roslyn on its
    bundled runtime with DOTNET_MULTILEVEL_LOOKUP=0, and every absolute input of those actions
    (references, analyzers, compiler) still exists and is older than that compile.

The Assembly-CSharp and Assembly-CSharp-Editor response files are then used byte for byte: the
same sources (this fixture lists the same script paths), references (the module graph these
Packages/ProjectSettings select), defines, language version, analyzers and options. Only the
location is rebound: the compile runs in NEW_DIR/project (relative source and output paths) with
this checkout's scripts, and the source generators' additional file names that directory. The
compiler is invoked as the graph's Csc action (`NetCoreRuntime/dotnet exec DotNetSdkRoslyn/csc.dll
/nostdlib /noconfig @rsp @rsp2`) without `/shared` (no compiler server is left behind).

Writes NEW_DIR/preflight.json (graph identity proof, rsp bytes and the compiled text, source
change, expanded arguments, every source/reference/analyzer/compiler hash, compiler output and exit
codes, output hashes and their comparison with the graph's outputs, `passed`) and both compiler
logs. Exit 0 only when both assemblies compile without an error from a proven graph. `--corrects`
marks the single allowed correction run after a preflight that failed with compile errors only;
an identity or missing-input failure is a STOP, not a correctable compile error.
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
sys.path.insert(0, str(PROBE.parents[1]))
from probe_paths import reference_file  # noqa: E402

ASSEMBLIES = {'Assembly-CSharp': 'Assets/Scripts/', 'Assembly-CSharp-Editor': 'Assets/Editor/'}
# The one source change between the graph's launch and this fixture: the recorder repair. The copy
# holds the base bytes (the blob before the change); this fixture's bytes are what is compiled.
KNOWN_CHANGE = {'path': 'Assets/Scripts/CoreProbe.cs',
                'base_sha256': '00103ec2409b8b2b624acac154459c7c45f8818bf55ace7489ce717f14fc9def'}
# Written by the Editor into ProjectSettings/ on first open of a copy.
EDITOR_WRITTEN = ('PackageManagerSettings.asset',)
COMPILE_INPUTS = ('.asmdef', '.asmref', '.rsp', '.dll', '.so', '.dylib', '.bundle', '.a', '.jslib')
ENV_KEYS = re.compile(r'^(DOTNET_|COMPlus_|MSBUILD|UseSharedCompilation|NUGET_)')


def sha(data):
    return hashlib.sha256(data).hexdigest()


def files(root, predicate=lambda p: True):
    root = Path(root)
    return {str(p.relative_to(root)): sha(p.read_bytes()) for p in sorted(root.rglob('*'))
            if p.is_file() and predicate(p)}


def arguments(rsp):
    """Unity writes one argument per line; quotes delimit a path."""
    return [line for line in rsp.decode('utf-8').splitlines() if line.strip()]


def unquote(value):
    return value[1:-1] if len(value) >= 2 and value[0] == value[-1] == '"' else value


def is_source(argument):
    return not argument.startswith(('-', '/'))


def scripts(root):
    return sorted(str(p.relative_to(root)) for p in (Path(root)/'Assets').rglob('*.cs'))


def fixture_problems(copy, proof):
    problems, written = [], {}
    for part in ('Packages', 'ProjectSettings'):
        mine, theirs = files(PROBE/part), files(copy/part)
        for rel, digest in mine.items():
            if theirs.get(rel) != digest:
                problems.append(f'{part}/{rel} differs in the copy')
        for rel in sorted(set(theirs)-set(mine)):
            if part == 'ProjectSettings' and rel in EDITOR_WRITTEN:
                written[part+'/'+rel] = theirs[rel]
            else:
                problems.append(f'the copy has {part}/{rel}, which this fixture does not')
    mine, theirs = files(PROBE/'Assets'), files(copy/'Assets')
    change = KNOWN_CHANGE['path'][len('Assets/'):]
    differing = sorted(rel for rel in mine if rel != change and theirs.get(rel) != mine[rel])
    extra = sorted(set(theirs)-set(mine))
    if differing:
        problems.append(f'Assets differ in the copy: {differing[:5]}')
    if extra:
        problems.append(f'the copy has other Assets: {extra[:5]}')
    if theirs.get(change) != KNOWN_CHANGE['base_sha256']:
        problems.append(f"the copy's {KNOWN_CHANGE['path']} is not the declared base bytes")
    base = (copy/KNOWN_CHANGE['path']).read_text() if (copy/KNOWN_CHANGE['path']).is_file() else ''
    diff = ''.join(difflib.unified_diff(base.splitlines(True), (PROBE/KNOWN_CHANGE['path']).read_text().splitlines(True),
                                        'graph/'+KNOWN_CHANGE['path'], 'fixture/'+KNOWN_CHANGE['path']))
    proof['source_change'] = dict(KNOWN_CHANGE, fixture_sha256=mine.get(change), copy_sha256=theirs.get(change),
                                  unified_diff=diff, unified_diff_sha256=sha(diff.encode()))
    proof['assets_equal'] = {rel: d for rel, d in mine.items() if rel != change and theirs.get(rel) == d}
    for root, name in ((PROBE, 'fixture'), (copy, 'copy')):
        inputs = sorted(str(p.relative_to(root)) for p in (Path(root)/'Assets').rglob('*')
                        if p.is_file() and p.suffix.lower() in COMPILE_INPUTS)
        if inputs:
            problems.append(f'the {name} has compile inputs under Assets: {inputs[:5]}')
    if scripts(copy) != scripts(PROBE):
        problems.append(f'the copy has scripts {scripts(copy)}, this fixture {scripts(PROBE)}')
    proof['scripts'] = scripts(PROBE)
    proof['editor_written_in_copy'] = written
    return problems


def launch_problems(copy, launch, release, proof):
    problems = []
    try:
        log = (launch/'editor.log').read_text(errors='replace')
        code = (launch/'exit.txt').read_text().strip()
        values = (launch/'values.jsonl').read_bytes()
    except OSError as error:
        return [f'no launch record in {launch}: {error}']
    lines = log.splitlines()

    def first(prefix):
        return next((line for line in lines if line.startswith(prefix)), '')
    reference = reference_file(PROBE, 'jsonl')
    proof['launch'] = {'dir': str(launch), 'exit': code, 'version': first('Unity Editor version:'),
                       'batch': first('Batch mode:'), 'renderer': first('Renderer:'),
                       'shutdown_logged': any('Cleanup mono' in line for line in lines),
                       'log_sha256': sha(log.encode(errors='replace')), 'values_sha256': sha(values),
                       'reference': str(reference),
                       'reference_sha256': sha(reference.read_bytes()) if reference.exists() else None,
                       'watchdog': (launch/'values.jsonl.watchdog').exists()}
    if code != '0':
        problems.append(f'the launch exited with {code}')
    if first('Unity Editor version:').split(':', 1)[-1].split()[:1] != [release]:
        problems.append(f'the launch ran {first("Unity Editor version:")!r}, not {release}')
    if first('Batch mode:').split(':', 1)[-1].strip() != 'NO':
        problems.append('the launch was not a windowed Editor')
    if 'NVIDIA' not in first('Renderer:'):
        problems.append(f'the launch rendered with {first("Renderer:")!r}')
    if not proof['launch']['shutdown_logged'] or proof['launch']['watchdog']:
        problems.append('the launch did not finish Play mode and log its shutdown')
    if not any(str(copy.resolve()/'Assets') in line for line in lines):
        problems.append('the launch log does not name this copy')
    if proof['launch']['reference_sha256'] != sha(values):
        problems.append('the launch did not record this fixture\'s existing reference bytes')
    return problems


def graph(copy, release, proof):
    """(dag directory, {assembly: Csc node}, problems) of the copy's Editor-written build graph."""
    problems = []
    dags = [d for d in sorted((copy/'Library/Bee/artifacts').glob('*.dag')) if (d/'Assembly-CSharp.rsp').is_file()]
    if len(dags) != 1:
        return None, {}, [f'the copy has {len(dags)} compile graphs, not one']
    dag = dags[0]
    try:
        description = json.loads((copy/'Library/Bee'/(dag.name+'.json')).read_text())
    except (OSError, json.JSONDecodeError) as error:
        return dag, {}, [f'no build graph description: {error}']
    data = Path.home()/'Unity/Hub/Editor'/release/'Editor/Data'
    nodes = {}
    for assembly in ASSEMBLIES:
        found = [n for n in description.get('Nodes', [])
                 if n.get('Annotation', '').startswith(f'Csc Library/Bee/artifacts/{dag.name}/{assembly}.dll')]
        if len(found) != 1:
            problems.append(f'the graph has {len(found)} Csc actions for {assembly}')
            continue
        node = nodes[assembly] = found[0]
        rsp = f'Library/Bee/artifacts/{dag.name}/{assembly}'
        want = (f'"{data}/NetCoreRuntime/dotnet" exec "{data}/DotNetSdkRoslyn/csc.dll" /nostdlib /noconfig /shared '
                f'"@{rsp}.rsp" "@{rsp}.rsp2"')
        if node.get('Action') != want:
            problems.append(f'the {assembly} Csc action is {node.get("Action")!r}')
        if node.get('Env') != [{'Key': 'DOTNET_MULTILEVEL_LOOKUP', 'Value': '0'}]:
            problems.append(f'the {assembly} Csc environment is {node.get("Env")}')
        built = copy/'Library/ScriptAssemblies'/(assembly+'.dll')
        own = dag/(assembly+'.dll')
        if not built.is_file() or not own.is_file() or built.read_bytes() != own.read_bytes():
            problems.append(f'Library/ScriptAssemblies/{assembly}.dll is not this graph\'s output')
    inputs = {}
    for assembly, node in nodes.items():
        compiled = (dag/(assembly+'.dll')).stat().st_mtime
        for item in node.get('Inputs', []):
            path = Path(item) if Path(item).is_absolute() else copy/item
            entry = inputs.setdefault(item, {'sha256': None})
            if path.is_file():
                entry.update(sha256=sha(path.read_bytes()), bytes=path.stat().st_size)
                if Path(item).is_absolute():
                    entry['older_than_compile'] = path.stat().st_mtime < compiled
                    if not entry['older_than_compile']:
                        problems.append(f'{item} changed after the graph\'s compile')
            else:
                problems.append(f'graph input {item} is missing')
    proof['graph'] = {'dag': str(dag), 'description_sha256': sha((copy/'Library/Bee'/(dag.name+'.json')).read_bytes()),
                      'actions': {a: n.get('Action') for a, n in nodes.items()},
                      'env': {a: n.get('Env') for a, n in nodes.items()}, 'inputs': inputs,
                      'outputs': {p.name: sha(p.read_bytes()) for p in sorted(dag.glob('*.dll'))}}
    return dag, nodes, problems


def tree_digest(root):
    listing = files(root)
    return {'files': len(listing), 'sha256': sha(json.dumps(listing, sort_keys=True).encode())}


def rsp_problems(dag, nodes):
    problems = []
    for assembly, folder in ASSEMBLIES.items():
        try:
            args = arguments((dag/f'{assembly}.rsp').read_bytes())+arguments((dag/f'{assembly}.rsp2').read_bytes())
        except OSError as error:
            problems.append(f'{assembly}: {error}')
            continue
        sources = [unquote(a) for a in args if is_source(a)]
        mine = [s for s in scripts(PROBE) if s.startswith(folder)]
        if sources != mine:
            problems.append(f'{assembly}.rsp compiles {sources}, this fixture has {mine}')
        node_sources = [i for i in nodes.get(assembly, {}).get('Inputs', []) if i.endswith('.cs')]
        if node_sources != sources:
            problems.append(f'the {assembly} Csc action reads {node_sources}')
    return problems


def compile_set(work, dag, data):
    target = work/'Library/Bee/artifacts'/dag.name
    target.mkdir(parents=True)
    dotnet, csc = data/'NetCoreRuntime/dotnet', data/'DotNetSdkRoslyn/csc.dll'
    compiler = {'dotnet': str(dotnet), 'dotnet_sha256': sha(dotnet.read_bytes()), 'csc': str(csc),
                'csc_sha256': sha(csc.read_bytes()), 'roslyn': tree_digest(data/'DotNetSdkRoslyn'),
                'runtime': tree_digest(data/'NetCoreRuntime')}
    results = {}
    for assembly in ASSEMBLIES:
        rsp, rsp2 = (dag/f'{assembly}.rsp').read_bytes(), (dag/f'{assembly}.rsp2').read_bytes()
        additional = dag/f'{assembly}.UnityAdditionalFile.txt'
        (target/f'{assembly}.rsp').write_bytes(rsp)
        (target/f'{assembly}.rsp2').write_bytes(rsp2)
        rebound = str(work.resolve()).encode()
        (target/additional.name).write_bytes(rebound)
        args = arguments(rsp)+arguments(rsp2)
        sources = [unquote(a) for a in args if is_source(a)]
        refs = {}
        for ref in [unquote(a.split(':', 1)[1]) for a in args if a.startswith('-r:')]:
            path = Path(ref) if Path(ref).is_absolute() else work/ref
            refs[ref] = sha(path.read_bytes()) if path.is_file() else None
        analyzers = {a: sha(Path(a).read_bytes()) if Path(a).is_file() else None
                     for a in [unquote(a.split(':', 1)[1]) for a in args if a.startswith('-analyzer:')]}
        command = [str(dotnet), 'exec', str(csc), '/nostdlib', '/noconfig',
                   f'@Library/Bee/artifacts/{dag.name}/{assembly}.rsp', f'@Library/Bee/artifacts/{dag.name}/{assembly}.rsp2']
        env = dict(os.environ, DOTNET_MULTILEVEL_LOOKUP='0')
        process = subprocess.run(command, cwd=work, env=env, capture_output=True, stdin=subprocess.DEVNULL)
        output = (process.stdout+process.stderr).decode(errors='replace')
        (work.parent/f'{assembly}.log').write_text(output)
        outputs = {}
        for suffix in ('.dll', '.ref.dll', '.pdb'):
            path = target/(assembly+suffix)
            outputs[assembly+suffix] = sha(path.read_bytes()) if path.is_file() else None
        results[assembly] = {
            'rsp': {'source': str(dag/f'{assembly}.rsp'), 'bytes': len(rsp), 'sha256': sha(rsp),
                    'compiled_sha256': sha(rsp), 'compiled_text': rsp.decode('utf-8')},
            'rsp2': {'bytes': len(rsp2), 'sha256': sha(rsp2), 'compiled_text': rsp2.decode('utf-8')},
            'additional_file': {'original_sha256': sha(additional.read_bytes()),
                                'original': additional.read_bytes().decode(), 'rebound': rebound.decode()},
            'effective_arguments': ['/nostdlib', '/noconfig']+args,
            'langversion': [a for a in args if a.startswith('-langversion')],
            'defines': [a.split(':', 1)[1] for a in args if a.startswith('-define:')],
            'sources': {s: sha((work/s).read_bytes()) if (work/s).is_file() else None for s in sources},
            'references': refs, 'analyzers': analyzers, 'command': command, 'cwd': str(work),
            'env': {k: v for k, v in sorted(env.items()) if ENV_KEYS.match(k)},
            'exit': process.returncode, 'output': output,
            'errors': [line for line in output.splitlines() if re.search(r'\berror [A-Z]+\d+', line)],
            'warnings': [line for line in output.splitlines() if re.search(r'\bwarning [A-Z]+\d+', line)],
            'outputs': outputs,
            'graph_outputs': {name: sha((dag/name).read_bytes()) if (dag/name).is_file() else None for name in outputs}}
        if process.returncode != 0:
            break    # the Editor assembly references Assembly-CSharp.ref.dll
    return compiler, results


def judge(results):
    problems = []
    for assembly, folder in ASSEMBLIES.items():
        r = results.get(assembly)
        if r is None:
            problems.append(f'{assembly}: not compiled')
            continue
        mine = [s for s in scripts(PROBE) if s.startswith(folder)]
        if list(r['sources']) != mine or None in r['sources'].values():
            problems.append(f'{assembly}: sources {list(r["sources"])} != {mine}')
        missing = [ref for ref, h in r['references'].items() if h is None]+[a for a, h in r['analyzers'].items() if h is None]
        if missing:
            problems.append(f'{assembly}: missing references or analyzers {missing}')
        if r['exit'] != 0 or r['errors'] or r['outputs'].get(assembly+'.dll') is None:
            problems.append(f"{assembly}: exit {r['exit']}, errors {r['errors']}")
    return problems


def compile_errors_only(record):
    if record.get('passed') is not False or record.get('identity_problems') or record.get('corrects'):
        return False
    failures = [r for r in record.get('results', {}).values() if r['exit'] != 0]
    return bool(failures) and all(r['errors'] for r in failures) and all(' missing ' not in p for p in record['problems'])


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--copy', type=Path, required=True)
    parser.add_argument('--launch', type=Path, required=True)
    parser.add_argument('--out', type=Path)
    parser.add_argument('--release', default='6000.0.71f1')
    parser.add_argument('--corrects', type=Path)
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    if not args.dry_run and (args.out is None or args.out.exists()):
        sys.exit(f'--out must name a new directory: {args.out}')
    corrects = None
    if args.corrects:
        failed = json.loads(args.corrects.read_text())
        if not compile_errors_only(failed):
            sys.exit(f'{args.corrects}: not a first preflight that failed with compile errors only')
        corrects = {'path': str(args.corrects.resolve()), 'sha256': sha(args.corrects.read_bytes()),
                    'errors': {a: r['errors'] for a, r in failed['results'].items()}}
    copy, launch = args.copy.resolve(strict=True), args.launch.resolve(strict=True)
    data = Path.home()/'Unity/Hub/Editor'/args.release/'Editor/Data'
    record = {'release': args.release, 'copy': str(copy), 'launch': str(launch),
              'out': str(args.out.resolve()) if args.out else None, 'not_used': ['/shared (compiler server)'],
              'corrects': corrects, 'sources': {s: sha((PROBE/s).read_bytes()) for s in scripts(PROBE)},
              'fixture': {'packages': files(PROBE/'Packages'), 'settings': files(PROBE/'ProjectSettings'),
                          'assets': files(PROBE/'Assets')}}
    proof = {}
    problems = fixture_problems(copy, proof)+launch_problems(copy, launch, args.release, proof)
    dag, nodes, graph_problems = graph(copy, args.release, proof)
    problems += graph_problems
    if dag is not None:
        problems += rsp_problems(dag, nodes)
    record['identity_problems'] = problems
    record['identity_proof'] = proof
    if args.dry_run:
        plan = {'compile_in': str(args.out/'project') if args.out else 'NEW_DIR/project',
                'commands': [[str(data/'NetCoreRuntime/dotnet'), 'exec', str(data/'DotNetSdkRoslyn/csc.dll'), '/nostdlib',
                              '/noconfig', f'@Library/Bee/artifacts/{dag.name if dag else "?"}/{a}.rsp',
                              f'@Library/Bee/artifacts/{dag.name if dag else "?"}/{a}.rsp2'] for a in ASSEMBLIES]}
        print(json.dumps({'identity_problems': problems, 'plan': plan, 'sources': record['sources'],
                          'source_change': proof.get('source_change', {}).get('unified_diff_sha256'),
                          'graph_inputs': len(proof.get('graph', {}).get('inputs', {}))}, indent=1))
        return int(bool(problems))
    args.out.mkdir(parents=True)
    work = args.out/'project'
    for source in scripts(PROBE):
        (work/source).parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(PROBE/source, work/source)
    if problems:
        record.update(compiler=None, results={}, problems=['not this fixture\'s graph: '+p for p in problems])
    else:
        try:
            record['compiler'], record['results'] = compile_set(work, dag, data)
            record['problems'] = judge(record['results'])
        except (OSError, ValueError) as error:
            record.update(compiler=None, results={}, problems=[f'no valid compile graph: {error}'])
    record['passed'] = not record['problems']
    with (args.out/'preflight.json').open('x') as stream:
        stream.write(json.dumps(record, indent=1)+'\n')
    for assembly, r in record['results'].items():
        same = {k: r['outputs'][k] == r['graph_outputs'][k] for k in r['outputs']}
        print(f"{assembly}: exit {r['exit']}, {len(r['references'])} references, {len(r['analyzers'])} analyzers, "
              f"rsp {r['rsp']['bytes']} B {r['rsp']['sha256'][:16]}, errors {r['errors'] or 'none'}, "
              f"warnings {len(r['warnings'])}, equal to the graph's outputs {same}")
    print('PASS' if record['passed'] else 'FAIL: '+'; '.join(record['problems']))
    return 0 if record['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
