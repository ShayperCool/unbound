import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]/'physicsbatch'))
from compare import compare, load
from verify_geometry import verify
from verify_geometry import swap_child


class GeometryVerificationTests(unittest.TestCase):
    def result(self, expected, actual):
        return verify(expected, actual, compare(expected, actual))

    def test_motion_and_callback_counts_still_gate(self):
        key = 'geometry/static/1/0/2/0/bullet'
        expected = {key+'/body':'-.125,0,0,0,0,0,1,0,0,0,0,0,0', key+'/events':'0'}
        actual = dict(expected)
        actual[key+'/body'] = '1,0,0,0,0,0,1,100,0,0,0,0,0'
        self.assertTrue(self.result(expected, actual)['unresolved'])
        actual = dict(expected)
        actual[key+'/events'] = '1'
        self.assertTrue(self.result(expected, actual)['unresolved'])
        del actual[key+'/body']
        self.assertTrue(self.result(expected, actual)['unresolved'])

    def test_contact_trace_is_reported_but_cannot_be_truncated(self):
        key = 'geometry/static/1/0/3/0/bullet'
        expected = {key+'/events':'1', key+'/event/0':'enter,wall,1,1,0,0,-100,0,0',
                    key+'/event/0/0':'0,0,0,-1,0,0,.8,bullet,wall'}
        actual = dict(expected)
        actual[key+'/event/0/0'] = '0,.1,0,-1,0,0,.8,bullet,wall'
        result = self.result(expected, actual)
        self.assertFalse(result['unresolved'])
        self.assertEqual(1, len(result['contact_trace']))
        del actual[key+'/event/0/0']
        self.assertTrue(self.result(expected, actual)['unresolved'])
        del actual[key+'/event/0']
        self.assertTrue(self.result(expected, actual)['unresolved'])

    def test_reflection_requires_every_other_body_field(self):
        key = 'geometry/kinematic/2/1/3/0/target/body'
        expected = {key:'1,.1,0,0,0,0,1,68,5,0,0,0,0'}
        actual = {key:'1,-.1,0,0,0,0,1,68,-5,0,0,0,0'}
        self.assertEqual(1, len(self.result(expected, actual)['mirrored_motion']))
        actual[key] = '1,-.1,0,0,0,0,1,60,-5,0,0,0,0'
        self.assertTrue(self.result(expected, actual)['unresolved'])
        outside = 'geometry/kinematic/1/1/3/0/target/body'
        self.assertTrue(self.result({outside:expected[key]}, {outside:actual[key]})['unresolved'])

    def test_reflection_must_be_consistent_for_every_body_and_step(self):
        prefix = 'geometry/dynamic/3/0/3'
        a, b = prefix+'/0/a/body', prefix+'/0/b/body'
        expected = {a:'-.3,.1,0,0,0,0,1,36,3,0,0,0,0', b:'.3,-.1,0,0,0,0,1,-36,-3,0,0,0,0'}
        actual = dict(expected)
        actual[a] = '-.3,-.1,0,0,0,0,1,36,-3,0,0,0,0'
        self.assertTrue(self.result(expected, actual)['unresolved'])
        actual[b] = '.3,.1,0,0,0,0,1,-36,3,0,0,0,0'
        self.assertFalse(self.result(expected, actual)['unresolved'])
        expected[prefix+'/1/a/body'] = expected[a]
        actual[prefix+'/1/a/body'] = expected[a]
        self.assertTrue(self.result(expected, actual)['unresolved'])

    def test_matching_callback_fields_and_contact_identities_still_gate(self):
        key = 'geometry/static/1/0/2/0/bullet'
        header, point = key+'/event/0', key+'/event/0/0'
        expected = {key+'/events':'1', header:'enter,wall,1,1,0,0,-100,0,0',
                    point:'0,0,0,-1,0,0,0,bullet,wall'}
        for bad in ('exit,wall,1,1,0,0,-100,0,0',
                    'enter,wrong-collider,1,1,0,0,-100,0,0',
                    'enter,wall,1,1,0,0,-90,0,0'):
            actual = dict(expected); actual[header] = bad
            self.assertTrue(self.result(expected, actual)['unresolved'])
        actual = dict(expected); actual[point] = '0,0,0,-1,0,0,0,bullet,wrong-collider'
        self.assertTrue(self.result(expected, actual)['unresolved'])
        actual = dict(expected); actual[header] = 'enter,wall,0,1,0,0,-100,0,0'; del actual[point]
        self.assertTrue(self.result(expected, actual)['unresolved'])

    def capture(self):
        return load(Path(__file__).resolve().parent/'Reference/unity-6000.0.71f1/geometry.tsv')

    def test_open_compound_rejects_unbounded_and_frozen_motion(self):
        expected = self.capture()
        prefix = 'geometry/kinematic-target/4/2/3'
        actual = dict(expected)
        key = prefix+'/0/bullet/body'
        fields = actual[key].split(','); fields[0] = '-1000000'; actual[key] = ','.join(fields)
        self.assertTrue(self.result(expected, actual)['unresolved'])
        actual = dict(expected)
        for key in list(actual):
            if not key.startswith(prefix+'/'): continue
            if '/event/' in key: del actual[key]
            elif key.endswith('/events'): actual[key] = '0'
            elif key.endswith('/bullet/body'): actual[key] = '-1,0,0,0,0,0,1,0,0,0,0,0,0'
        self.assertTrue(self.result(expected, actual)['unresolved'])

    def test_open_compound_does_not_exempt_other_callback_phases_or_missing_steps(self):
        expected = self.capture()
        self.assertFalse(self.result(expected, expected)['unresolved'])
        prefix = 'geometry/kinematic-target/4/2/3'
        actual = dict(expected)
        key = prefix+'/0/bullet/event/0'
        actual[key] = actual[key].replace('enter,', 'stay,', 1)
        self.assertTrue(self.result(expected, actual)['unresolved'])
        actual = dict(expected)
        for key in list(actual):
            if key.startswith(prefix+'/3/bullet/event/'): del actual[key]
        actual[prefix+'/3/bullet/events'] = '0'
        self.assertTrue(self.result(expected, actual)['unresolved'])

    def test_convex_cardinality_exception_excludes_spheres_and_enter_callbacks(self):
        expected = self.capture()
        sphere = 'geometry/kinematic-target/4/3/3/0/wall/event/1'
        self.assertEqual('rear', expected[sphere+'/0'].split(',')[-1])
        actual = dict(expected)
        fields = actual[sphere].split(','); fields[2] = '4'; actual[sphere] = ','.join(fields)
        for i in range(1,4): actual[sphere+'/'+str(i)] = actual[sphere+'/0']
        self.assertTrue(self.result(expected, actual)['unresolved'])
        convex = 'geometry/kinematic-target/1/3/3/1/bullet/event/0'
        for count, accepted in ((3,True),(4,False)):
            actual = dict(expected)
            fields = actual[convex].split(','); fields[2] = str(count); actual[convex] = ','.join(fields)
            for i in range(2,count): actual[convex+'/'+str(i)] = actual[convex+'/0']
            self.assertEqual(not accepted, bool(self.result(expected, actual)['unresolved']))
        enter = 'geometry/kinematic-target/1/3/3/0/bullet/event/0'
        actual = dict(expected)
        fields = actual[enter].split(','); fields[2] = '3'; actual[enter] = ','.join(fields)
        actual[enter+'/2'] = actual[enter+'/0']
        self.assertTrue(self.result(expected, actual)['unresolved'])

    def test_child_mapping_is_coherent_across_steps_and_receivers(self):
        expected = self.capture()
        prefix = 'geometry/kinematic-target/2/3/2'
        def reflect(rows, selected):
            for key in list(rows):
                if not selected(key) or '/event/' not in key: continue
                fields = rows[key].split(',')
                if '/' not in key.rsplit('/event/',1)[1]:
                    fields[1] = swap_child(fields[1]); fields[7] = str(-float(fields[7]))
                else:
                    fields[-2:] = [swap_child(x) for x in fields[-2:]]
                rows[key] = ','.join(fields)
        actual = dict(expected)
        key = prefix+'/0/bullet/event/0'
        actual[key] = actual[key].replace('enter,front,','enter,rear,',1)
        self.assertTrue(self.result(expected, actual)['unresolved'])
        actual = dict(expected); reflect(actual, lambda k:k.startswith(prefix+'/0/bullet/'))
        self.assertTrue(self.result(expected, actual)['unresolved'])
        actual = dict(expected); reflect(actual, lambda k:k.startswith(prefix+'/0/'))
        self.assertTrue(self.result(expected, actual)['unresolved'])
        actual = dict(expected); reflect(actual, lambda k:k.startswith(prefix+'/'))
        self.assertFalse(self.result(expected, actual)['unresolved'])

    def test_one_mapping_must_cover_bodies_and_callbacks_together(self):
        expected = self.capture()
        actual = dict(expected)
        prefix = 'geometry/dynamic/3/2/3/'
        for key in actual:
            if key.startswith(prefix) and key.endswith('/body'):
                fields = actual[key].split(',')
                fields[1] = str(-float(fields[1])); fields[8] = str(-float(fields[8]))
                actual[key] = ','.join(fields)
        self.assertTrue(self.result(expected, actual)['unresolved'])
        for key in actual:
            if not key.startswith(prefix) or '/event/' not in key: continue
            fields = actual[key].split(',')
            if '/' not in key.rsplit('/event/',1)[1]:
                fields[1] = swap_child(fields[1]); fields[7] = str(-float(fields[7]))
            else:
                fields[-2:] = [swap_child(x) for x in fields[-2:]]
            actual[key] = ','.join(fields)
        self.assertFalse(self.result(expected, actual)['unresolved'])


if __name__ == '__main__':
    unittest.main()
