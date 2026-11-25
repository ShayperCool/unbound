//! Baked light probes: the light at each probe's point as L2 spherical harmonics, the direct
//! light of the bake's lights and the light that reaches the point from other surfaces and the
//! sky (matches the Unity 6000.0.71f1 Editor's Progressive CPU lightmapper,
//! tests/projects/bakedbuffers `Probes*`).

use super::grid::Grid;
use super::light::{Light, Mode, Tables};
use super::sampling::{self, Samples};
use super::trace::{Ray, Scene};

/// The coefficients of a probe's light: nine, each red, green and blue.
pub type Coefficients = [[f32; 3]; 9];

/// The nine real L2 spherical harmonics' constant factors.
const K0: f32 = 0.282_094_8;
const K1: f32 = 0.488_602_5;
const K2: f32 = 1.092_548_4;
const K20: f32 = 0.946_174_7;
const K20_OFFSET: f32 = 0.315_391_57;
const K22: f32 = 0.546_274_2;

/// Where the bake puts a probe at `point` (in world space): each coordinate cut to whole
/// hundredths of a metre towards zero (in single precision: the hundredths times 0.01).
pub fn snap(point: [f32; 3]) -> [f32; 3] {
    point.map(|v| (v * 100.0) as i32 as f32 * 0.01)
}

/// Adds light of `color` arriving from `direction` (of unit length, towards where it comes
/// from) to `out`, projected on the harmonics and times pi.
pub fn add(out: &mut Coefficients, color: [f32; 3], direction: [f32; 3]) {
    let [x, y, z] = direction;
    let basis = [
        K0,
        x * K1,
        y * K1,
        z * K1,
        x * y * K2,
        y * z * K2,
        (z * z * K20) - K20_OFFSET,
        x * z * K2,
        (x * x - y * y) * K22,
    ];
    for (coefficient, b) in out.iter_mut().zip(basis) {
        for (o, c) in coefficient.iter_mut().zip(color) {
            *o += c * b * std::f32::consts::PI;
        }
    }
}

/// How light probes take the lights they sample (soft shadows, area lights): over the probes'
/// Direct Samples, each probe's samples by the Sobol sequence turned by its place in its
/// group's frame (its index in the group, and the group's size), without blue noise.
pub struct Sampling<'a> {
    pub samples: &'a Samples,
    pub count: u32,
    /// Each probe's index in its group and the group's size.
    pub places: Vec<(usize, usize)>,
}

impl<'a> Sampling<'a> {
    /// The places of probes in groups of these sizes, the groups in order.
    pub fn new(samples: &'a Samples, count: u32, groups: impl IntoIterator<Item = usize>) -> Self {
        let places = groups
            .into_iter()
            .flat_map(|size| (0..size).map(move |k| (k, size.max(1))))
            .collect();
        Sampling {
            samples,
            count: count.max(1),
            places,
        }
    }

    /// The two numbers of a probe's sample.
    pub fn numbers(&self, probe: usize, index: u32) -> [f32; 2] {
        let (k, width) = self.places[probe];
        let rotation = self.samples.rotation(k, 0, width, 0);
        [
            sampling::fraction(self.samples.sobol(index, 0) + rotation[0]),
            sampling::fraction(self.samples.sobol(index, 1) + rotation[1]),
        ]
    }
}

/// Whether a light is sampled: area lights, and lights with soft shadows.
pub fn is_sampled(light: &Light) -> bool {
    light.kind.is_area() || (light.casts_shadows && light.shadow_size > 0.0)
}

/// What a sampled light sends a probe at `p` for a sample of `u`, if it sees it: its colour
/// and the direction it comes from (a soft shadow's ray aimed at a point of its disk, an area
/// light's at a point of its surface).
pub fn sampled_light(
    light: &Light,
    tables: &Tables,
    color: [f32; 3],
    p: [f32; 3],
    u: [f32; 2],
    casters: &Scene,
) -> Option<([f32; 3], [f32; 3])> {
    let arriving = light.sampled_around(tables, color, p, u)?;
    let direction = if light.kind.is_area() {
        arriving.direction
    } else {
        light.jittered(&arriving, p, u)
    };
    let shadowed = light.casts_shadows
        && casters.occluded_inclusive(
            &Ray {
                origin: p,
                direction,
            },
            arriving.distance,
        );
    (!shadowed).then_some((arriving.color, direction))
}

