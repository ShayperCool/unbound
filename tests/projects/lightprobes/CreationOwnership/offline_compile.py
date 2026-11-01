#!/usr/bin/env python3
"""Offline compile of the creation-ownership witness with the fixture's own compiler arguments.

    offline_compile.py --dag DAG --unity EDITOR --out NEW_DIR

DAG is Library/Bee/artifacts/<hash>.dag of an existing valid lightprobes witness project copy
(an Editor-compiled one). Its Assembly-CSharp.rsp / Assembly-CSharp-Editor.rsp (+ .rsp2) are
reused line for line: every reference, define, analyzer and option stays, only the source
lines change (Assembly-CSharp: the three witness scripts instead of the lifecycle witness; the
Editor assembly keeps its two sources) and the additional file names the staging project. The
compiler is the Editor's own Roslyn on its own .NET runtime, as Bee calls it, without /shared
(no compiler server is left behind). Writes NEW_DIR/compile.json with every hash and argument.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

SUITE = Path(__file__).resolve().parent
PROBE = SUITE.parent
SCRIPTS = ('SceneCreationOwnership.cs', 'CreationOwnershipTracker.cs', 'CreationTeardownWitness.cs')
EXPECTED_SOURCES = {'Assembly-CSharp': ['"Assets/Scripts/LightProbesLifecycle.cs"'],
                    'Assembly-CSharp-Editor': ['"Assets/Editor/LightProbesSetup.cs"', '"Assets/Editor/ProbeRunner.cs"']}


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--dag', required=True, type=Path)
    parser.add_argument('--unity', required=True, type=Path, help='the Editor directory (.../Editor)')
    parser.add_argument('--out', required=True, type=Path)
    args = parser.parse_args()
    if args.out.exists():
        sys.exit(f'refusing to overwrite {args.out}')
    project = (args.out/'project').resolve()
    dag_rel = Path('Library/Bee/artifacts')/args.dag.name
    dag = project/dag_rel
    dag.mkdir(parents=True)
    (project/'Assets/Scripts').mkdir(parents=True)
    (project/'Assets/Editor').mkdir(parents=True)
    report = {'dag': str(args.dag), 'unity': str(args.unity), 'project': str(project), 'inputs': {}, 'assemblies': []}
    for name in SCRIPTS:
        shutil.copyfile(SUITE/name, project/'Assets/Scripts'/name)
        report['inputs']['Assets/Scripts/'+name] = sha(SUITE/name)
    for name in ('LightProbesSetup.cs', 'ProbeRunner.cs'):
        shutil.copyfile(PROBE/'Assets/Editor'/name, project/'Assets/Editor'/name)
        report['inputs']['Assets/Editor/'+name] = sha(PROBE/'Assets/Editor'/name)
    dotnet = args.unity/'Data/NetCoreRuntime/dotnet'
    csc = args.unity/'Data/DotNetSdkRoslyn/csc.dll'
    report['compiler'] = {'dotnet': str(dotnet), 'dotnet_sha256': sha(dotnet), 'csc': str(csc), 'csc_sha256': sha(csc)}
    failed = False
    for assembly in ('Assembly-CSharp', 'Assembly-CSharp-Editor'):
        original = args.dag/(assembly+'.rsp')
        lines = original.read_text().splitlines()
        sources = [line for line in lines if line.startswith('"Assets/')]
        if sources != EXPECTED_SOURCES[assembly]:
            sys.exit(f'{original}: sources {sources} are not the lightprobes witness copy {EXPECTED_SOURCES[assembly]}')
        replacement = ['"Assets/Scripts/'+name+'"' for name in SCRIPTS] if assembly == 'Assembly-CSharp' else sources
        rewritten, placed = [], False
        for line in lines:
            if line.startswith('"Assets/'):
                if not placed:
                    rewritten.extend(replacement)
                    placed = True
                continue
            rewritten.append(line)
        (dag/(assembly+'.rsp')).write_text('\n'.join(rewritten)+'\n')
        shutil.copyfile(args.dag/(assembly+'.rsp2'), dag/(assembly+'.rsp2'))
        (dag/(assembly+'.UnityAdditionalFile.txt')).write_text(str(project))
        changed = [line for line in rewritten if line not in lines]
        dropped = [line for line in lines if line not in rewritten]
        command = [str(dotnet), 'exec', str(csc), '/nostdlib', '/noconfig',
                   '@'+str(dag_rel/(assembly+'.rsp')), '@'+str(dag_rel/(assembly+'.rsp2'))]
        run = subprocess.run(command, cwd=project, env=dict(os.environ, DOTNET_MULTILEVEL_LOOKUP='0'),
                             capture_output=True, text=True)
        output = (run.stdout+run.stderr).strip()
        (args.out/(assembly+'.log')).write_text(output+'\n')
        entry = {'assembly': assembly, 'command': command, 'cwd': str(project), 'exit': run.returncode,
                 'diagnostics': output.splitlines(),
                 'original_rsp': str(original), 'original_rsp_sha256': sha(original),
                 'original_rsp2_sha256': sha(args.dag/(assembly+'.rsp2')),
                 'original_additionalfile_sha256': sha(args.dag/(assembly+'.UnityAdditionalFile.txt')),
                 'rewritten_rsp_sha256': sha(dag/(assembly+'.rsp')),
                 'references': sum(line.startswith('-r:') for line in rewritten),
                 'defines': sum(line.startswith('-define:') for line in rewritten),
                 'analyzers': sum(line.startswith('-analyzer:') for line in rewritten),
                 'options': [line for line in rewritten if line.startswith('/') or line.startswith('-langversion')
                             or line.startswith('-target')],
                 'changed_lines': changed, 'dropped_lines': dropped}
        dll = dag/(assembly+'.dll')
        if dll.exists():
            entry['dll_sha256'] = sha(dll)
        report['assemblies'].append(entry)
        print(assembly, 'exit', run.returncode, ('\n'+output) if output else '(no diagnostics)')
        if run.returncode:
            failed = True
            break
    (args.out/'compile.json').write_text(json.dumps(report, indent=1)+'\n')
    return int(failed)


if __name__ == '__main__':
    sys.exit(main())
