#!/usr/bin/env python3
"""Suite and landing probes must execute their captured artifacts, not mutable targets."""
import contextlib
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

import land
import runtime_snapshot
import suite


class ProbeSnapshotIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.target = self.root/'target'
        self.cli = self.target/'release/unbound'
        self.cli.parent.mkdir(parents=True)
        self.cli.write_text('#!/bin/sh\nexit 0\n')
        self.cli.chmod(0o755)
        self.managed = self.target/'managed'
        self.managed.mkdir()
        (self.managed/'Unbound.Host.dll').write_bytes(b'host')
        self.environment = mock.patch.dict(os.environ, UNBOUND_RUNTIME_CACHE=str(self.root/'shared'),
                                          UNBOUND_MANAGED_DIR=str(self.managed), LD_LIBRARY_PATH='/existing/libs')
        self.environment.start()
        self.addCleanup(self.environment.stop)

    def test_suite_reuses_a_snapshot_instead_of_links_to_build_outputs(self):
        first = suite.make_runtime(self.root/'first', self.cli, self.managed)
        second = suite.make_runtime(self.root/'second', self.cli, self.managed)
        self.assertEqual(first.resolve(), second.resolve())
        self.cli.write_text('new build')
        (self.managed/'Unbound.Host.dll').write_bytes(b'new host')
        self.assertEqual((first/'unbound').read_text(), '#!/bin/sh\nexit 0\n')
        self.assertEqual((first/'managed/Unbound.Host.dll').read_bytes(), b'host')
        self.assertNotEqual(suite.make_runtime(self.root/'third', self.cli, self.managed).resolve(), first.resolve())

    def test_gate_exports_the_same_matching_snapshot_to_every_probe(self):
        gate = land.Gate(self.root, 'HEAD', [], False, self.root/'gate.log')
        gate.env['UNBOUND'] = str(self.cli)
        gate.prepare_probe_runtime(self.target)
        runtime = gate.probe_runtime
        self.assertEqual(gate.env['UNBOUND'], str(runtime/'unbound'))
        self.assertEqual(gate.env['UNBOUND_CLI'], str(runtime/'unbound'))
        self.assertEqual(gate.env['UNBOUND_MANAGED_DIR'], str(runtime/'managed'))
        self.assertEqual(gate.env['UNBOUND_RUNTIME'], str(runtime))
        self.assertEqual(gate.env['LD_LIBRARY_PATH'], str(runtime/'managed')+os.pathsep+'/existing/libs')
        (self.managed/'Unbound.Host.dll').write_bytes(b'changed build')
        self.assertEqual((runtime/'managed/Unbound.Host.dll').read_bytes(), b'host')
        self.assertEqual(json.loads((runtime/'runtime-manifest.json').read_text())['unbound'],
                         runtime_snapshot.digest(runtime/'unbound'))

    def test_legacy_suite_projects_keep_mutable_state_private_to_each_run(self):
        projects = self.root/'projects'
        source = projects/'probe'
        for folder in ('Assets', 'Packages', 'ProjectSettings', 'Reference', 'Library/Unbound'):
            (source/folder).mkdir(parents=True)
            (source/folder/'input').write_text(folder)
        original = ['unbound', 'run', '-p', str(source), '--headless', '1']
        with mock.patch.multiple(suite, PROJECTS=projects, REPO=self.root):
            first = suite.private_project(original, self.root/'first')
            second = suite.private_project(original, self.root/'second')
            self.assertEqual(first, suite.private_project(original, self.root/'first'))
            self.assertEqual(suite.private_project(['unbound', 'run', '-p', str(self.root/'outside')], self.root/'third'),
                             ['unbound', 'run', '-p', str(self.root/'outside')])
        a, b = Path(first[3]), Path(second[3])
        self.assertNotEqual(a, b)
        self.assertEqual((a/'Assets/input').read_text(), 'Assets')
        self.assertFalse((a/'Library').exists())
        (a/'Assets/input').write_text('changed only in one run')
        (a/'Library/Unbound').mkdir(parents=True)
        (a/'Library/Unbound/cache').write_text('mutable')
        self.assertEqual((b/'Assets/input').read_text(), 'Assets')
        self.assertEqual((source/'Assets/input').read_text(), 'Assets')
        self.assertFalse((b/'Library').exists())
        self.assertEqual((a/'Reference/input').read_text(), 'Reference')

    def test_staged_projects_preserve_local_and_cached_package_inputs(self):
        projects = self.root/'projects'
        source = projects/'probe'
        for folder in ('Assets', 'Packages', 'Library/PackageCache/pkg'):
            (source/folder).mkdir(parents=True)
        manifest = {'dependencies': {'local': 'file:../../local-package', 'absolute': 'file:/absolute/package', 'registry': '1.2.3'}}
        lock = {'dependencies': {'local': {'version': 'file:../../local-package', 'source': 'local'}}}
        (source/'Packages/manifest.json').write_text(json.dumps(manifest))
        (source/'Packages/packages-lock.json').write_text(json.dumps(lock))
        (source/'Library/PackageCache/pkg/package.json').write_text('{"name":"pkg"}')
        with mock.patch.multiple(suite, PROJECTS=projects, REPO=self.root):
            args = suite.private_project(['unbound', 'run', '--project='+str(source)], self.root/'run')
        project = Path(args[2].split('=', 1)[1])
        copied = json.loads((project/'Packages/manifest.json').read_text())
        self.assertEqual(copied['dependencies']['local'], 'file:'+str((source/'Packages/../../local-package').resolve()))
        self.assertEqual(copied['dependencies']['absolute'], 'file:/absolute/package')
        self.assertEqual(copied['dependencies']['registry'], '1.2.3')
        self.assertEqual(json.loads((project/'Packages/packages-lock.json').read_text())['dependencies']['local']['version'],
                         copied['dependencies']['local'])
        self.assertEqual(json.loads((source/'Packages/manifest.json').read_text()), manifest)
        self.assertEqual((project/'Library/PackageCache/pkg/package.json').read_text(), '{"name":"pkg"}')
        self.assertFalse((project/'Library/Unbound').exists())

    def test_cached_gate_retains_its_snapshot_result(self):
        runtime = suite.make_runtime(self.root/'run', self.cli, self.managed)
        def run(gate):
            gate.probe_runtime = runtime
        with mock.patch.object(land, 'STATE', self.root/'state'), \
             mock.patch.object(land, 'git', return_value='tree'), \
             mock.patch.object(land.Gate, 'run', run), \
             contextlib.redirect_stdout(io.StringIO()):
            first = land.run_gate(self.root, 'main', [], False, 'branch')
            with mock.patch.object(land.Gate, 'run', side_effect=AssertionError('cached gate rebuilt')):
                second = land.run_gate(self.root, 'main', [], False, 'branch')
        self.assertEqual(first, runtime)
        self.assertEqual(second, runtime)

    def test_wrapped_recipes_execute_with_independent_project_libraries(self):
        repo = Path(__file__).resolve().parents[2]
        recipes = suite.recipes('6000.0.71f1')
        projects = self.root/'source projects'
        home = self.root/'fake-home'
        launcher = home/'.cache/compat/with-heavy-job'
        launcher.parent.mkdir(parents=True)
        launcher.write_text(f'''#!{sys.executable}
import json, os, pathlib, sys
args = sys.argv[1:]
pathlib.Path(os.environ['LAUNCH_RECORD']).write_text(json.dumps(args))
assert args[0] == '--kind'
command = args[2:]
if len(command) > 2 and command[0] == 'timeout' and command[2] == 'sg':
    command = command[:2] + ['sh', '-c', command[-1]]
os.execvpe(command[0], command, os.environ)
''')
        launcher.chmod(0o755)
        self.cli.write_text(f'''#!{sys.executable}
import json, os, pathlib, sys
args = sys.argv[1:]
project = pathlib.Path(args[args.index('-p')+1])
(project/'Library/Unbound').mkdir(parents=True, exist_ok=True)
(project/'Library/Unbound/mutable').write_text('this run')
pathlib.Path(os.environ['ARG_RECORD']).write_text(json.dumps({{'args': args, 'project': str(project)}}))
if os.environ.get('NETWORK_STREAMING_OUT'):
    (pathlib.Path(os.environ['NETWORK_STREAMING_OUT'])/'finished').touch()
''')
        for name in ('input', 'gamepad', 'networkstreaming'):
            source = projects/name
            (source/'Assets').mkdir(parents=True)
            (source/'Assets/input').write_text(name)
            (source/'Reference').mkdir()
            (source/'Reference/unity-6000.0.71f1.jsonl').write_text('{"k":"meta","steps":"4:move 1,2"}\n')
            for script in ('run-unbound.sh', 'compare.py', 'run-probe.py', 'server.py'):
                if (repo/'tests/projects'/name/script).is_file():
                    shutil.copyfile(repo/'tests/projects'/name/script, source/script)
            captured = []
            for label in ('first $literal', 'second'):
                work = self.root/label
                record, launch = self.root/f'{name}-{label}.json', self.root/f'{name}-{label}-launch.json'
                context = {'work_root': work, 'values': {
                    'cli': str(self.cli), 'managed': str(self.managed), 'python': sys.executable,
                    'projects': str(projects), 'label': label, 'release': '6000.0.71f1'},
                    'env': dict(os.environ, HOME=str(home), ARG_RECORD=str(record), LAUNCH_RECORD=str(launch),
                                PYTHONPATH=str(repo/'tools/agents'))}
                with mock.patch.multiple(suite, PROJECTS=projects, LAUNCHER=launcher), \
                     contextlib.redirect_stdout(io.StringIO()):
                    result = suite.run_probe(name, recipes[name][:1], context)
                self.assertEqual(result['status'], 'pass', result)
                actual = json.loads(record.read_text())
                project = Path(actual['project'])
                self.assertEqual(project, work/name/'projects'/name)
                self.assertTrue((project/'Library/Unbound/mutable').is_file())
                self.assertFalse((source/'Library').exists())
                self.assertEqual(json.loads(launch.read_text())[:2],
                                 ['--kind', 'engine-gpu' if name == 'networkstreaming' else 'engine-cpu'])
                if name == 'input':
                    self.assertEqual(actual['args'][actual['args'].index('--move')+1], '3:1,2')
                captured.append(project)
            self.assertNotEqual(*captured)

    def test_input_wrapper_keeps_its_default_project(self):
        repo = Path(__file__).resolve().parents[2]
        record = self.root/'arguments.json'
        self.cli.write_text(f'#!{sys.executable}\nimport json,sys\n'
                            f'open({str(record)!r}, "w").write(json.dumps(sys.argv[1:]))\n')
        env = dict(os.environ)
        env.pop('UNBOUND_PROBE_PROJECT', None)
        subprocess.run(['sh', str(repo/'tests/projects/input/run-unbound.sh'),
                        str(repo/'tests/projects/input/Reference/unity-6000.0.71f1.jsonl'),
                        str(self.root/'out'), str(self.cli)], env=env, check=True)
        args = json.loads(record.read_text())
        self.assertEqual(args[args.index('-p')+1], str(repo/'tests/projects/input'))


if __name__ == '__main__':
    unittest.main()
