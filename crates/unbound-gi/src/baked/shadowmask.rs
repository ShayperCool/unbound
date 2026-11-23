//! The shadowmask channels of a bake's mixed lights: each mixed light that casts shadows gets
//! one of four channels, lights whose reach overlaps getting different ones; the shadowmask
//! then keeps, per texel and channel, how much of the texel the channel's light sees (matches
//! the Unity 6000.0.71f1 Editor, tests/projects/bakedbuffers `Masks*`).

use super::light::{Kind, Light, Mode};

/// The shadowmask's channels.
pub const CHANNELS: usize = 4;

fn dot(a: [f32; 3], b: [f32; 3]) -> f32 {
    a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
}

fn sub(a: [f32; 3], b: [f32; 3]) -> [f32; 3] {
    [a[0] - b[0], a[1] - b[1], a[2] - b[2]]
}

fn cross(a: [f32; 3], b: [f32; 3]) -> [f32; 3] {
    [
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    ]
}

fn along(a: [f32; 3], b: [f32; 3], t: f32) -> [f32; 3] {
    [a[0] + b[0] * t, a[1] + b[1] * t, a[2] + b[2] * t]
}

/// The point of triangle (a, b, c) nearest to `p`.
fn nearest_on_triangle(p: [f32; 3], a: [f32; 3], b: [f32; 3], c: [f32; 3]) -> [f32; 3] {
    let (ab, ac, ap) = (sub(b, a), sub(c, a), sub(p, a));
    let (d1, d2) = (dot(ab, ap), dot(ac, ap));
    if d1 <= 0.0 && d2 <= 0.0 {
        return a;
    }
    let bp = sub(p, b);
    let (d3, d4) = (dot(ab, bp), dot(ac, bp));
    if d3 >= 0.0 && d4 <= d3 {
        return b;
    }
    let vc = d1 * d4 - d3 * d2;
    if vc <= 0.0 && d1 >= 0.0 && d3 <= 0.0 {
        return along(a, ab, d1 / (d1 - d3));
    }
    let cp = sub(p, c);
    let (d5, d6) = (dot(ab, cp), dot(ac, cp));
    if d6 >= 0.0 && d5 <= d6 {
        return c;
    }
    let vb = d5 * d2 - d1 * d6;
    if vb <= 0.0 && d2 >= 0.0 && d6 <= 0.0 {
        return along(a, ac, d2 / (d2 - d6));
    }
    let va = d3 * d6 - d5 * d4;
    if va <= 0.0 && (d4 - d3) >= 0.0 && (d5 - d6) >= 0.0 {
        return along(b, sub(c, b), (d4 - d3) / ((d4 - d3) + (d5 - d6)));
    }
    let denominator = 1.0 / (va + vb + vc);
    let (v, w) = (vb * denominator, vc * denominator);
    std::array::from_fn(|k| a[k] + ab[k] * v + ac[k] * w)
}

/// A spot light's reach: the square pyramid from the light along its axis to its range, as
/// wide there as its cone.
#[derive(Clone, Copy, Debug)]
struct Pyramid {
    apex: [f32; 3],
    /// Along the axis, at the range.
    far: [f32; 3],
    axis: [f32; 3],
    /// The far end's corners, in order around it.
    corners: [[f32; 3]; 4],
}

impl Pyramid {
    fn new(
        apex: [f32; 3],
        axis: [f32; 3],
        right: [f32; 3],
        up: [f32; 3],
        length: f32,
        half: f32,
    ) -> Pyramid {
        let far = along(apex, axis, length);
        let at = |a: f32, b: f32| -> [f32; 3] {
            std::array::from_fn(|k| far[k] + right[k] * a + up[k] * b)
        };
        Pyramid {
            apex,
            far,
            axis,
            corners: [
                at(-half, -half),
                at(half, -half),
                at(half, half),
                at(-half, half),
            ],
        }
    }

    /// The planes bounding it, as (a point of the plane, its outward normal): the far end and
    /// the four sides.
    fn planes(&self) -> [([f32; 3], [f32; 3]); 5] {
        let c = &self.corners;
        let side = |a: [f32; 3], b: [f32; 3]| -> ([f32; 3], [f32; 3]) {
            let n = cross(sub(a, self.apex), sub(b, self.apex));
            // Outward: away from the far end's centre.
            let n = if dot(n, sub(self.far, self.apex)) > 0.0 {
                n.map(|v| -v)
            } else {
                n
            };
            (self.apex, n)
        };
        [
            (self.far, self.axis),
            side(c[0], c[1]),
            side(c[1], c[2]),
            side(c[2], c[3]),
            side(c[3], c[0]),
        ]
    }

