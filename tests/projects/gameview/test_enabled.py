import base64
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('enabled_gameview_runner', Path(__file__).with_name('run.py'))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class EnabledReferenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.probe = self.root/'probe'
        files = {}
        for key, part in {'assets':'Assets', 'packages':'Packages', 'settings':'ProjectSettings'}.items():
            path = self.probe/part/('ProjectVersion.txt' if key=='settings' else 'input')
            path.parent.mkdir(parents=True)
            data = b'm_EditorVersion: test\n' if key=='settings' else b'authored input'
            path.write_bytes(data)
            files[key+'/'+path.name] = {'sha256':hashlib.sha256(data).hexdigest(),
                                      'bytes_base64':base64.b64encode(data).decode()}
        self.work = self.root/'work'
        self.capture = (Path(__file__).parent/'Reference/unity-6000.0.71f1-enabled-resize.jsonl').read_bytes()
        self.receipt = {'selected_release':'test', 'files':files}
        for name in ('native1','native2'):
            out = self.work/name;out.mkdir(parents=True)
            (out/'values.jsonl').write_bytes(self.capture)
            (out/'prelaunch-inputs.json').write_text(json.dumps(self.receipt))
        self.target = self.root/'Reference/enabled.jsonl'
        self.patches = [patch.object(runner,'PROBE',self.probe),
                        patch.object(runner,'enabled_reference',return_value=self.target),
                        patch.dict(runner.os.environ,{'UNBOUND_PROBE_UNITY_VERSION':'test'}),
                        patch('editor_runner.result_problem',return_value=None),
                        patch('editor_runner.load_result',return_value={'forced_exit':None})]
        for item in self.patches:item.start()

    def tearDown(self):
        for item in reversed(self.patches):item.stop()
        self.temp.cleanup()

    def test_identical_bound_repeats_publish_once(self):
        self.assertEqual(0,runner.publish_enabled(self.work))
        self.assertEqual(self.capture,self.target.read_bytes())
        with self.assertRaises(FileExistsError):runner.publish_enabled(self.work)

    def test_bad_sources_release_and_receipt_bytes_fail_before_publication(self):
        source = self.work/'native1/prelaunch-inputs.json'
        for modification in ('release','hash','bytes'):
            receipt = json.loads(json.dumps(self.receipt))
            if modification=='release':receipt['selected_release']='other'
            elif modification=='hash':receipt['files']['assets/input']['sha256']='0'*64
            else:receipt['files']['assets/input']['bytes_base64']=base64.b64encode(b'wrong input').decode()
            source.write_text(json.dumps(receipt))
            with self.assertRaises(RuntimeError):runner.publish_enabled(self.work)
            self.assertFalse(self.target.exists())

    def test_wrong_keys_disagreeing_repeats_and_forced_exit_fail(self):
        path=self.work/'native2/values.jsonl'
        for data in (self.capture.replace(b'enabled.after.cameraEnabled',b'wrong.key'),
                     self.capture.replace(b'"v":[320,320]',b'"v":[321,320]'),
                     self.capture.replace(b'"v":true',b'"v":false'),
                     self.capture.replace(b'"v":[320,320]',b'"v":[NaN,320]')):
            self.assertNotEqual(self.capture,data)
            path.write_bytes(data)
            with self.assertRaises(RuntimeError):runner.publish_enabled(self.work)
            self.assertFalse(self.target.exists())
        path.write_bytes(self.capture)
        with patch('editor_runner.load_result',return_value={'forced_exit':'shutdown-guard'}):
            with self.assertRaises(RuntimeError):runner.publish_enabled(self.work)
        self.assertFalse(self.target.exists())
