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

    def test_particle_allowance_is_one_pending_frame_only(self):
        prefix = [row('after.particles', frame=0, phase=compare.PARTICLE_PHASE, detail='particleTicks=1 particles=1'),
                  row('returned', detail='scriptEnabled=false')]
        def view(tick):
            return compare.project_case(prefix + [row('after.particles', phase=compare.PARTICLE_PHASE,
                                                      detail=f'particleTicks={tick} particles=1')])[0]
        self.assertEqual(view(1), view(2))
        self.assertNotEqual(view(2), view(3))
        self.assertNotEqual(compare.project_case([row('after.particles', phase=compare.PARTICLE_PHASE,
                                                     detail='particleTicks=1 particles=1')])[0], view(2))

    def test_unexpected_particle_callback_phase_is_not_filtered(self):
        prefix = [row('returned', detail='scriptEnabled=false')]
        expected = compare.project_case(prefix)[0]
        self.assertNotEqual(expected, compare.project_case(prefix + [row('parent.first.OnParticleTrigger')])[0])

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