    /// The squared distance from a point to the pyramid (0 inside it).
    fn squared_distance(&self, p: [f32; 3]) -> f32 {
        if self
            .planes()
            .iter()
            .all(|(at, n)| dot(sub(p, *at), *n) <= 0.0)
        {
            return 0.0;
        }
        let c = &self.corners;
        let faces = [
            (self.apex, c[0], c[1]),
            (self.apex, c[1], c[2]),
            (self.apex, c[2], c[3]),
            (self.apex, c[3], c[0]),
            (c[0], c[1], c[2]),
            (c[0], c[2], c[3]),
        ];
        faces
            .iter()
            .map(|&(a, b, c)| {
                let q = sub(p, nearest_on_triangle(p, a, b, c));
                dot(q, q)
            })
            .fold(f32::INFINITY, f32::min)
    }
}

/// What a light's reach is tested by: a sphere around it and, for a spot light, its pyramid.
#[derive(Clone, Copy, Debug)]
struct Reach {
    centre: [f32; 3],
    radius: f32,
    pyramid: Option<Pyramid>,
}

impl Reach {
    fn of(light: &Light) -> Reach {
        if light.kind != Kind::Spot {
            return Reach {
                centre: light.position,
                radius: light.range,
                pyramid: None,
            };
        }
        let half_angle = light.cone_angle * 0.5;
        let (cos, sin) = (half_angle.cos(), half_angle.sin());
        let cot = cos / sin;
        let r = light.range;
        // The least sphere around the cone: for a cone up to 90 degrees wide one through its
        // apex and its rim, else one around its rim.
        let (offset, radius) = if !(cos > 0.0) {
            (0.0, r)
        } else if cot >= 1.0 {
            (r / (cos + cos), r / (cos + cos))
        } else {
            (cos * r, (sin * r).max(cos * r))
        };
        Reach {
            centre: along(light.position, light.forward, offset),
            radius,
            pyramid: Some(Pyramid::new(
                light.position,
                light.forward,
                light.right,
                cross(light.forward, light.right),
                r,
                r / cot,
            )),
        }
    }

    fn meets(&self, other: &Reach) -> bool {
        let d = sub(self.centre, other.centre);
        let both = self.radius + other.radius;
        if !(dot(d, d) < both * both) {
            return false;
        }
        match (&self.pyramid, &other.pyramid) {
            (None, None) => true,
            (Some(p), None) => p.squared_distance(other.centre) < other.radius * other.radius,
            (None, Some(p)) => p.squared_distance(self.centre) < self.radius * self.radius,
            (Some(a), Some(b)) => !separated(a, b) && !separated(b, a),
        }
    }
}

/// Whether one of `a`'s planes leaves all of `b` (its apex and corners) strictly outside.
fn separated(a: &Pyramid, b: &Pyramid) -> bool {
    let points = [
        b.apex,
        b.corners[0],
        b.corners[1],
        b.corners[2],
        b.corners[3],
    ];
    a.planes()
        .iter()
        .any(|(at, normal)| points.iter().all(|p| dot(sub(*p, *at), *normal) > 0.0))
}

/// The shadowmask channel of each light (by its place in `lights`, which are in the bake's
/// order): the mixed lights that cast shadows take the channels, directional ones first in
/// their order (when four have theirs, no other light gets one), then channel by channel each
/// other light in order that overlaps none already in the channel.
pub fn channels(lights: &[Light]) -> Vec<Option<u8>> {
    let mut out = vec![None; lights.len()];
    let mut pending = Vec::new();
    let mut taken = 0usize;
    for (i, light) in lights.iter().enumerate() {
        if light.mode == Mode::Mixed && light.casts_shadows {
            if light.kind == Kind::Directional {
                out[i] = Some(taken as u8);
                taken += 1;
            } else {
                pending.push(i);
            }
        }
        if taken >= CHANNELS {
            return out;
        }
    }
    let reaches: Vec<Reach> = pending.iter().map(|&i| Reach::of(&lights[i])).collect();
    for channel in taken..CHANNELS {
        let mut layer: Vec<usize> = Vec::new();
        for (k, &i) in pending.iter().enumerate() {
            if out[i].is_none() && layer.iter().all(|&j| !reaches[k].meets(&reaches[j])) {
                out[i] = Some(channel as u8);
                layer.push(k);
            }
        }
    }
    out
}

/// Whether a light falls back to being baked in the Shadowmask Lighting Mode: a mixed light
/// that casts shadows but has no channel.
pub fn falls_back(light: &Light, channel: Option<u8>) -> bool {
    light.mode == Mode::Mixed && light.casts_shadows && channel.is_none()
}

/// Gives the lights their channels ([`channels`]) and, in the Shadowmask Lighting Mode,
/// bakes the mixed lights left without one.
pub fn assign(lights: &mut [Light], shadowmask_mode: bool) -> Vec<Option<u8>> {
    let channels = channels(lights);
    if shadowmask_mode {
        for (light, &channel) in lights.iter_mut().zip(&channels) {
            if falls_back(light, channel) {
                light.mode = Mode::Baked;
            }
        }
    }
    channels
}

