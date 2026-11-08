#!/usr/bin/env python3
"""Check generator unit tests and doctor's file/type/message reports, including a warm cache."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
HEAVY = Path.home()/'.cache/compat/with-heavy-job'


def main():
    dotnet = Path.home()/'.dotnet/dotnet'
    if not dotnet.is_file():
        dotnet = shutil.which('dotnet')
    result = subprocess.run([str(HEAVY), '--kind', 'build', str(dotnet), 'test',
                             str(REPO/'managed/Unbound.ApiTools.Tests/Unbound.ApiTools.Tests.csproj'),
                             '-c', 'Release', '--verbosity', 'quiet'], cwd=REPO)
    if result.returncode:
        return result.returncode
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    # StateMachineBehaviour.OnStateIK is sent since the animation update runs IK passes
    # (tests/projects/animcallbacks), so UnsentIK and InheritedIK are now controls that doctor
    # must not report.
    expected = {
        ('DoctorCoverageControls.UnsentComponent', 'OnParticleUpdateJobScheduled'),
        ('DoctorCoverageControls.UnsentComponent', 'OnChildRectTransformDimensionsChange'),
    }
    for repeat in range(2):
        run = subprocess.run([str(HEAVY), '--kind', 'tool', cli, 'doctor', '-p', str(ROOT), '--json'],
                             cwd=REPO, capture_output=True, text=True)
        if run.returncode:
            print(run.stdout, run.stderr)
            return run.returncode
        report = json.loads(run.stdout)
        found = report['unsupportedMessages']
        actual = {(m['type'], m['message']) for m in found}
        bad_locations = [m for m in found if m['file'] != 'Assets/Scripts/MessageCoverageControls.cs' or m['line'] <= 0]
        if actual != expected or len(found) != len(expected) or bad_locations or report['messageAnalysisFailedAssemblies']:
            print(json.dumps(report, indent=2))
            print('missing:', sorted(expected - actual), 'extra:', sorted(actual - expected))
            return 1
        print(f'doctor pass {repeat + 1}: {len(found)} messages with source locations; no controls misclassified')
    return 0


if __name__ == '__main__':
    sys.exit(main())
