"""Checks the shared Editor launcher with fake Editor, licensing and Hub processes (no Unity).

    python3 tests/test_editor_runner.py
"""
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parent))
import editor_runner as er  # noqa: E402

# A fake Editor: makes itself a session leader like Unity, starts a licensing client and a
# package manager in its group, may start a Hub for its project (which ignores TERM, starts
# a helper and a crash handler in its own session), writes a log, then behaves per FAKE_MODE.
FAKE = r'''
import os, signal, subprocess, sys, time
mode = os.environ['FAKE_MODE']
tmp = os.environ['FAKE_ROOT']
python = os.environ['FAKE_PYTHON']  # sys.executable names the fake Editor here
args = sys.argv[1:]
project = args[args.index('-projectPath')+1]
log = open(args[args.index('-logFile')+1], 'a', buffering=1)
if os.getsid(0) != os.getpid():
    os.setsid()

def spawn(argv0, *rest, session=False, ignore_term=False, code='', name=None):
    body = ('import signal, time, os\n' + ('signal.signal(signal.SIGTERM, signal.SIG_IGN)\n' if ignore_term else '')
            + code + '\ntime.sleep(600)\n')
    p = subprocess.Popen([argv0, '-c', body, *rest], executable=python, start_new_session=session)
    if name:
        open(os.path.join(tmp, name+'.pid'), 'w').write(str(p.pid))
    return p

if mode != 'relaunched':
    spawn(tmp+'/Editor/Data/Resources/Licensing/Client/Unity.Licensing.Client', '--namedPipe', 'Unity-LicenseClient-test', name='licensing')
    spawn(tmp+'/Editor/Data/Resources/PackageManager/Server/UnityPackageManager', '-ipc', name='upm')
renderer = 'llvmpipe (LLVM 19.1.1, 256 bits)' if mode == 'software' else 'NVIDIA GeForce RTX 4060/PCIe/SSE2'
log.write('Renderer: ' + renderer + '\n')
if os.environ.get('FAKE_HUB'):
    hub = tmp + '/hub/unityhub-bin'
    spawn(hub, '--', '--silent', '--', '-projectPath', project, ignore_term=True, name='hub', code=(
        'import subprocess, sys\n'
        'c = subprocess.Popen([%r, "-c", "import time; time.sleep(600)", "--database=/x/unityhub/Crashpad"], executable=%r, start_new_session=True)\n'
        'open(%r, "w").write(str(c.pid))\n'
        'subprocess.Popen([%r, "-c", "import time; time.sleep(600)", "--type=zygote", "--crashpad-handler-pid=%%d" %% c.pid], executable=%r)\n'
    ) % (tmp + '/hub/chrome_crashpad_handler', python, tmp + '/crashpad.pid', hub, python))
    log.write("'/usr/bin/unityhub' '--' '--silent' '--' '-projectPath' '" + project + "'\n")
time.sleep(.3)
if mode in ('clean', 'relaunched'):
    time.sleep(float(os.environ.get('FAKE_DELAY', '0')))
    log.write('Cleanup mono\n')
    sys.exit(0)
if mode == 'hang-after-shutdown':
    signal.signal(signal.SIGTERM, signal.SIG_IGN)
    log.write('Cleanup mono\n')
if mode == 'handoff':
    env = dict(os.environ, FAKE_MODE='relaunched', FAKE_HUB='', FAKE_DELAY='1')
    me = open('/proc/self/cmdline', 'rb').read().split(b'\0')[0]
    subprocess.Popen([me, __file__, *args], executable=python, env=env, start_new_session=True)
    sys.exit(1)
time.sleep(600)
'''


class FakeEditorTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.addCleanup(self.cleanup)
        (self.root/'Editor').mkdir()
        (self.root/'hub').mkdir()
        self.script = self.root/'fake_editor.py'
        self.script.write_text(FAKE)
        self.editor = self.root/'Editor/Unity'
        self.editor.write_text(f'#!/bin/bash\nexec -a "$0" {sys.executable} {self.script} "$@"\n')
        self.editor.chmod(0o755)
        self.project = self.root/'projects'/'native1'
        self.project.mkdir(parents=True)
        self.out = self.root/'out'
        self.others = []

    def cleanup(self):
        for p in er.processes():
            if any(str(self.root).encode() in a for a in p.argv) or any(
                    a.startswith(str(self.root).encode()) for a in p.argv[:1]):
                os.kill(p.pid, signal.SIGKILL)
        for p in self.others:
            p.kill()
            p.wait()
        self.temp.cleanup()

    def pid(self, name):
        return int((self.root/(name+'.pid')).read_text())

    def alive(self, pid):
        return er.read_proc(pid) is not None

    def spec(self, mode, hub=True, **extra):
        os.environ.update(FAKE_MODE=mode, FAKE_ROOT=str(self.root), FAKE_HUB='1' if hub else '', FAKE_PYTHON=sys.executable)
        spec = {'label': 'native1', 'kind': 'editor-gpu', 'project': str(self.project),
                'log': str(self.root/'editor.log'), 'out': str(self.out), 'cwd': str(self.project),
                'command': [str(self.editor), '-projectPath',
                            str(self.project), '-logFile', str(self.root/'editor.log')],
                'timeout': 30, 'shutdown_grace': 1, 'kill_grace': 1, 'follow': 3, 'require_gpu': True}
        spec.update(extra)
        return spec

    def supervise(self, mode, **extra):
        return er.supervise(self.spec(mode, **extra), poll=.05)

    def test_clean_exit_sweeps_hub_and_spares_licensing(self):
        other = subprocess.Popen([str(self.root/'hub/unityhub-bin'), '-c', 'import time; time.sleep(600)',
                                  '--', '--silent', '--', '-projectPath', '/another/project'],
                                 executable=sys.executable)
        self.others.append(other)
        result = self.supervise('clean')
        self.assertEqual((result['exit_code'], result['forced_exit']), (0, None))
        self.assertTrue(result['renderer_ok'] and result['shutdown_logged'] and result['hub_launched'])
        # The Hub starts in the Editor's group: it is ended with the group's leftovers.
        swept = {r['pid']: r['signals'] for r in result['leftovers']+result['swept']}
        self.assertEqual(swept.get(self.pid('hub')), ['TERM', 'KILL'])  # the Hub ignores TERM
        self.assertIn(int((self.root/'crashpad.pid').read_text()), swept)
        self.assertFalse(self.alive(self.pid('hub')))
        self.assertTrue(self.alive(self.pid('licensing')))
        self.assertIsNone(other.poll(), "another project's Hub was signalled")
        self.assertEqual(json.loads((self.out/er.RESULT).read_text())['exit_code'], 0)
        self.assertIsNone(er.result_problem(self.out))

    def test_launch_receipt_precedes_editor_and_preserves_input_bytes(self):
        import base64
        import hashlib
        version = self.project/'ProjectVersion.txt'
        version.write_bytes(b'm_EditorVersion: selected\n')
        result = self.supervise('clean', hub=False, launch_inputs={
            'release': 'selected', 'paths': {'version': str(version)}})
        receipt = json.loads((self.out/'prelaunch-inputs.json').read_text())
        saved = receipt['files']['version']
        self.assertEqual(base64.b64decode(saved['bytes_base64']), version.read_bytes())
        self.assertEqual(saved['sha256'], hashlib.sha256(version.read_bytes()).hexdigest())
        self.assertEqual(receipt['selected_release'], 'selected')
        self.assertLessEqual(receipt['recorded'], (self.root/'editor.log').stat().st_mtime)
        self.assertEqual(result['exit_code'], 0)
        version.write_text('Editor mutation')
        self.assertEqual(base64.b64decode(saved['bytes_base64']), b'm_EditorVersion: selected\n')

    def test_shutdown_guard_kills_the_group_but_not_licensing(self):
        result = self.supervise('hang-after-shutdown')
        self.assertEqual(result['forced_exit'], 'shutdown-guard')
        self.assertEqual((result['exit_code'], result['signal']), (137, 'SIGKILL'))
        stopped = {r['pid']: r['signals'] for r in result['stopped']}
        self.assertEqual(stopped[self.pid('upm')], ['TERM'])
        self.assertIn('KILL', next(r['signals'] for r in result['stopped'] if 'Editor/Unity' in r['cmd']))
        self.assertNotIn(self.pid('licensing'), stopped)
        self.assertTrue(self.alive(self.pid('licensing')))
        self.assertFalse(self.alive(self.pid('upm')))
        self.assertFalse(self.alive(self.pid('hub')))
        self.assertGreaterEqual(result['times']['forced_s']-result['times']['shutdown_s'], 1)
        self.assertIsNone(er.result_problem(self.out))  # it logged its shutdown

    def test_watchdog(self):
        result = self.supervise('hang', timeout=1.5)
        self.assertEqual(result['forced_exit'], 'watchdog')
        self.assertEqual(result['exit_code'], 128+signal.SIGTERM)
        self.assertTrue(self.alive(self.pid('licensing')))
        self.assertIn('watchdog', er.result_problem(self.out))

    def test_probe_progress_timeout(self):
        result = self.supervise('hang', timeout=30,
                                progress_files=[str(self.root/'values.jsonl')], progress_timeout=.8)
        self.assertEqual(result['forced_exit'], 'no-progress')
        self.assertLess(result['times']['finished_s'], 10)
        self.assertIn('no-progress', er.result_problem(self.out))
        self.assertTrue(self.alive(self.pid('licensing')))

    def test_exited_owner_starts_no_editor(self):
        owner = er.read_proc(os.getpid())
        result = self.supervise('hang', owner_pid=owner.pid, owner_start_ticks=owner.start_ticks+1)
        self.assertEqual(result['forced_exit'], 'owner-exit')
        self.assertIsNone(result['exit_code'])
        self.assertFalse((self.root/'licensing.pid').exists())

    def test_owner_exit_stops_the_editor(self):
        owner = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(.7)'])
        self.others.append(owner)
        identity = er.read_proc(owner.pid)
        result = self.supervise('hang', owner_pid=owner.pid, owner_start_ticks=identity.start_ticks)
        self.assertEqual(result['forced_exit'], 'owner-exit')
        self.assertLess(result['times']['finished_s'], 10)
        self.assertTrue(self.alive(self.pid('licensing')))

    def test_private_display_is_inside_supervision(self):
        display = unittest.mock.Mock(name='private display')
        display.name = ':9876'
        with patch.object(er, 'Display', return_value=display) as create:
            result = self.supervise('clean', private_display=True, screen='64x64x24')
        create.assert_called_once_with('64x64x24')
        display.close.assert_called_once()
        self.assertEqual(result['display'], ':9876')

    def test_software_renderer_stops_early(self):
        result = self.supervise('software')
        self.assertEqual(result['forced_exit'], 'renderer')
        self.assertFalse(result['renderer_ok'])
        self.assertLess(result['times']['finished_s'], 10)
        self.assertIn('renderer', er.result_problem(self.out))

    def test_handoff_is_followed(self):
        result = self.supervise('handoff', hub=False)
        self.assertEqual(result['exit_code'], 1)
        self.assertEqual(len(result['followed']), 1)
        self.assertIn('exit_s', result['followed'][0])
        self.assertTrue(result['shutdown_logged'])
        self.assertIsNone(er.find_relaunch(self.project, set()))
        self.assertIn('exit with 0', er.result_problem(self.out))

    def test_late_sweep_and_run_through_launcher(self):
        # The runner side: a stand-in launcher drops --kind/--scope-id and runs the supervisor.
        launcher = self.root/'with-heavy-job'
        launcher.write_text('#!/bin/sh\n[ "$1" = --kind ] && [ "$3" = --scope-id ] || exit 99\n'
                            'echo "$2" > "$(dirname "$0")/kind"; shift 4; exec "$@"\n')
        launcher.chmod(0o755)
        saved = er.LAUNCHER
        er.LAUNCHER = launcher
        self.addCleanup(setattr, er, 'LAUNCHER', saved)
        os.environ.update(FAKE_MODE='clean', FAKE_ROOT=str(self.root), FAKE_HUB='', FAKE_PYTHON=sys.executable)
        job = er.EditorJob(self.project, self.root/'editor.log', self.out, graphics=False,
                           editor=self.editor, late_sweeps=(2,), timeout=30)
        self.assertEqual(job.kind, 'editor-cpu')
        self.assertIn('-nographics', job.command())
        result = er.run(job)
        self.assertEqual((result['exit_code'], result['launcher']['exit']), (0, 0))
        self.assertEqual((self.root/'kind').read_text().strip(), 'editor-cpu')
        self.assertGreaterEqual(result['times']['lease_wait_s'], 0)
        with self.assertRaises(SystemExit):
            er.run(job)  # write-once
        # A Hub that appears after the Editor ended is caught by the late sweep.
        hub = subprocess.Popen([str(self.root/'hub/unityhub-bin'), '-c',
                                'import signal, time; signal.signal(signal.SIGTERM, signal.SIG_IGN); time.sleep(600)',
                                '--', '--silent', '--', '-projectPath', str(self.project)], executable=sys.executable)
        self.others.append(hub)
        deadline = time.monotonic()+20
        while hub.poll() is None and time.monotonic() < deadline:
            time.sleep(.2)
        self.assertEqual(hub.returncode, -signal.SIGKILL)
        rows = [json.loads(line) for line in (self.out/'hub-sweeps.jsonl').read_text().splitlines()]
        self.assertEqual(rows[0]['swept'][0]['pid'], hub.pid)


