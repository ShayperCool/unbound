import math
import unittest
from compare import differences, solver_roundoff


class SolverRoundoffTest(unittest.TestCase):
    def sample(self, state=0, angle=15, speed=0):
        return {'body': {'av': speed, 'p': [1, 2]},
                'properties': {'state': state, 'angle': angle, 'min': -10, 'max': 15},
                'events': []}

    def classified(self, expected, actual, key='hinge.case0.35'):
        return solver_roundoff(key, expected, actual, list(differences(expected, actual)))

    def test_small_angular_residual_is_reported_separately(self):
        self.assertTrue(self.classified(self.sample(speed=.0003), self.sample()))

    def test_limit_state_can_only_vary_at_its_own_boundary(self):
        self.assertTrue(self.classified(self.sample(state=0), self.sample(state=2)))
        self.assertFalse(self.classified(self.sample(state=0, angle=14), self.sample(state=2, angle=14)))
        self.assertFalse(self.classified(self.sample(state=0), self.sample(state=1)))

    def test_equal_limits_state_is_not_ignored(self):
        self.assertFalse(self.classified(self.sample(state=0), self.sample(state=3)))

    def test_larger_angular_error_fails(self):
        self.assertFalse(self.classified(self.sample(speed=.002), self.sample()))

    def test_nonfinite_velocity_fails(self):
        self.assertFalse(self.classified(self.sample(speed=math.nan), self.sample()))

    def test_position_error_fails_even_with_tiny_angular_residual(self):
        a, b = self.sample(speed=.0003), self.sample()
        b['body']['p'][0] += .001
        self.assertFalse(self.classified(a, b))

    def test_missing_break_callback_fails(self):
        a, b = self.sample(speed=.0003), self.sample()
        a['events'] = ['HingeJoint2D:True:CallbackOnly']
        self.assertFalse(self.classified(a, b))

    def test_missing_record_fails(self):
        self.assertFalse(self.classified(self.sample(), 'MISSING'))

    def test_api_and_other_joint_errors_fail(self):
        self.assertFalse(self.classified(self.sample(speed=.0003), self.sample(), 'hinge.defaults'))
        self.assertFalse(self.classified(self.sample(speed=.0003), self.sample(), 'spring.case0.35'))

    def test_wheel_relative_speed_retains_its_body_error_bound(self):
        a, b = self.sample(speed=43), self.sample(speed=43.001)
        a['other'] = {'av': 42.35}; b['other'] = {'av': 42.349}
        a['properties']['speed'] = -.65; b['properties']['speed'] = -.652
        self.assertTrue(self.classified(a, b, 'wheel.case5.35'))
        b['properties']['speed'] = -.7
        self.assertFalse(self.classified(a, b, 'wheel.case5.35'))

    def test_wheel_position_errors_are_never_classified(self):
        a, b = self.sample(speed=43), self.sample(speed=43.001)
        b['body']['p'][0] += .001
        self.assertFalse(self.classified(a, b, 'wheel.case5.35'))


if __name__ == '__main__':
    unittest.main()
