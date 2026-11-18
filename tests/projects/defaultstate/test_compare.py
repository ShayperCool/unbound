import json
from pathlib import Path
import re
import tempfile
import unittest
from unittest import mock

import run as capture_runner

import probe_paths
from compare import rows, entries, compare, check_constructor_stores, host_decided, matches, validate_controls, CONTROL_VALUES


def value(text):
    return {'status': 'value', 'value': text}


class ComparisonTests(unittest.TestCase):
    def test_publication_rejects_duplicate_or_aliased_captures(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root/'first').mkdir()
            (root/'alias').symlink_to(root/'first', target_is_directory=True)
            with mock.patch.object(capture_runner, 'WORK', root):
                for labels in [['first', 'first'], ['first', 'alias'], ['first', 'first', 'other']]:
                    with self.subTest(labels=labels), self.assertRaisesRegex(ValueError, 'distinct resolved'):
                        capture_runner.publish(labels)

    def test_recording_controls_enforce_isolation_and_inherited_state(self):
        capture = {key: value(text) for key, text in CONTROL_VALUES.items()}
        validate_controls(capture)
        capture['FreshStateControl/Property/FreshStateControl/ZReads'] = value('1')
        with self.assertRaises(ValueError): validate_controls(capture)
        capture = {key: value(text) for key, text in CONTROL_VALUES.items()}
        capture['FreshStateControl/Property/FreshStateControl/ZNested'] = value('NestedStateDerived{field=2}')
        with self.assertRaises(ValueError): validate_controls(capture)

    def test_exact_debt_does_not_accept_new_values_or_disappear(self):
        expected, actual = {'x': value('1')}, {'x': value('2')}
        debt = {'x': {'expected': value('1'), 'actual': value('2'), 'reason': 'queued'}}
        self.assertFalse(compare(expected, actual, debt)['failures'])
        self.assertEqual(len(compare(expected, {'x': value('3')}, debt)['failures']), 1)
        self.assertEqual(compare(expected, expected, debt)['stale'], ['x'])
        self.assertEqual(compare(expected, {}, debt)['missing'], ['x'])
        self.assertEqual(compare({}, actual, {})['extra'], ['x'])

    def test_variable_rows_preserve_other_fields_and_status(self):
        debt = {'x': {'expectedPattern': r'date:[0-9]+:Local', 'actualPattern': r'date:[0-9]+:Local', 'reason': 'clock'}}
        result = compare({'x': value('date:123:Local')}, {'x': value('date:456:Local')}, debt)
        self.assertFalse(result['failures'])
        self.assertEqual(len(result['differences']), 1)
        self.assertEqual(result['differences'][0]['actual'], value('date:456:Local'))
        self.assertTrue(compare({'x': value('date:123:Local')}, {'x': value('date:456:Utc')}, debt)['failures'])
        self.assertTrue(compare({'x': value('date:123:Local')}, {'x': {'status': 'getter-threw', 'value': 'date:456:Local'}}, debt)['failures'])

    def test_inactive_gradient_colors_are_the_only_variable_leaves(self):
        # The unused colors of inactive MinMaxGradients vary between Editor repeats (values like
        # uninitialized storage); their entries open exactly those Color leaves and keep the rest.
        root = Path(__file__).resolve().parent
        reference = rows(root/'Reference'/'unity-6000.0.71f1'/'values.jsonl')
        debt = entries(json.loads((root/'debt.json').read_text()), '6000.0.71f1')
        prefix = 'UnityEngine.ParticleSystem/Property/UnityEngine.ParticleSystem/'
        for name, garbage in [('colorBySpeed', 'r=NaN;g=-1.5E+26;b=3.6E-41;a=-33437'),
                              ('colorOverLifetime', 'r=-509083.5;g=NaN;b=0;a=3.39772839E-41'),
                              ('main', 'r=NaN;g=4.27970564E-41;b=0;a=NaN'),
                              ('trails', 'r=1.14859603E-30;g=0;b=NaN;a=3.48993383E-41')]:
            with self.subTest(row=name):
                key = prefix + name
                entry = debt[key]
                self.assertIn('expectedPattern', entry)
                expected = reference[key]
                self.assertTrue(matches(expected, entry, 'expected'))
                text = expected['value']
                # Active fields stay exact.
                for active in ('enabled=false', 'mode=enum:', 'alpha=1'):
                    if active in text:
                        changed = text.replace(active, active.replace('false', 'true').replace('1', '0.5')
                                               .replace('mode=enum:', 'mode=enum:9'), 1)
                        self.assertFalse(matches(value(changed), entry, 'expected'), active)
                # The opened Colors (color, colorMax, colorMin of colorBySpeed and
                # colorOverLifetime; the first colorMin, the third Color, of main and trails)
                # each accept other values; a closed one stays exact.
                colors = [m.span(1) for m in re.finditer(r'UnityEngine\.Color\{([^}]*)\}', text)]
                # All three Colors of colorBySpeed and colorOverLifetime are opened (none closed).
                count, opened, closed = {'colorBySpeed': (3, {0, 1, 2}, []), 'colorOverLifetime': (3, {0, 1, 2}, []),
                                         'main': (3, {2}, [0]), 'trails': (6, {2}, [5])}[name]
                self.assertEqual(len(colors), count)
                for index in sorted(opened) + closed:
                    start, end = colors[index]
                    changed = value(text[:start] + garbage + text[end:])
                    self.assertEqual(matches(changed, entry, 'expected'), index in opened, (name, index))
        terrain = debt['UnityEngine.Terrain/Property/UnityEngine.Terrain/freeUnusedRenderingResources']
        self.assertTrue(matches(value('false'), terrain, 'expected'))
        self.assertFalse(matches(value('1'), terrain, 'expected'))

    def test_invalid_debt_is_rejected(self):
        with self.assertRaises(ValueError): entries({'release': 'other', 'entries': []}, 'release')
        entry = {'key': 'x', 'reason': ''}
        with self.assertRaises(ValueError): entries({'release': 'r', 'entries': [entry]}, 'r')
        entry['reason'] = 'queued'
        with self.assertRaises(ValueError): entries({'release': 'r', 'entries': [entry, entry]}, 'r')

    def test_truncation_and_duplicate_rows_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)/'rows.jsonl'
            path.write_text(json.dumps(dict(key='x', **value('1')))+'\n')
            with self.assertRaises(ValueError): rows(path)
            path.write_text(path.read_text()*2+json.dumps(dict(key='complete', **value('1')))+'\n')
            with self.assertRaises(ValueError): rows(path)

    def test_host_decided_rows_take_this_hosts_measurement(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            def write(name, *records):
                (root/name).write_text(''.join(json.dumps(r)+'\n' for r in records))
            write('linux.jsonl', {'case': 'other', 'settings': {'mode': 9}}, {'case': 'new', 'settings': {'mode': 1}})
            write('mac.jsonl', {'case': 'new', 'settings': {'mode': 2}})
            entry = {'key': 'x', 'probe': 'p', 'file': 'f', 'record': {'case': 'new'}, 'field': ['settings', 'mode'],
                     'value': 'enum:{}', 'reason': 'host'}
            document = {'release': 'r', 'entries': [entry]}
            source = lambda probe, file, host: root/('mac.jsonl' if host else 'linux.jsonl')
            linux = lambda probe, file, host: root/'linux.jsonl'
            self.assertEqual(host_decided(document, 'r', {'x': value('enum:1')}, source), {'x': value('enum:2')})
            self.assertEqual(host_decided(document, 'r', {'x': value('enum:1')}, linux), {'x': value('enum:1')})
            # The Linux source must be the reference row it stands for.
            with self.assertRaisesRegex(ValueError, 'not its Linux source'):
                host_decided(document, 'r', {'x': value('enum:2')}, source)
            with self.assertRaisesRegex(ValueError, 'not its Linux source'):
                host_decided(document, 'r', {}, source)
            write('mac.jsonl', {'case': 'new', 'settings': {'mode': 2}}, {'case': 'new', 'settings': {'mode': 3}})
            with self.assertRaisesRegex(ValueError, '2 records match'):
                host_decided(document, 'r', {'x': value('enum:1')}, source)
            with self.assertRaises(ValueError): host_decided(document, 'other', {'x': value('enum:1')}, source)
            with self.assertRaises(ValueError):
                host_decided({'release': 'r', 'entries': [dict(entry, reason=' ')]}, 'r', {'x': value('enum:1')}, source)
            with self.assertRaisesRegex(ValueError, 'duplicate'):
                host_decided({'release': 'r', 'entries': [entry, entry]}, 'r', {'x': value('enum:1')}, linux)
            with self.assertRaisesRegex(ValueError, 'no field'):
                host_decided({'release': 'r', 'entries': [dict(entry, field=['settings', 'other'])]}, 'r',
                             {'x': value('enum:1')}, linux)

    def test_a_missing_macos_reference_fails_the_host_source(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root/'tests/projects/p/Reference/unity-6000.0.71f1').mkdir(parents=True)
            with mock.patch.object(capture_runner, 'REPO', root), mock.patch.object(probe_paths, 'MACOS_ARM64', True), \
                    mock.patch.object(capture_runner, 'MACOS_ARM64', True):
                linux = capture_runner.host_source('p', 'values.jsonl', False)
                self.assertEqual(linux, root/'tests/projects/p/Reference/unity-6000.0.71f1/values.jsonl')
                with self.assertRaisesRegex(ValueError, 'no macOS Editor reference'):
                    capture_runner.host_source('p', 'values.jsonl', True)
                (root/'tests/projects/p/Reference/unity-6000.0.71f1/metal').mkdir()
                (root/'tests/projects/p/Reference/unity-6000.0.71f1/metal/values.jsonl').write_text('')
                self.assertEqual(capture_runner.host_source('p', 'values.jsonl', True).parent.name, 'metal')

    def test_the_lightmapper_row_follows_the_host_editor(self):
        # Linux reads ProgressiveCPU (1); the macOS Editor ProgressiveGPU (2), from lightingassets.
        key = 'UnityEngine.LightingSettings/Property/UnityEngine.LightingSettings/lightmapper'
        for macos, wanted in ((False, 'enum:1'), (True, 'enum:2')):
            with self.subTest(macos=macos), mock.patch.object(probe_paths, 'MACOS_ARM64', macos), \
                    mock.patch.object(capture_runner, 'MACOS_ARM64', macos):
                expected, changed = capture_runner.expected_rows()
                self.assertEqual(expected[key], value(wanted))
                self.assertEqual([row['key'] for row in changed], [key] if macos else [])
                self.assertEqual(len(expected), len(rows(capture_runner.reference_root(capture_runner.ROOT)/'values.jsonl')))

    def test_constructor_debt_is_exact_and_detects_fixes(self):
        row = {'constructor': 'C()', 'field': 'x:int', 'expected': 'i4:1', 'actual': 'i4:2'}
        debt = [dict(row, reason='queued')]
        self.assertFalse(check_constructor_stores({'differences': [row]}, debt))
        self.assertTrue(check_constructor_stores({'differences': [dict(row, actual='i4:3')]}, debt))
        self.assertTrue(check_constructor_stores({'differences': []}, debt))


if __name__ == '__main__': unittest.main()
