import unittest
import compare


def row(event, frame=1, phase='Update.ScriptRunDelayedDynamicFrameRate', detail='', case='NullCoroutine'):
    return dict(case=case, event=event, frame=frame, phase=phase, detail=detail)


class ResidualBounds(unittest.TestCase):
    def test_renderer_phase_is_strict(self):
        expected = [row('camera.preCull', phase=compare.RENDER_PHASE)]
        actual = [row('camera.preCull', phase='PostLateUpdate.FinishFrameRendering')]
        self.assertNotEqual(compare.project_case(expected)[0], compare.project_case(actual)[0])

    def test_wrong_destroy_point_stays_a_failure(self):
        expected = [row('parent.first.OnDestroy', phase='PostLateUpdate.ScriptRunDelayedDynamicFrameRate')]
        actual = [row('parent.first.OnDestroy')]
        self.assertNotEqual(compare.project_case(expected)[0], compare.project_case(actual)[0])

    def test_particle_callbacks_and_ticks_are_strict(self):
        prefix = [row('returned', detail='scriptEnabled=false')]
        expected = compare.project_case(prefix)[0]
        self.assertNotEqual(expected, compare.project_case(prefix + [row('parent.first.OnParticleTrigger')])[0])
        ticks = lambda tick: compare.project_case(prefix + [row('after.particles', detail=f'particleTicks={tick}')])[0]
        self.assertNotEqual(ticks(1), ticks(2))

    def test_first_frame_visibility_is_strict(self):
        visible = [row('parent.first.OnBecameVisible', frame=0, phase=compare.RENDER_PHASE, case='delayFraction')]
        self.assertNotEqual(compare.project_case(visible)[0], compare.project_case(visible * 2)[0])

    def test_unload_requires_callbacks_before_completion(self):
        rows = [row('unload.return', detail='targetNull=false', case='unload'),
                row('unload.completed', frame=2, phase='EarlyUpdate.UpdatePreloading',
                    detail='targetNull=true', case='unload'),
                row('parent.first.OnDestroy', frame=2, phase='EarlyUpdate.UpdatePreloading', case='unload')]
        self.assertTrue(compare.project_case(rows)[1])

    def test_unload_requires_the_object_to_be_null_at_completion(self):
        rows = [row('unload.return', detail='targetNull=false', case='unload'),
                row('unload.completed', frame=2, phase='EarlyUpdate.UpdatePreloading',
                    detail='targetNull=false', case='unload')]
        self.assertTrue(compare.project_case(rows)[1])


if __name__ == '__main__':
    unittest.main()
