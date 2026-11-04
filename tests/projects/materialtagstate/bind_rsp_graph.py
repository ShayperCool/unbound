#!/usr/bin/env python3
"""Proves which saved genuine Editor compile a response file belongs to, then derives the
preflight response file for the material tag state fixture. Reads only; writes only to --out.

    bind_rsp_graph.py --release 6000.0.71f1 --saved <modelimport run>/projects --out <runfolder>/rsp-graph

For each saved URP repeat it binds the Assembly-CSharp-Editor response file to:
  * the Bee graph that wrote it (WriteText payload bytes) and the Csc node that consumed it
    (command line, compiler, inputs), the Bee input data (defines, references, analyzers,
    language options, packages) and the Editor log of that session;
  * the compiled output chain (Bee dll, post-processed dll, Library/ScriptAssemblies);
  * the retained project: release, sources, package lock and package cache, and the project
    files the probe's run.py composes from this repository;
  * every reference's bytes and modification time (all older than the compile).
It then rewrites only source/output/relative-path lines of repeat 1's file and records each
rebinding. Nothing is compiled or launched here.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
MODELIMPORT = REPO/'tests/projects/modelimport'
URP = REPO/'tests/projects/urp'
FIXTURE = PROBE/'Assets/Editor/MaterialTagStateDiscriminator.cs'
ASSEMBLY = 'Assembly-CSharp-Editor'
# What modelimport/run.py copy_project adds for the URP profile.
URP_FILES = ['Assets/Settings/ProbeURP.asset', 'Assets/Settings/ProbeRenderer.asset',
             'Assets/UniversalRenderPipelineGlobalSettings.asset', 'Assets/DefaultVolumeProfile.asset',
             'ProjectSettings/GraphicsSettings.asset', 'ProjectSettings/QualitySettings.asset']


# Child processes get an environment built from an empty mapping with these key names only.
CHILD_ENV_KEYS = ('PATH', 'HOME', 'LANG', 'LC_ALL', 'TZ')


def child_env():
    return {k: os.environ[k] for k in CHILD_ENV_KEYS if k in os.environ}


def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest()


def sha_bytes(b):
    return hashlib.sha256(b).hexdigest()


def unquote(s):
    return s[1:-1] if len(s) >= 2 and s[0] == '"' and s[-1] == '"' else s


def tree_digest(root):
    h = hashlib.sha256()
    for p in sorted(root.rglob('*')):
        if p.is_file() and not p.is_symlink():
            h.update(str(p.relative_to(root)).encode() + b'\0' + hashlib.sha256(p.read_bytes()).digest())
    return h.hexdigest()


def parse_rsp(text):
    lines = text.split('\n')
    if lines and lines[-1] == '':
        lines = lines[:-1]
    out = {'lines': lines, 'defines': [], 'refs': [], 'analyzers': [], 'sources': [], 'options': []}
    for i, line in enumerate(lines):
        if line.startswith('-define:'):
            out['defines'].append(line[len('-define:'):])
        elif line.startswith('-r:'):
            out['refs'].append(unquote(line[3:]))
        elif line.startswith('-analyzer:'):
            out['analyzers'].append(unquote(line[len('-analyzer:'):]))
        elif line.startswith('"') and line.endswith('.cs"'):
            out['sources'].append(unquote(line))
        else:
            out['options'].append(line)
    return out


def expected_project_files(rev, release):
    """The files modelimport/run.py copy_project(dest, 'URP') writes, from git revision rev."""
    def tree(path):
        names = subprocess.run(['git', '-C', str(REPO), 'ls-tree', '-r', '--name-only', rev, '--', path],
                               check=True, capture_output=True, text=True, env=child_env()).stdout.split('\n')
        return [n for n in names if n]

    def show(path):
        return subprocess.run(['git', '-C', str(REPO), 'show', f'{rev}:{path}'], check=True, capture_output=True,
                              env=child_env()).stdout

    files = {}
    base = 'tests/projects/modelimport/'
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        for name in tree(base + part):
            files[name[len(base):]] = show(name)
    files['ProjectSettings/ProjectVersion.txt'] = ('m_EditorVersion: ' + release + '\n').encode()
    ubase = 'tests/projects/urp/'
    for name in URP_FILES:
        files[name] = show(ubase + name)
        if name.startswith('Assets/'):
            files[name + '.meta'] = show(ubase + name + '.meta')
    files['Assets/Settings.meta'] = show(ubase + 'Assets/Settings.meta')
    manifest = json.loads(show(ubase + 'Packages/manifest.json'))
    manifest['dependencies'].update(json.loads(show(base + 'Packages/manifest.json'))['dependencies'])
    files['Packages/manifest.json'] = (json.dumps(manifest, indent=2) + '\n').encode()
    files['Packages/packages-lock.json'] = show(ubase + 'Packages/packages-lock.json')
    settings = files['ProjectSettings/ProjectSettings.asset'].decode()
    assert 'm_ActiveColorSpace: 0' in settings
    files['ProjectSettings/ProjectSettings.asset'] = settings.replace('m_ActiveColorSpace: 0', 'm_ActiveColorSpace: 1').encode()
    return files


def compare_project(project, rev, release):
    expected = expected_project_files(rev, release)
    saved = {}
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        for p in sorted((project/part).rglob('*')):
            if p.is_file():
                saved[str(p.relative_to(project))] = p
    result = {'revision': rev, 'equal': [], 'differ': [], 'missing_in_saved': [], 'extra_in_saved': []}
    for rel, data in sorted(expected.items()):
        if rel not in saved:
            result['missing_in_saved'].append(rel)
        elif saved[rel].read_bytes() == data:
            result['equal'].append(rel)
        else:
            result['differ'].append({'path': rel, 'saved_sha256': sha(saved[rel]), 'expected_sha256': sha_bytes(data)})
    for rel in sorted(set(saved) - set(expected)):
        result['extra_in_saved'].append({'path': rel, 'sha256': sha(saved[rel])})
    result['counts'] = {k: len(v) for k, v in result.items() if isinstance(v, list)}
    return result


def bind_repeat(project, log, release):
    # The explicit project argument is the base for relative Bee/compiler inputs.
    project = Path(project).absolute()
    bee = project/'Library/Bee'
    info = json.loads((bee/'bee_backend.info').read_text())
    dags = sorted(bee.glob('*.dag.json'))
    if len(dags) != 1:
        raise SystemExit(f'{project}: expected one Bee graph, found {len(dags)}')
    dag_name = dags[0].name[:-len('.dag.json')]
    dag = json.loads(dags[0].read_text())
    nodes = dag['Nodes']
    payloads = (bee/f'{dag_name}.dag.payloads').read_bytes()
    art = f'Library/Bee/artifacts/{dag_name}.dag'
    rsp_rel = f'{art}/{ASSEMBLY}.rsp'
    rsp_path = project/rsp_rel
    rsp_bytes = rsp_path.read_bytes()
    checks = {}

    def node_writing(rel):
        found = [n for n in nodes if n.get('Outputs') == [rel]]
        if len(found) != 1:
            raise SystemExit(f'{project}: {len(found)} graph nodes write {rel}')
        return found[0]

    payload_checks = {}
    for suffix in ('.rsp', '.rsp2', '.UnityAdditionalFile.txt'):
        rel = f'{art}/{ASSEMBLY}{suffix}'
        n = node_writing(rel)
        data = payloads[n['PayloadOffset']:n['PayloadOffset'] + n['PayloadLength']]
        payload_checks[suffix] = {'node': n['DebugActionIndex'], 'payload_equals_file': data == (project/rel).read_bytes(),
                                  'sha256': sha(project/rel), 'bytes': len(data)}
    checks['graph_payloads'] = payload_checks

    csc = [n for n in nodes if n.get('Annotation', '').startswith(f'Csc {art}/{ASSEMBLY}.dll')]
    if len(csc) != 1:
        raise SystemExit(f'{project}: {len(csc)} Csc nodes for {ASSEMBLY}')
    csc = csc[0]
    rsp = parse_rsp(rsp_bytes.decode('utf-8'))

    def resolve(p):
        # The Csc action runs in the project: relative compiler, rsp, reference, analyzer and source
        # paths bind there, never at the caller's working directory.
        return str((project/p) if not os.path.isabs(p) else Path(p))

    inputs = csc['Inputs']
    input_dlls = {resolve(p) for p in inputs if p.endswith('.dll')}
    rsp_refs = {resolve(p) for p in rsp['refs']}
    checks['csc_node'] = {
        'node': csc['DebugActionIndex'],
        'action': csc['Action'],
        'env': csc.get('Env'),
        'outputs': csc['Outputs'],
        'consumes_rsp': rsp_rel in inputs and f'"@{rsp_rel}"' in csc['Action'],
        'non_dll_inputs': sorted(p for p in inputs if not p.endswith('.dll')),
        'rsp_sources_equal_node_sources': sorted(rsp['sources']) == sorted(p for p in inputs if p.endswith('.cs')),
        'rsp_reference_set_equals_node_dll_inputs_minus_analyzers': rsp_refs == input_dlls - {resolve(a) for a in rsp['analyzers']},
        'node_dll_inputs_not_in_rsp_refs': sorted(input_dlls - rsp_refs),
        'rsp_refs_not_in_node_inputs': sorted(rsp_refs - input_dlls),
    }
    m = re.match(r'"([^"]+)" exec "([^"]+)"', csc['Action'])
    compiler = {'dotnet': resolve(m.group(1)), 'csc': resolve(m.group(2))} if m else None
    if compiler:
        compiler['dotnet_sha256'] = sha(compiler['dotnet'])
        compiler['csc_sha256'] = sha(compiler['csc'])
    checks['compiler'] = compiler

    data = json.loads(next(bee.glob('*-inputdata.json')).read_text())
    conf = data['BeeBuildProgramCommon.Data.ConfigurationData']
    sc = data['ScriptCompilationBuildProgram.Data.ScriptCompilationData']
    asm = [a for a in sc['Assemblies'] if a['Name'] == ASSEMBLY]
    if len(asm) != 1:
        raise SystemExit(f'{project}: inputdata has {len(asm)} {ASSEMBLY} entries')
    asm = asm[0]
    resolved_packages = {p['Name']: p['ResolvedPath'] for p in conf.get('Packages', [])}
    real_refs = {os.path.realpath(p) for p in rsp_refs}

    def package_path(p):
        # Bee input data names package files by their virtual Packages/<name>/ path.
        parts = Path(p).parts
        if len(parts) > 2 and parts[0] == 'Packages' and parts[1] in resolved_packages:
            return resolve(str(Path(resolved_packages[parts[1]]).joinpath(*parts[2:])))
        return resolve(p)

    checks['inputdata'] = {
        'unity_version': conf.get('UnityVersion'),
        'batchmode': conf.get('Batchmode'),
        'defines_equal_in_order': asm['Defines'] == rsp['defines'],
        'define_count': len(asm['Defines']),
        'sources_equal': asm['SourceFiles'] == rsp['sources'],
        'reference_count': len(asm['PrebuiltReferences']) + len(asm['References']),
        'prebuilt_references_subset_of_rsp': all(os.path.realpath(package_path(p)) in real_refs for p in asm['PrebuiltReferences']),
        'analyzers_equal_as_set': sorted(asm['Analyzers']) == sorted(rsp['analyzers']),
        'language_version': asm['LanguageVersion'],
        'rsp_has_language_version': f"-langversion:{asm['LanguageVersion']}" in rsp['options'],
        'deterministic': asm['UseDeterministicCompilation'],
        'custom_compiler_options_in_rsp': all(o in rsp['options'] for o in asm['CustomCompilerOptions']),
        'compile_project_path': asm.get('Path'),
        'compile_project_path_resolves_here': Path(asm.get('Path', '')).resolve() == project.resolve(),
        'dotnet_runtime_path': sc.get('DotnetRuntimePath'),
        'dotnet_roslyn_path': sc.get('DotnetRoslynPath'),
        'packages': sorted((p['Name'], p.get('ResolvedPath')) for p in conf.get('Packages', [])),
    }

    outs = {}
    for name in (f'{art}/{ASSEMBLY}.dll', f'{art}/post-processed/{ASSEMBLY}.dll', f'Library/ScriptAssemblies/{ASSEMBLY}.dll'):
        outs[name] = sha(project/name)
    checks['output_chain'] = {'files': outs, 'all_equal': len(set(outs.values())) == 1}

    log_text = Path(log).read_text(errors='replace')
    head = log_text[:4000]
    checks['editor_log'] = {
        'path': str(log), 'sha256': sha(log),
        'unity_version_line': next((l for l in head.split('\n') if l.startswith('Unity Editor version:')), None),
        'project_path_argument': str(project) in head,
        'nographics_argument': '\n-nographics\n' in head,
        'null_gfx_device': 'Renderer: Null Device' in log_text,
        'csc_line_present': f'Csc {art}/{ASSEMBLY}.dll' in log_text,
        'processing_line': next((l.strip() for l in log_text.split('\n') if f'Processing assembly {art}/{ASSEMBLY}.dll' in l), None),
    }
    pv = (project/'ProjectSettings/ProjectVersion.txt').read_text()
    checks['release'] = {'bee_backend_unity_version': info.get('UnityVersion'), 'project_version': pv.strip().split('\n'),
                         'matches_release': info.get('UnityVersion') == release and f'm_EditorVersion: {release}' in pv}

    compile_time = (project/f'{art}/{ASSEMBLY}.dll').stat().st_mtime
    refs = []
    for r in rsp['refs']:
        p = Path(resolve(r))
        refs.append({'rsp': r, 'resolved': str(p), 'exists': p.exists(), 'sha256': sha(p) if p.exists() else None,
                     'mtime_not_after_compile': p.exists() and p.stat().st_mtime <= compile_time})
    analyzers = [{'path': a, 'resolved': resolve(a), 'sha256': sha(resolve(a)),
                  'mtime_not_after_compile': Path(resolve(a)).stat().st_mtime <= compile_time}
                 for a in rsp['analyzers']]
    checks['references'] = {
        'count': len(refs), 'unique': len({r['resolved'] for r in refs}),
        'all_exist': all(r['exists'] for r in refs),
        'all_mtime_not_after_compile': all(r['mtime_not_after_compile'] for r in refs),
        'reference_set_sha256': sha_bytes('\n'.join(sorted(f"{r['resolved']} {r['sha256']}" for r in refs)).encode()),
    }
    checks['analyzers'] = analyzers
    srcs = []
    for s in rsp['sources']:
        p = project/s
        git = MODELIMPORT/s
        srcs.append({'path': s, 'sha256': sha(p), 'repo_head_sha256': sha(git) if git.exists() else None,
                     'equal_repo_head': git.exists() and p.read_bytes() == git.read_bytes(),
                     'mtime_not_after_compile': p.stat().st_mtime <= compile_time})
    checks['sources'] = srcs
    lock = json.loads((project/'Packages/packages-lock.json').read_text())['dependencies']
    cache = {}
    for d in sorted((project/'Library/PackageCache').iterdir()):
        pj = d/'package.json'
        if pj.exists():
            meta = json.loads(pj.read_text())
            cache[meta['name']] = {'dir': d.name, 'version': meta.get('version')}
    checks['packages'] = {
        'lock_sha256': sha(project/'Packages/packages-lock.json'),
        'lock_equals_repo_urp_lock': (project/'Packages/packages-lock.json').read_bytes() == (URP/'Packages/packages-lock.json').read_bytes(),
        'cache_versions_match_lock': {n: (c['version'] == lock.get(n, {}).get('version')) for n, c in cache.items()},
        'cache': cache,
    }
    return {'project': str(project), 'dag': dag_name, 'rsp': {'path': str(rsp_path), 'sha256': sha_bytes(rsp_bytes),
            'lines': len(rsp['lines']), 'defines': len(rsp['defines']), 'refs': len(rsp['refs']),
            'analyzers': len(rsp['analyzers']), 'sources': rsp['sources']}, 'checks': checks, 'parsed': rsp,
            'ref_rows': refs}


def derive(rep, out_dir, preflight_dir):
    project = Path(rep['project'])
    parsed = rep['parsed']
    derived, rebindings = [], []
    for i, line in enumerate(parsed['lines']):
        new = line
        if line.startswith('-out:') or line.startswith('-refout:'):
            flag, value = line.split(':', 1)
            new = f'{flag}:"{preflight_dir/Path(unquote(value)).name}"'
            reason = 'output redirected to the cache runfolder'
        elif line.startswith('-r:') and not os.path.isabs(unquote(line[3:])):
            new = f'-r:"{project/unquote(line[3:])}"'
            reason = 'relative reference made absolute against the saved project'
        elif line.startswith('-analyzer:') and not os.path.isabs(unquote(line[len('-analyzer:'):])):
            new = f'-analyzer:"{project/unquote(line[len("-analyzer:"):])}"'
            reason = 'relative analyzer made absolute against the saved project'
        elif line.startswith('/additionalfile:') and not os.path.isabs(unquote(line[len('/additionalfile:'):])):
            new = f'/additionalfile:"{project/unquote(line[len("/additionalfile:"):])}"'
            reason = 'relative additional file made absolute against the saved project'
        elif line.startswith('"') and line.endswith('.cs"'):
            new = f'"{project/unquote(line)}"'
            reason = 'original source kept, made absolute against the saved project'
        else:
            reason = None
        derived.append(new)
        if new != line:
            rebindings.append({'line': i + 1, 'original': line, 'derived': new, 'reason': reason})
        if line.startswith('"') and line.endswith('.cs"'):
            derived.append(f'"{FIXTURE}"')
            rebindings.append({'line': i + 1, 'original': None, 'derived': derived[-1],
                               'reason': 'fixture source added beside the original (import-stage DescriptionDump kept unchanged)'})
    text = '\n'.join(derived) + '\n'
    dp = parse_rsp(text)
    resolved_orig = [str(project/r) if not os.path.isabs(r) else r for r in parsed['refs']]
    resolved_analyzers = [str(project/a) if not os.path.isabs(a) else a for a in parsed['analyzers']]
    check = {
        'defines_identical_in_order': dp['defines'] == parsed['defines'],
        'analyzers_identical_in_order_after_resolution': dp['analyzers'] == resolved_analyzers,
        'options_identical_except_out_refout_additionalfile': [o for o in dp['options'] if not o.startswith(('-out:', '-refout:', '/additionalfile:'))]
            == [o for o in parsed['options'] if not o.startswith(('-out:', '-refout:', '/additionalfile:'))],
        'references_identical_in_order_after_resolution': dp['refs'] == resolved_orig,
        'reference_bytes_identical': all(sha(r) == row['sha256'] for r, row in zip(dp['refs'], rep['ref_rows'])),
        'sources': dp['sources'],
        'no_output_inside_saved_project': not any(str(project) in l for l in derived if l.startswith(('-out:', '-refout:'))),
    }
    (out_dir/f'{ASSEMBLY}.derived.rsp').write_text(text)
    return text, rebindings, check


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--release', required=True)
    ap.add_argument('--saved', required=True, type=Path, help='the modelimport run projects directory')
    ap.add_argument('--logs', required=True, type=Path, help='the modelimport run directory holding native1/native2 logs')
    ap.add_argument('--revisions', default='HEAD', help='comma-separated git revisions to compare project composition with')
    ap.add_argument('--out', required=True, type=Path)
    args = ap.parse_args()
    out = args.out
    out.mkdir(parents=True, exist_ok=False)
    preflight = out.parent/'preflight'
    repeats = {}
    for r in ('native1', 'native2'):
        project = args.saved/f'URP-{r}'
        rep = bind_repeat(project, args.logs/r/'URP.log', args.release)
        rep['composition'] = {rev: compare_project(project, rev, args.release) for rev in args.revisions.split(',')}
        repeats[r] = rep
    a, b = repeats['native1'], repeats['native2']
    norm = lambda rep: [l.replace(rep['project'], '<PROJECT>') for l in rep['parsed']['lines']]
    cross = {
        'rsp_equal_after_project_path_normalization': norm(a) == norm(b),
        'differing_lines_before_normalization': sum(1 for x, y in zip(a['parsed']['lines'], b['parsed']['lines']) if x != y),
        'reference_bytes_equal': [x['sha256'] for x in a['ref_rows']] == [y['sha256'] for y in b['ref_rows']],
        'reference_bytes_equal_outside_bee_artifacts': all(x['sha256'] == y['sha256'] for x, y in zip(a['ref_rows'], b['ref_rows'])
                                                           if not x['rsp'].startswith('Library/Bee/')),
        'bee_built_references_differing': sorted({x['rsp'] for x, y in zip(a['ref_rows'], b['ref_rows']) if x['sha256'] != y['sha256']}),
        'package_cache_tree_equal': tree_digest(Path(a['project'])/'Library/PackageCache') == tree_digest(Path(b['project'])/'Library/PackageCache'),
        'output_dll_equal': list(a['checks']['output_chain']['files'].values())[0] == list(b['checks']['output_chain']['files'].values())[0],
    }
    text, rebindings, derived_check = derive(a, out, preflight)
    command = [a['checks']['compiler']['dotnet'], 'exec', a['checks']['compiler']['csc'], '/nostdlib', '/noconfig',
               f'@{out/(ASSEMBLY + ".derived.rsp")}', f'@{Path(a["project"])/"Library/Bee/artifacts"/(a["dag"] + ".dag")/(ASSEMBLY + ".rsp2")}']
    for rep in repeats.values():
        del rep['parsed']
    identity = {'release': args.release, 'fixture': {'path': str(FIXTURE), 'sha256': sha(FIXTURE)},
                'repeats': repeats, 'cross_repeat': cross,
                'derived': {'path': str(out/(ASSEMBLY + '.derived.rsp')), 'sha256': sha_bytes(text.encode()),
                            'from': a['rsp'], 'rebindings': rebindings, 'checks': derived_check},
                'preflight_command': {'argv': command, 'cwd': str(preflight), 'env': {'DOTNET_MULTILEVEL_LOOKUP': '0'},
                                      'command_line_rebinding': 'genuine /shared omitted: no persistent compiler server'}}
    (out/'identity.json').write_text(json.dumps(identity, indent=2) + '\n')
    # The summary: every boolean proof, so a reader sees failures without digging.
    flat = {}

    def walk(o, p):
        if isinstance(o, bool):
            flat[p] = o
        elif isinstance(o, dict):
            for k, v in o.items():
                walk(v, f'{p}.{k}' if p else k)
    for r, rep in repeats.items():
        walk(rep['checks'], r)
    walk(cross, 'cross')
    walk(derived_check, 'derived')
    (out/'summary.json').write_text(json.dumps(flat, indent=2) + '\n')
    for k, v in flat.items():
        print(('true  ' if v else 'false ') + k)
    return 0


if __name__ == '__main__':
    sys.exit(main())
