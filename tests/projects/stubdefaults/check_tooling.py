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
    # Every control declares a message Unbound sends, so doctor must report none of them:
    # StateMachineBehaviour.OnStateIK is sent since the animation update runs IK passes
    # (tests/projects/animcallbacks), the child-dimension declaration is a supported-message
    # control (rectmessages), and OnParticleUpdateJobScheduled goes to every script of a system
    # its update steps (tests/projects/particlejobs), inherited declarations included.
    # Reporting, source locations and inherited declarations of an unsent message are covered by
    # Unbound.ApiTools.Tests' MessageCoverageTests with an injected rule list.
    expected = set()
    for repeat in range(2):
        run = subprocess.run([str(HEAVY), '--kind', 'tool', cli, 'doctor', '-p', str(ROOT), '--json'],
                             cwd=REPO, capture_output=True, text=True)
        if run.returncode:
            print(run.stdout, run.stderr)
            return run.returncode
        report = json.loads(run.stdout)
        found = report['unsupportedMessages']
        actual = {(m['type'], m['message']) for m in found}
        if actual != expected or len(found) != len(expected) or report['messageAnalysisFailedAssemblies']:
            print(json.dumps(report, indent=2))
            print('missing:', sorted(expected - actual), 'extra:', sorted(actual - expected))
            return 1
        print(f'doctor pass {repeat + 1}: {len(found)} messages reported; no controls misclassified')
    return 0


if __name__ == '__main__':
    sys.exit(main())
