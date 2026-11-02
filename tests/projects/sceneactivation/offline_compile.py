#!/usr/bin/env python3
"""Offline preflight: compiles this probe's scripts with Unity's Roslyn and the compiler
arguments the Editor generated for a project with the SAME package manifest and settings.

    offline_compile.py --donor PROJECT --unity EDITOR_DIR --out NEW_DIR

PROJECT is an Editor-compiled copy of tests/projects/editorcallbacks (its Library/Bee holds the
Assembly-CSharp(.Editor).rsp/.rsp2 Bee wrote). This probe's Packages/ and ProjectSettings/ are
that probe's, byte for byte, except EditorBuildSettings.asset (the scene list, which no compiler
argument depends on); the script checks this against the donor copy before compiling and records
every hash. The donor's rsp files are reused line for line: every reference (modules), define,
analyzer and option stays; only the source lines are rebound to this probe's scripts, and the
additional file names the private staging project. Compiler: the Editor's csc.dll on its own
.NET runtime, as Bee calls it, without /shared (no compiler server stays behind).
Writes NEW_DIR/compile.json.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
DONOR_PROBE = PROBE.parent/'editorcallbacks'
SOURCES = {'Assembly-CSharp': sorted(p.name for p in (PROBE/'Assets/Scripts').glob('*.cs')),
           'Assembly-CSharp-Editor': sorted(p.name for p in (PROBE/'Assets/Editor').glob('*.cs'))}
FOLDER = {'Assembly-CSharp': 'Assets/Scripts', 'Assembly-CSharp-Editor': 'Assets/Editor'}
DONOR_SOURCES = {'Assembly-CSharp': ['"Assets/Scripts/ProbeDriver.cs"', '"Assets/Scripts/ProbeExtra.cs"',
                                     '"Assets/Scripts/ProbeLog.cs"', '"Assets/Scripts/ProbeMarker.cs"'],
                 'Assembly-CSharp-Editor': ['"Assets/Editor/ProbeSetup.cs"', '"Assets/Editor/SceneCallbacks.cs"']}
# Settings files the Editor writes or that name scenes; none feeds a compiler argument.
SETTINGS_EXEMPT = {'EditorBuildSettings.asset', 'PackageManagerSettings.asset'}


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def tree(root):
    return {str(p.relative_to(root)): sha(p) for p in sorted(Path(root).rglob('*')) if p.is_file()}


def identity(donor):
    """The donor copy's manifest/lock/settings against this probe's and the committed donor probe's."""
    report, problems = {}, []
    for part in ('Packages', 'ProjectSettings'):
        mine, theirs, committed = tree(PROBE/part), tree(donor/part), tree(DONOR_PROBE/part)
        exempt = SETTINGS_EXEMPT if part == 'ProjectSettings' else set()
        diff_donor = sorted(k for k in set(mine) | set(theirs) if mine.get(k) != theirs.get(k))
        diff_committed = sorted(k for k in set(mine) | set(committed) if mine.get(k) != committed.get(k))
        report[part] = {'probe': mine, 'donor': theirs, 'differs_from_donor_copy': diff_donor,
                        'differs_from_committed_donor_probe': diff_committed, 'exempt': sorted(exempt)}
        for name in diff_donor + diff_committed:
            if name not in exempt:
                problems.append(f'{part}/{name} differs')
    return report, problems


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--donor', required=True, type=Path)
    parser.add_argument('--unity', required=True, type=Path, help='the Editor directory (.../Editor)')
    parser.add_argument('--out', required=True, type=Path)
    args = parser.parse_args()
    if args.out.exists():
        sys.exit(f'refusing to overwrite {args.out}')
    dags = sorted((args.donor/'Library/Bee/artifacts').glob('*.dag'))
    if len(dags) != 1:
        sys.exit(f'{args.donor}: expected one Bee dag, found {dags}')
    donor_dag = dags[0]
    settings, problems = identity(args.donor)
    if problems:
        sys.exit('the donor is not compile-equivalent: '+'; '.join(problems))
    project = (args.out/'project').resolve()
    dag_rel = Path('Library/Bee/artifacts')/donor_dag.name
    dag = project/dag_rel
    dag.mkdir(parents=True)
    report = {'donor': str(args.donor), 'donor_dag': str(donor_dag), 'unity': str(args.unity),
              'project': str(project), 'settings_identity': settings, 'inputs': {}, 'assemblies': []}
    for assembly, names in SOURCES.items():
        (project/FOLDER[assembly]).mkdir(parents=True, exist_ok=True)
        for name in names:
            source = PROBE/FOLDER[assembly]/name
            shutil.copyfile(source, project/FOLDER[assembly]/name)
            report['inputs'][FOLDER[assembly]+'/'+name] = sha(source)
    dotnet = args.unity/'Data/NetCoreRuntime/dotnet'
    csc = args.unity/'Data/DotNetSdkRoslyn/csc.dll'
    report['compiler'] = {'dotnet': str(dotnet), 'dotnet_sha256': sha(dotnet), 'csc': str(csc), 'csc_sha256': sha(csc)}
    failed = False
    for assembly in ('Assembly-CSharp', 'Assembly-CSharp-Editor'):
        original = donor_dag/(assembly+'.rsp')
        lines = original.read_text().splitlines()
        sources = [line for line in lines if line.startswith('"Assets/')]
        if sources != DONOR_SOURCES[assembly]:
            sys.exit(f'{original}: sources {sources} are not the editorcallbacks scripts')
        replacement = ['"'+FOLDER[assembly]+'/'+name+'"' for name in SOURCES[assembly]]
        rewritten, placed = [], False
        for line in lines:
            if line.startswith('"Assets/'):
                if not placed:
                    rewritten.extend(replacement)
                    placed = True
                continue
            rewritten.append(line)
        (dag/(assembly+'.rsp')).write_text('\n'.join(rewritten)+'\n')
        shutil.copyfile(donor_dag/(assembly+'.rsp2'), dag/(assembly+'.rsp2'))
        (dag/(assembly+'.UnityAdditionalFile.txt')).write_text(str(project))
        command = [str(dotnet), 'exec', str(csc), '/nostdlib', '/noconfig',
                   '@'+str(dag_rel/(assembly+'.rsp')), '@'+str(dag_rel/(assembly+'.rsp2'))]
        run = subprocess.run(command, cwd=project, env=dict(os.environ, DOTNET_MULTILEVEL_LOOKUP='0'),
                             capture_output=True, text=True)
        output = (run.stdout+run.stderr).strip()
        (args.out/(assembly+'.log')).write_text(output+'\n')
        entry = {'assembly': assembly, 'command': command, 'cwd': str(project), 'exit': run.returncode,
                 'diagnostics': output.splitlines(),
                 'original_rsp': str(original), 'original_rsp_sha256': sha(original),
                 'original_rsp2_sha256': sha(donor_dag/(assembly+'.rsp2')),
                 'rewritten_rsp_sha256': sha(dag/(assembly+'.rsp')),
                 'references': sum(line.startswith('-r:') for line in rewritten),
                 'module_references': sorted(line.rsplit('/', 1)[-1].rstrip('"') for line in rewritten
                                             if line.startswith('-r:') and 'Module.dll' in line),
                 'defines': sum(line.startswith('-define:') for line in rewritten),
                 'analyzers': sum(line.startswith('-analyzer:') for line in rewritten),
                 'changed_lines': [line for line in rewritten if line not in lines],
                 'dropped_lines': [line for line in lines if line not in rewritten]}
        dll = dag/(assembly+'.dll')
        if dll.exists():
            entry['dll_sha256'] = sha(dll)
        report['assemblies'].append(entry)
        print(assembly, 'exit', run.returncode, ('\n'+output) if output else '(no diagnostics)')
        if run.returncode or output:
            failed = True
            break
    report['passed'] = not failed
    (args.out/'compile.json').write_text(json.dumps(report, indent=1)+'\n')
    return int(failed)


if __name__ == '__main__':
    sys.exit(main())