/// The direct light of the bake's baked lights at each probe's point: every directional light
/// and the lights of the point's region of the light grid, unshadowed from the point (which
/// faces every way). A mixed light's direct light is in no probe, in any Lighting Mode (in the
/// Subtractive mode too: it is realtime for the objects the probes light; tests/projects/
/// bakedbuffers `ProbesMore`, `ProbesSky`); one baked instead (in the Shadowmask mode, without
/// a channel) is taken as a baked light (not measured).
pub fn direct(
    positions: &[[f32; 3]],
    lights: &[Light],
    grid: &Grid,
    casters: &Scene,
    sampled: Option<&Sampling>,
) -> Vec<Coefficients> {
    let tables = Tables::new();
    positions
        .iter()
        .enumerate()
        .map(|(probe, &p)| {
            let mut out = Coefficients::default();
            let mut take = |index: usize| {
                let light = &lights[index];
                if light.mode != Mode::Baked {
                    return;
                }
                // A sampled light: the mean of its samples.
                if let Some(sampling) = sampled.filter(|_| is_sampled(light)) {
                    let weight = 1.0 / sampling.count as f32;
                    for i in 0..sampling.count {
                        let u = sampling.numbers(probe, i);
                        if let Some((color, direction)) =
                            sampled_light(light, &tables, light.color, p, u, casters)
                        {
                            add(&mut out, color.map(|c| c * weight), direction);
                        }
                    }
                    return;
                }
                let Some(arriving) = light.arriving_around(&tables, light.color, p) else {
                    return;
                };
                let shadowed = light.casts_shadows
                    && casters.occluded_inclusive(
                        &Ray {
                            origin: p,
                            direction: arriving.direction,
                        },
                        arriving.distance,
                    );
                if !shadowed {
                    add(&mut out, arriving.color, arriving.direction);
                }
            };
            for &light in &grid.directional {
                take(light);
            }
            if let Some(region) = grid.region(p) {
                for &light in &region.lights {
                    take(light);
                }
            }
            out
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    use crate::baked::grid::Bounds;
    use crate::baked::light::Kind;

    #[test]
    fn a_probe_is_baked_at_its_point_cut_to_hundredths_towards_zero() {
        // (-0.4 - 0.13 is a little above -0.53 in single precision: it is cut to -0.52.)
        let moved = -0.4f32 + -0.13f32;
        assert_eq!(snap([2.0700002, -2.0900002, moved]), [2.07, -2.09, -0.52]);
        // Hundredths times 0.01: 0.1 is the float just below 0.1, 1.5 and 0 stay.
        assert_eq!(
            snap([0.1, 1.5, 0.0]),
            [f32::from_bits(0x3dcc_cccc), 1.5, 0.0]
        );
        assert_eq!(
            snap([-0.239, 0.4567, -0.0051]).map(|v| (v * 100.0).round()),
            [-23.0, 45.0, -0.0]
        );
    }

    fn sun(forward: [f32; 3]) -> Light {
        Light {
            kind: Kind::Directional,
            mode: Mode::Baked,
            color: [2.0, 1.0, 0.5],
            indirect_color: [2.0, 1.0, 0.5],
            position: [0.0; 3],
            forward,
            right: [1.0, 0.0, 0.0],
            range: 0.0,
            cone_angle: 0.0,
            casts_shadows: true,
            shadow_size: 0.0,
            up: [0.0, 1.0, 0.0],
            size: [0.0; 2],
        }
    }

    fn bake(lights: &[Light], scene: &Scene) -> Coefficients {
        let grid = Grid::new(
            lights,
            Bounds {
                min: [-1.0; 3],
                max: [1.0; 3],
            },
        );
        direct(&[[0.0; 3]], lights, &grid, scene, None)[0]
    }

    #[test]
    fn light_from_one_direction_projects_on_the_harmonics() {
        // A sun shining down: its light comes from +y.
        let c = bake(&[sun([0.0, -1.0, 0.0])], &Scene::new([]));
        let pi = std::f32::consts::PI;
        let near = |a: f32, b: f32| (a - b).abs() < 1e-5;
        assert!(near(c[0][0], 2.0 * K0 * pi) && near(c[0][2], 0.5 * K0 * pi));
        // The y term alone of the first order; the zz term at its constant.
        assert!(near(c[2][0], 2.0 * K1 * pi) && c[1][0] == 0.0 && c[3][0] == 0.0);
        assert!(near(c[6][0], -2.0 * K20_OFFSET * pi));
        assert!(near(c[8][0], -2.0 * K22 * pi));
    }

    #[test]
    fn a_probe_takes_the_baked_lights_unless_shadowed() {
        // A mixed light's direct light is in no probe (tests/projects/bakedbuffers ProbesMore:
        // a mixed spot light in the Subtractive Lighting Mode).
        let mut mixed = sun([0.0, -1.0, 0.0]);
        mixed.mode = Mode::Mixed;
        assert_eq!(bake(&[mixed], &Scene::new([])), Coefficients::default());
        assert_ne!(
            bake(&[sun([0.0, -1.0, 0.0])], &Scene::new([])),
            Coefficients::default()
        );
        // A roof above the probe shadows the sun.
        let roof = Scene::new([
            [[-5.0, 2.0, -5.0], [5.0, 2.0, -5.0], [5.0, 2.0, 5.0]],
            [[-5.0, 2.0, -5.0], [5.0, 2.0, 5.0], [-5.0, 2.0, 5.0]],
        ]);
        assert_eq!(
            bake(&[sun([0.0, -1.0, 0.0])], &roof),
            Coefficients::default()
        );
    }
}
