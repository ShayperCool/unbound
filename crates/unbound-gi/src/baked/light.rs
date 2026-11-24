//! The lights of a bake and the light each sends to a point of a surface: directional, point
//! and spot lights of the built-in render pipeline, with its distance falloff and its spot
//! cone's falloff, both read from tables whose size and reading give the values of the Unity
//! 6000.0.71f1 Editor's Progressive CPU lightmapper (tests/projects/bakedbuffers).

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Kind {
    Directional,
    Point,
    Spot,
}

/// How a light takes part in the bake (Light Mode).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Mode {
    Mixed,
    Baked,
}

/// A light of the bake.
#[derive(Clone, Copy, Debug)]
pub struct Light {
    pub kind: Kind,
    pub mode: Mode,
    /// Its colour times its intensity, linear.
    pub color: [f32; 3],
    /// The same times its Indirect Multiplier.
    pub indirect_color: [f32; 3],
    pub position: [f32; 3],
    /// Where it points (its transform's forward axis), of unit length.
    pub forward: [f32; 3],
    /// Its transform's right axis, of unit length (a spot light's reach is turned by it).
    pub right: [f32; 3],
    pub range: f32,
    /// A spot light's whole cone angle, in radians.
    pub cone_angle: f32,
    pub casts_shadows: bool,
}

/// The samples of the distance falloff table.
const FALLOFF_SAMPLES: usize = 1024;
/// The length of the spot cone's falloff table.
const CONE_SAMPLES: usize = 128;
/// Where the built-in pipeline's distance falloff starts to fade to zero (of the squared
/// distance over the squared range), and its attenuation's quadratic factor.
const FADE_START: f32 = 0.8 * 0.8;
const QUADRATIC: f32 = 25.0;

/// The built-in pipeline's attenuation of a point or spot light at a distance over its range.
fn legacy_falloff(distance: f32) -> f32 {
    let d = distance.clamp(0.0, 1.0);
    let squared = d * d;
    let attenuation = 1.0 / (QUADRATIC * squared + 1.0);
    if squared < FADE_START {
        attenuation
    } else if squared <= 1.0 {
        attenuation * ((squared + -FADE_START) / -(1.0 - FADE_START) + 1.0)
    } else {
        0.0
    }
}

/// The built-in pipeline's spot attenuation from the cone's edge (0) to its axis (1), in 255ths:
/// it rises over the first 24 of 64 steps.
const CONE_STEPS: [u8; 24] = [
    0, 3, 10, 16, 24, 33, 45, 60, 76, 94, 113, 132, 152, 170, 187, 203, 216, 227, 235, 243, 247,
    251, 253, 254,
];

fn cone_step(index: usize) -> f32 {
    CONE_STEPS.get(index).map_or(1.0, |&v| v as f32 / 255.0)
}

/// What a spot light loses at `x` from the cone's edge (0) to its axis (1).
fn legacy_cone_loss(x: f32) -> f32 {
    let scaled = x.clamp(0.0, 1.0) * 63.0;
    let low = (scaled as usize).min(63);
    let fraction = scaled - low as f32;
    (1.0 - ((fraction * cone_step((low + 1).min(63))) as f64
        + cone_step(low) as f64 * (1.0 - fraction as f64))) as f32
}

/// The tables the bake's lights are evaluated with.
pub struct Tables {
    falloff: Vec<f32>,
    cone: Vec<f32>,
}

impl Default for Tables {
    fn default() -> Tables {
        Tables::new()
    }
}

impl Tables {
    pub fn new() -> Tables {
        let falloff = (0..FALLOFF_SAMPLES)
            .map(|k| legacy_falloff(k as f32 / (FALLOFF_SAMPLES - 1) as f32))
            .collect();
        // The cone's table is laid out by the reciprocal of the squared place in the cone, from
        // the axis (its last entry) to the edge.
        let n = CONE_SAMPLES as f64;
        let mut cone = vec![0f32; CONE_SAMPLES];
        for (k, entry) in cone.iter_mut().enumerate().take(CONE_SAMPLES - 1).skip(2) {
            let place = (((n - 1.0) / k as f64 + -1.0) / (n - 2.0)) as f32;
            *entry = legacy_cone_loss(place.sqrt());
        }
        cone[CONE_SAMPLES - 1] = 1.0;
        Tables { falloff, cone }
    }

