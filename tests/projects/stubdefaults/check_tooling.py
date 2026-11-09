#!/usr/bin/env python3
"""Check generator unit tests and doctor's file/type/message reports, including a warm cache."""
import argparse
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
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--doctor-only', action='store_true', help='short landing check; omit generator unit tests')
    args = parser.parse_args()
    if not args.doctor_only:
        dotnet = Path.home()/'.dotnet/dotnet'
        if not dotnet.is_file():
            dotnet = shutil.which('dotnet')
        result = subprocess.run([str(HEAVY), '--kind', 'test', str(dotnet), 'test',
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
        ('DoctorCoverageControls.InheritedUnsentComponent', 'OnParticleUpdateJobScheduled'),
    }
    # A message below a user-defined MonoBehaviour base must be found, and inherited uses
    # must report its declaration once rather than adding a diagnostic for every leaf type.
    source_lines = (ROOT/'Assets/Scripts/MessageCoverageControls.cs').read_text().splitlines()
    inherited_line = next(i for i, line in enumerate(source_lines, 1)
                          if 'protected void OnParticleUpdateJobScheduled' in line)
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
        inherited = [m for m in found if m['type'] == 'DoctorCoverageControls.InheritedUnsentComponent']
        bad_inheritance = len(inherited) != 1 or inherited[0]['line'] != inherited_line
        if actual != expected or len(found) != len(expected) or bad_locations or bad_inheritance or report['messageAnalysisFailedAssemblies']:
            print(json.dumps(report, indent=2))
            print('missing:', sorted(expected - actual), 'extra:', sorted(actual - expected))
            return 1
        print(f'doctor pass {repeat + 1}: {len(found)} messages with source locations; no controls misclassified')
    return 0


if __name__ == '__main__':
    sys.exit(main())
