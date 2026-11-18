#!/usr/bin/env python3
"""tests/guarded_editor.py's checks of an Editor result, command and compile fixture, on both
OSes' branches (the guarded runners' controls mock only the Linux Editor)."""
from pathlib import Path
import sys
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import guarded_editor  # noqa: E402

RELEASE = '6000.0.71f1'
MAC_EDITOR = f'/Applications/Unity/Hub/Editor/{RELEASE}/Unity.app/Contents/MacOS/Unity'
LINUX_EDITOR = f'/home/u/Unity/Hub/Editor/{RELEASE}/Editor/Unity'


def windowed(editor, prefix=()):
    return [*prefix, editor, '-projectPath', '/p', '-executeMethod', 'ProbeRunner.Record', '-logFile', '/l']


class MacOS(unittest.TestCase):
    def setUp(self):
        patch = mock.patch.object(guarded_editor, 'MACOS_ARM64', True)
        patch.start()
        self.addCleanup(patch.stop)

    def result(self, **change):
        result = {'device': 'metal', 'renderer': 'Metal: Apple M2 Pro', 'renderer_ok': True, 'kind': 'editor-gpu',
                  'require_gpu': True, 'exit_code': 0, 'forced_exit': None, 'command': windowed(MAC_EDITOR)}
        result.update(change)
        return result

    def test_the_metal_editor_passes(self):
        self.assertEqual(guarded_editor.result_problems(self.result()), [])
        self.assertEqual(guarded_editor.command_problems(windowed(MAC_EDITOR), RELEASE), [])

    def test_another_renderer_or_device_is_refused(self):
        for change in ({'renderer': 'NVIDIA GeForce RTX 4060/PCIe/SSE2'}, {'device': None}, {'renderer_ok': None}):
            self.assertTrue(any('not the Metal device' in p for p in guarded_editor.result_problems(self.result(**change))),
                            change)
        self.assertTrue(guarded_editor.result_problems(self.result(kind='editor-cpu')))

    def test_another_command_is_refused(self):
        other = MAC_EDITOR.replace(RELEASE, '6000.0.70f1')
        self.assertTrue(any('is not release' in p for p in guarded_editor.command_problems(windowed(other), RELEASE)))
        for flag in ('-batchmode', '-quit', '-nographics'):
            self.assertTrue(guarded_editor.command_problems(windowed(MAC_EDITOR)+[flag], RELEASE), flag)
        # The Linux command is not the macOS Editor's.
        self.assertTrue(guarded_editor.command_problems(windowed(LINUX_EDITOR, ['vglrun', '-d', 'egl']), RELEASE))

    def test_a_batch_fixture_of_this_copy_passes_and_others_are_refused(self):
        batch = [MAC_EDITOR, '-batchmode', '-projectPath', '/copy', '-quit', '-logFile', '/l']
        self.assertEqual(guarded_editor.batch_fixture_problems(self.result(command=batch), '/copy'), [])
        self.assertTrue(guarded_editor.batch_fixture_problems(self.result(command=batch), '/other'))
        self.assertTrue(guarded_editor.batch_fixture_problems(self.result(command=windowed(MAC_EDITOR)), '/p'))
        self.assertTrue(guarded_editor.batch_fixture_problems(self.result(command=batch, renderer='llvmpipe'), '/copy'))

    def test_the_data_directory_follows_the_editor_that_runs(self):
        with mock.patch.dict('os.environ', {'UNBOUND_PROBE_UNITY': '/x/Unity.app/Contents/MacOS/Unity'}):
            self.assertEqual(guarded_editor.editor_data(RELEASE), Path('/x/Unity.app/Contents'))
        with mock.patch.dict('os.environ', {}, clear=True):
            self.assertEqual(guarded_editor.editor_data(RELEASE), Path(MAC_EDITOR).parents[1])


class Linux(unittest.TestCase):
    def setUp(self):
        patch = mock.patch.object(guarded_editor, 'MACOS_ARM64', False)
        patch.start()
        self.addCleanup(patch.stop)

    def test_the_nvidia_editor_through_virtualgl_passes(self):
        result = {'renderer': 'NVIDIA GeForce RTX 4060/PCIe/SSE2', 'renderer_ok': True, 'kind': 'editor-gpu',
                  'require_gpu': True, 'launcher': {'exit': 0}}
        self.assertEqual(guarded_editor.result_problems(result), [])
        self.assertTrue(any('not the NVIDIA GPU' in p for p in
                            guarded_editor.result_problems({**result, 'renderer': 'llvmpipe', 'renderer_ok': False})))
        self.assertTrue(any('launcher exit' in p for p in guarded_editor.result_problems({**result, 'launcher': {'exit': 1}})))
        command = windowed(LINUX_EDITOR, ['vglrun', '-d', 'egl'])
        self.assertEqual(guarded_editor.command_problems(command, RELEASE), [])
        self.assertTrue(any('not a vglrun EGL command' in p for p in guarded_editor.command_problems(command[3:], RELEASE)))

    def test_the_data_directory_follows_the_editor_that_runs(self):
        with mock.patch.dict('os.environ', {'UNBOUND_PROBE_UNITY': '/x/Editor/Unity'}):
            self.assertEqual(guarded_editor.editor_data(RELEASE), Path('/x/Editor/Data'))


if __name__ == '__main__':
    unittest.main()