    /// The distance falloff at a distance over the light's range.
    fn falloff(&self, distance: f32) -> f32 {
        let index = distance * FALLOFF_SAMPLES as f32;
        let low = (index as usize).min(FALLOFF_SAMPLES - 1);
        let high = (index as usize + 1).min(FALLOFF_SAMPLES - 1);
        let fraction = index - low as f32;
        (1.0 - fraction) * self.falloff[low] + fraction * self.falloff[high]
    }

    /// The spot cone's falloff at `place` from the cone's edge (0) to its axis (1).
    fn cone(&self, place: f32) -> f32 {
        let place = place.max(0.0);
        let index = (CONE_SAMPLES - 1) as f32 / (1.0 + place * place * (CONE_SAMPLES - 2) as f32);
        let low = (index as usize).min(CONE_SAMPLES - 1);
        let high = (index as usize + 1).min(CONE_SAMPLES - 1);
        let fraction = index - low as f32;
        1.0 - ((1.0 - fraction) * self.cone[low] + fraction * self.cone[high])
    }
}

/// What a light sends to a point.
#[derive(Clone, Copy, Debug)]
pub struct Arriving {
    /// The light's colour, attenuated and scaled by the surface's facing.
    pub color: [f32; 3],
    /// Towards the light, of unit length.
    pub direction: [f32; 3],
    /// How far the light is along `direction` (a directional light: beyond everything).
    pub distance: f32,
}

/// A directional light is this far away for its shadow rays.
pub const FAR: f32 = 1e27;
/// A point this near a light takes its full colour whatever it faces.
const AT_THE_LIGHT: f32 = 1e-19;

fn dot(a: [f32; 3], b: [f32; 3]) -> f32 {
    a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
}

impl Light {
    /// What the light sends to a point of a surface with the (shading) normal `normal`, before
    /// shadows; `None` where it sends nothing: behind the surface, beyond its range, outside
    /// its cone. `color` is the light's own or its indirect one.
    pub fn arriving(
        &self,
        tables: &Tables,
        color: [f32; 3],
        position: [f32; 3],
        normal: [f32; 3],
    ) -> Option<Arriving> {
        self.arriving_facing(tables, color, position, Some(normal))
    }

    /// What the light sends to a point that faces every way (a light probe's), before shadows:
    /// as [`Light::arriving`] without a surface to be behind or slanted to.
    pub fn arriving_around(
        &self,
        tables: &Tables,
        color: [f32; 3],
        position: [f32; 3],
    ) -> Option<Arriving> {
        self.arriving_facing(tables, color, position, None)
    }

    fn arriving_facing(
        &self,
        tables: &Tables,
        color: [f32; 3],
        position: [f32; 3],
        normal: Option<[f32; 3]>,
    ) -> Option<Arriving> {
        if self.kind == Kind::Directional {
            let direction = self.forward.map(|v| -v);
            let facing = normal.map_or(1.0, |n| dot(n, direction));
            // (Not a number is not above zero.)
            return (facing > 0.0).then(|| Arriving {
                color: color.map(|c| c * facing),
                direction,
                distance: FAR,
            });
        }
        let to_light = [
            self.position[0] - position[0],
            self.position[1] - position[1],
            self.position[2] - position[2],
        ];
        let distance = dot(to_light, to_light).sqrt();
        if !(distance < self.range) {
            return None;
        }
        if distance < AT_THE_LIGHT {
            let at = tables.falloff(0.0);
            return Some(Arriving {
                color: color.map(|c| c * at),
                direction: [0.0; 3],
                distance,
            });
        }
        let direction = to_light.map(|v| v / distance);
        let facing = normal.map_or(1.0, |n| dot(n, direction).max(0.0));
        if !(facing > 0.0) {
            return None;
        }
        let mut scale = facing * tables.falloff(distance / self.range);
        if self.kind == Kind::Spot {
            let cone = (self.cone_angle * 0.5).cos();
            let along = dot(direction, self.forward.map(|v| -v));
            if !(along >= cone) {
                return None;
            }
            scale *= tables.cone((along - cone) / (1.0 - cone));
        }
        Some(Arriving {
            color: color.map(|c| c * scale),
            direction,
            distance,
        })
    }
}
