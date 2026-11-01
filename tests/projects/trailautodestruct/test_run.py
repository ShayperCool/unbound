"""Check fixture delivery and incomplete-output rejection without starting an engine."""
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import runpy
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import validation_cache


class CommandCaptured(Exception):
    pass


class RunnerTests(unittest.TestCase):
    def setUp(self):
        cache = validation_cache()/'runs'
        cache.mkdir(parents=True, exist_ok=True)
        temporary = tempfile.TemporaryDirectory(prefix='trail-runner-test-', dir=cache)
        self.addCleanup(temporary.cleanup)
        self.work = Path(temporary.name).resolve()
        self.cli = self.work/'unused-cli'
        self.cli.write_text('This file is hashed, never executed.\n')
        self.runtime = self.work/'unused-runtime'
        self.runtime.mkdir()
        self.output = self.work/'case/engine'

    def invoke(self, launch):
        argv = [str(ROOT/'run.py'), 'engine', '--release', '6000.0.71f1',
                '--work', str(self.work), '--label', 'case',
                '--cli', str(self.cli), '--runtime', str(self.runtime)]
        with patch.object(sys, 'argv', argv), patch.object(sys, 'path', list(sys.path)), \
                patch.dict(os.environ), \
                patch('subprocess.run', side_effect=launch), \
                patch('subprocess.Popen', side_effect=AssertionError('no child process allowed')), \
                contextlib.redirect_stdout(io.StringIO()):
            runpy.run_path(str(ROOT/'run.py'), run_name='__main__')

    def test_delivers_shader_metadata_and_original_sources_before_launch(self):
        reference = ROOT/'Reference/unity-6000.0.71f1.jsonl'
        original_reference = reference.read_bytes()

        def inspect(command, **kwargs):
            project = self.output/'project'
            inputs = json.loads((self.output/'inputs.json').read_text())
            sources = {
                'Assets/Resources/LineColor.shader': ROOT.parent/'lines/Assets/Resources/LineColor.shader',
                'Assets/Resources/LineColor.shader.meta': ROOT.parent/'lines/Assets/Resources/LineColor.shader.meta',
                'Assets/Scripts/TrailAutodestructProbe.cs': ROOT/'Sources/TrailAutodestructProbe.cs',
                'Assets/Editor/ProbeRunner.cs': ROOT/'Sources/ProbeRunner.cs',
            }
            for relative, source in sources.items():
                with self.subTest(relative=relative):
                    self.assertEqual((project/relative).read_bytes(), source.read_bytes())
                    self.assertEqual(inputs[relative], hashlib.sha256(source.read_bytes()).hexdigest())
            self.assertEqual(json.loads((project/'Packages/manifest.json').read_text()), {'dependencies': {}})
            self.assertEqual(kwargs['env']['TRAIL_AUTODESTRUCT_OUT'], str(self.output/'events.jsonl'))
            raise CommandCaptured

        with self.assertRaises(CommandCaptured):
            self.invoke(inspect)
        self.assertEqual(reference.read_bytes(), original_reference)

    def test_graphical_command_has_room_for_fifty_steps_and_one_launcher(self):
        def inspect(command, **kwargs):
            self.assertEqual(command[:5], [str(Path.home()/'.cache/compat/with-heavy-job'),
                                          'timeout', '--kill-after=15', '180', str(self.cli.resolve())])
            self.assertEqual(command[5:], ['run', '-p', str(self.output/'project'),
                                           '--frames', '100', '--size', '128x128', '--no-watch'])
            self.assertEqual(command.count(str(Path.home()/'.cache/compat/with-heavy-job')), 1)
            raise CommandCaptured

        with self.assertRaises(CommandCaptured):
            self.invoke(inspect)

    def test_successful_process_with_only_completion_marker_is_rejected(self):
        def incomplete(command, **kwargs):
            Path(kwargs['env']['TRAIL_AUTODESTRUCT_OUT']).write_text('{"complete":true}\n')
            return SimpleNamespace(returncode=0)

        with self.assertRaises(AssertionError):
            self.invoke(incomplete)
        self.assertEqual(json.loads((self.output/'exit.json').read_text()), {'exit': 0})
        self.assertEqual((self.output/'events.jsonl').read_text(), '{"complete":true}\n')


if __name__ == '__main__':
    unittest.main()
