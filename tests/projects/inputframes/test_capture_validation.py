import unittest
from input_capture_validation import keyboard_input_problem


class CaptureValidationTests(unittest.TestCase):
    def test_keyboard_press_is_a_delivery_witness(self):
        self.assertIsNone(keyboard_input_problem([
            {'k': 'event', 'type': 'STAT', 'device': 'Keyboard', 'keys': 'B'}]))

    def test_configuration_and_release_events_do_not_prove_delivery(self):
        self.assertIsNotNone(keyboard_input_problem([
            {'k': 'event', 'type': 'DCFG', 'device': 'Keyboard'},
            {'k': 'event', 'type': 'STAT', 'device': 'Keyboard', 'keys': ''}]))

    def test_mouse_input_and_an_empty_capture_are_rejected(self):
        self.assertIsNotNone(keyboard_input_problem([]))
        self.assertIsNotNone(keyboard_input_problem([
            {'k': 'event', 'type': 'STAT', 'device': 'Mouse', 'buttons': 1}]))

class PublisherValidationTests(unittest.TestCase):
    def test_matching_captures_without_a_keyboard_press_cannot_replace_the_reference(self):
        import json
        import tempfile
        from pathlib import Path
        from unittest.mock import patch
        import run
        with tempfile.TemporaryDirectory() as directory:
            work = Path(directory)
            for name in ('native1', 'native2'):
                capture = work / name / 'record.jsonl'
                capture.parent.mkdir()
                capture.write_text(json.dumps({'k': 'event', 'type': 'DCFG', 'device': 'Keyboard'}) + '\n')
            reference = work / 'reference.jsonl'
            reference.write_text('original reference\n')
            with patch.object(run, 'reference_file', return_value=reference), patch.object(run.subprocess, 'run') as compare:
                with self.assertRaisesRegex(SystemExit, 'no keyboard key-down'):
                    run.publish(work)
                compare.assert_not_called()
            self.assertEqual('original reference\n', reference.read_text())

    def test_replay_rejects_a_reference_without_a_keyboard_press(self):
        import json
        import tempfile
        from pathlib import Path
        from unittest.mock import patch
        import run
        with tempfile.TemporaryDirectory() as directory:
            work = Path(directory)
            reference = work / 'reference.jsonl'
            reference.write_text(json.dumps({'k': 'event', 'type': 'STAT', 'device': 'Mouse', 'buttons': 1}) + '\n')
            with patch.object(run, 'reference_file', return_value=reference), patch.object(run, 'copy_project') as copy, patch.object(run.subprocess, 'run', side_effect=AssertionError('reference must be checked before starting a process')):
                with self.assertRaisesRegex(SystemExit, 'no keyboard key-down'):
                    run.unbound(work)
                copy.assert_not_called()


if __name__ == '__main__':
    unittest.main()