#[cfg(test)]
mod tests {
    use super::*;

    fn light(kind: Kind, position: [f32; 3], forward: [f32; 3], range: f32) -> Light {
        Light {
            kind,
            mode: Mode::Mixed,
            color: [1.0; 3],
            indirect_color: [1.0; 3],
            position,
            forward,
            right: if forward[1].abs() > 0.5 {
                [1.0, 0.0, 0.0]
            } else {
                [forward[2], 0.0, -forward[0]]
            },
            range,
            cone_angle: 30f32.to_radians(),
            casts_shadows: true,
        }
    }

    fn sun() -> Light {
        light(Kind::Directional, [0.0; 3], [0.0, -1.0, 0.0], 0.0)
    }

    fn point(x: f32) -> Light {
        light(Kind::Point, [x, 1.5, 0.0], [0.0, 0.0, 1.0], 3.0)
    }

    fn spot_down(x: f32) -> Light {
        light(Kind::Spot, [x, 3.0, 3.0], [0.0, -1.0, 0.0], 6.0)
    }

    #[test]
    fn suns_take_the_first_channels_and_four_take_them_all() {
        let lights = [point(0.0), sun(), sun(), point(20.0)];
        assert_eq!(channels(&lights), vec![Some(2), Some(0), Some(1), Some(2)]);
        // Four suns: no other light gets a channel, also one before them.
        let lights = [point(0.0), sun(), sun(), sun(), sun(), sun()];
        assert_eq!(
            channels(&lights),
            vec![None, Some(0), Some(1), Some(2), Some(3), None]
        );
    }

    #[test]
    fn overlapping_lights_take_different_channels_in_order_and_the_rest_none() {
        let row: Vec<Light> = (0..6).map(|k| point(k as f32 * 0.9)).collect();
        assert_eq!(
            channels(&row),
            vec![Some(0), Some(1), Some(2), Some(3), None, None]
        );
        // Only mixed lights that cast shadows take part.
        let mut quiet = point(0.0);
        quiet.casts_shadows = false;
        let mut baked = point(0.0);
        baked.mode = Mode::Baked;
        assert_eq!(
            channels(&[quiet, baked, point(0.0)]),
            vec![None, None, Some(0)]
        );
        assert!(!falls_back(&quiet, None) && !falls_back(&baked, None));
        assert!(falls_back(&point(0.0), None) && !falls_back(&point(0.0), Some(1)));
    }

    #[test]
    fn mixed_lights_left_without_a_channel_are_baked_in_the_shadowmask_mode_alone() {
        let row: Vec<Light> = (0..6).map(|k| point(k as f32 * 0.9)).collect();
        for shadowmask_mode in [true, false] {
            let mut lights = row.clone();
            let channels = assign(&mut lights, shadowmask_mode);
            assert_eq!(channels[4..], [None, None]);
            let modes: Vec<Mode> = lights.iter().map(|l| l.mode).collect();
            let left = if shadowmask_mode {
                Mode::Baked
            } else {
                Mode::Mixed
            };
            assert_eq!(modes, [[Mode::Mixed; 4].as_slice(), &[left; 2]].concat());
        }
    }

    #[test]
    fn spot_lights_overlap_by_their_pyramids() {
        // Side by side 6 m apart: their spheres meet, their narrow pyramids do not.
        assert_eq!(
            channels(&[spot_down(-3.0), spot_down(3.0)]),
            vec![Some(0), Some(0)]
        );
        // A point light beside one spot's pyramid, out of its reach, and one inside it.
        assert_eq!(
            channels(&[
                spot_down(-3.0),
                light(Kind::Point, [3.0, 1.5, 3.0], [0.0, 0.0, 1.0], 1.0)
            ]),
            vec![Some(0), Some(0)]
        );
        assert_eq!(
            channels(&[
                spot_down(-3.0),
                light(Kind::Point, [-3.0, 0.0, 3.0], [0.0, 0.0, 1.0], 1.0)
            ]),
            vec![Some(0), Some(1)]
        );
    }

    #[test]
    fn a_pyramid_is_at_no_distance_from_what_it_holds() {
        let p = Reach::of(&spot_down(0.0)).pyramid.unwrap();
        assert_eq!(p.squared_distance([0.0, 0.0, 3.0]), 0.0);
        // Straight beyond its far end, 6 m below the light: 1 m further.
        assert!((p.squared_distance([0.0, -4.0, 3.0]) - 1.0).abs() < 1e-4);
        // Behind its apex.
        assert!((p.squared_distance([0.0, 5.0, 3.0]) - 4.0).abs() < 1e-4);
    }
}