class HubSelectionTest(unittest.TestCase):
    def test_reused_pid_is_not_signalled(self):
        before = er.Proc(123, 1, 123, 'R', [b'/Editor/Unity'], 10)
        replacement = er.Proc(123, 1, 123, 'R', [b'/Editor/Unity'], 11)
        with patch.object(er, 'read_proc', return_value=replacement), \
             patch.object(er.os, 'pidfd_open', return_value=999), \
             patch.object(er.os, 'close'), patch.object(er.signal, 'pidfd_send_signal') as send:
            self.assertFalse(er.alive(before))
            self.assertFalse(er.send(before, signal.SIGTERM))
        send.assert_not_called()

    def test_table(self):
        P = er.Proc
        project = b'/c/runs/p/projects/native1'
        table = [
            P(10, 1, 10, 'S', [b'/u/Editor/Unity', b'-projectPath', project]),
            P(11, 10, 10, 'S', [b'/u/Editor/Data/Resources/Licensing/Client/Unity.Licensing.Client']),
            P(20, 2, 10, 'S', [b'/usr/lib/unityhub/unityhub-bin', b'--', b'--silent', b'--', b'-projectPath', project]),
            P(21, 20, 10, 'S', [b'/usr/lib/unityhub/unityhub-bin', b'--type=zygote']),
            P(22, 21, 10, 'S', [b'/usr/lib/unityhub/unityhub-bin', b'--type=gpu-process', b'--crashpad-handler-pid=30']),
            P(30, 2, 30, 'S', [b'/usr/lib/unityhub/chrome_crashpad_handler', b'--database=/h/.config/unityhub/Crashpad']),
            P(40, 2, 40, 'S', [b'/usr/lib/unityhub/unityhub-bin']),  # the user's own Hub
            P(41, 40, 40, 'S', [b'/usr/lib/unityhub/unityhub-bin', b'--type=zygote']),
            P(50, 2, 50, 'S', [b'/usr/lib/unityhub/unityhub-bin', b'--', b'-projectPath', b'/c/runs/p/projects/native2']),
            P(60, 2, 60, 'S', [b'/bin/bash', b'/usr/bin/unityhub', b'--', b'--silent', b'--', b'-projectPath', project]),
        ]
        self.assertEqual([p.pid for p in er.hub_processes(project.decode(), table)], [20, 21, 22, 30, 60])
        # The project named through a symbolic link: the Hub names the path the Editor was given.
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        link = Path(temp.name)/'link'
        link.symlink_to('/c/runs/p/projects', target_is_directory=True)
        self.assertEqual(er.path_keys(link/'native1'), {str(link/'native1').encode(), project})
        self.assertEqual(sorted(p.pid for p in er.family(10, table)), [10, 20, 21, 22])

    def test_group_of_a_non_leader_is_not_taken(self):
        P = er.Proc
        table = [P(10, 5, 5, 'S', [b'/u/Editor/Unity']), P(11, 5, 5, 'S', [b'/bin/other']),
                 P(12, 10, 5, 'S', [b'/u/child'])]
        self.assertEqual(sorted(p.pid for p in er.family(10, table)), [10, 12])


class PublishTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.dirs = [self.root/'native1', self.root/'native2']
        for d in self.dirs:
            self.capture(d)
        self.target = self.root/'Reference'/'unity-test'
        self.written = []

    def capture(self, d, rows=('{"k":"x","v":1}', '{"k":"complete","v":true}'), **result):
        d.mkdir(exist_ok=True)
        (d/'values.jsonl').write_text(''.join(r+'\n' for r in rows))
        value = {'exit_code': 0, 'forced_exit': None, 'renderer': 'NVIDIA GeForce RTX 4060/PCIe/SSE2',
                 'shutdown_logged': True, 'require_gpu': True}
        value.update(result)
        (d/er.RESULT).write_text(json.dumps(value))

    def publish(self):
        return er.publish(self.dirs, self.target, lambda d: ['values.jsonl'],
                          lambda target, first: self.written.append(first), [er.jsonl_complete()])

    def refused(self, text):
        with self.assertRaises(SystemExit) as raised:
            self.publish()
        self.assertIn(text, str(raised.exception))
        self.assertEqual(self.written, [])

    def test_publishes_identical_complete_repeats(self):
        self.assertEqual(self.publish(), 0)
        self.assertEqual(self.written, [self.dirs[0]])

    def test_refusals(self):
        self.capture(self.dirs[1], rows=('{"k":"x","v":2}', '{"k":"complete","v":true}'))
        self.refused('differ')
        for d in self.dirs:
            self.capture(d, rows=('{"k":"x","v":1}',))
        self.refused('completion marker')
        for d in self.dirs:
            self.capture(d, exit_code=1)
        self.refused('exit with 0')
        self.capture(self.dirs[0], exit_code=143, forced_exit='watchdog', forced_detail='late')
        self.capture(self.dirs[1])
        self.refused('watchdog')
        self.capture(self.dirs[0], exit_code=137, forced_exit='shutdown-guard', shutdown_logged=False)
        self.refused('shutdown-guard')
        self.capture(self.dirs[0], renderer='llvmpipe')
        self.refused('NVIDIA')
        (self.dirs[0]/er.RESULT).unlink()
        self.refused('no Editor result')

    def test_guard_exit_after_shutdown_and_existing_target(self):
        self.capture(self.dirs[0], exit_code=137, forced_exit='shutdown-guard', shutdown_logged=True)
        self.target.mkdir(parents=True)
        self.refused('refusing to overwrite')
        self.target.rmdir()
        self.assertEqual(self.publish(), 0)

    def test_legacy_exit_files(self):
        d = self.root/'legacy'
        d.mkdir()
        (d/'exit.txt').write_text('0\n')
        (d/'editor.log').write_text('Renderer: NVIDIA GeForce RTX 4060/PCIe/SSE2\n')
        self.assertIsNone(er.result_problem(d))
        (d/'exit.txt').write_text('1\n')
        self.assertIn('exit with 0', er.result_problem(d))


class DisplayTest(unittest.TestCase):
    def test_private_xvfb(self):
        display = er.Display('64x64x24')
        try:
            self.assertRegex(display.name, r'^:\d+$')
            self.assertIsNone(display.server.poll())
        finally:
            display.close()
        self.assertIsNotNone(display.server.poll())


if __name__ == '__main__':
    unittest.main()
