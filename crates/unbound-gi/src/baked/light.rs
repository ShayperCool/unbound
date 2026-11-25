//! The lights of a bake and the light each sends to a point of a surface: directional, point
//! and spot lights of the built-in render pipeline, with its distance falloff and its spot
//! cone's falloff, both read from tables whose size and reading give the values of the Unity
//! 6000.0.71f1 Editor's Progressive CPU lightmapper (tests/projects/bakedbuffers).

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Kind {
    Directional,
    Point,
    Spot,
    /// A rectangle area light (baked only): `size` its width and height.
    Rectangle,
    /// A disc area light (baked only): `size[0]` its radius.
    Disc,
}

impl Kind {
    /// An area light: its light is sampled over its surface.
    pub fn is_area(self) -> bool {
        matches!(self, Kind::Rectangle | Kind::Disc)
    }
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
    /// Where its baked shadows are soft: its Baked Shadow Radius (a point or spot light) or
    /// Angle in radians (a directional light); 0 where they are hard.
    pub shadow_size: f32,
    /// Its transform's up axis, of unit length (an area light's height runs along it).
    pub up: [f32; 3],
    /// An area light's width and height (a disc's radius first).
    pub size: [f32; 2],
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

impl Light {
    /// The radius of the disk a soft shadow's rays aim at, at the light (a directional
    /// light's one unit away: its angle in radians, tests/projects/bakedbuffers `SoftSun`,
    /// `SoftMixed`); 0 for hard shadows.
    pub fn shadow_radius(&self) -> f32 {
        self.shadow_size
    }

    /// The direction a soft shadow's ray of the sample at `position` takes towards the light
    /// (`arriving`'s), aimed at a point of a disk around it facing the sample: the direction
    /// times the light's distance (1 for a directional light) plus the disk point of `rnd` (a
    /// point of the unit square, mapped to the unit disk) times the radius, in a basis around
    /// the direction; of unit length.
    pub fn jittered(&self, arriving: &Arriving, position: [f32; 3], rnd: [f32; 2]) -> [f32; 3] {
        let l = arriving.direction;
        let distance = match self.kind {
            Kind::Directional => 1.0,
            _ if arriving.distance == FAR => {
                let d: [f32; 3] = std::array::from_fn(|k| self.position[k] - position[k]);
                (d[0] * d[0] + d[1] * d[1] + d[2] * d[2]).sqrt()
            }
            _ => arriving.distance,
        };
        let radius = self.shadow_radius();
        let [dx, dy] = super::sampling::disk(rnd);
        let (b1, b2) = super::sampling::basis(l);
        let v: [f32; 3] = std::array::from_fn(|k| {
            l[k] * distance + (b1[k] * (radius * dx) + b2[k] * (radius * dy))
        });
        let length = (v[0] * v[0] + v[1] * v[1] + v[2] * v[2]).sqrt();
        v.map(|x| x / length)
    }
}
/// The Editor's pi and smallest distance of a light sample.
const PI: f32 = 3.141_592_7;
const EPSILON: f32 = 1e-19;

fn unit(v: [f32; 3]) -> [f32; 3] {
    let l = dot(v, v).sqrt();
    v.map(|x| x / l)
}

fn cross(a: [f32; 3], b: [f32; 3]) -> [f32; 3] {
    [
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    ]
}

fn unit_cross(a: [f32; 3], b: [f32; 3]) -> [f32; 3] {
    cross(a, b)
}

/// A point of a rectangle (its corner `s` and its sides `ex`, `ey`) sampled uniformly over the
/// solid angle it covers seen from `o`, for a point `uv` of the unit square, and that solid
/// angle (Urena, Fajardo and King's spherical rectangle sampling).
fn spherical_rectangle(
    s: [f32; 3],
    ex: [f32; 3],
    ey: [f32; 3],
    o: [f32; 3],
    uv: [f32; 2],
) -> ([f32; 3], f32) {
    let (u, v) = (uv[0], uv[1]);
    let exl = dot(ex, ex).sqrt();
    let eyl = dot(ey, ey).sqrt();
    let x = ex.map(|c| c / exl);
    let y = ey.map(|c| c / eyl);
    let mut z = cross(x, y);
    let d: [f32; 3] = std::array::from_fn(|k| s[k] - o[k]);
    let mut z0 = dot(d, z);
    if z0 > 0.0 {
        z = z.map(|c| -c);
        z0 = -z0;
    }
    let z0sq = z0 * z0;
    let x0 = dot(d, x);
    let y0 = dot(d, y);
    let x1 = x0 + exl;
    let y1 = y0 + eyl;
    let y0sq = y0 * y0;
    let y1sq = y1 * y1;
    let v00 = [x0, y0, z0];
    let v01 = [x0, y1, z0];
    let v10 = [x1, y0, z0];
    let v11 = [x1, y1, z0];
    let n0 = unit(cross(v00, v10));
    let n1 = unit(cross(v10, v11));
    let n2 = unit(cross(v11, v01));
    let n3 = unit(cross(v01, v00));
    let g0 = (-dot(n0, n1)).acos();
    let g1 = (-dot(n1, n2)).acos();
    let g2 = (-dot(n2, n3)).acos();
    let g3 = (-dot(n3, n0)).acos();
    let b0 = n0[2];
    let b1 = n2[2];
    let b0sq = b0 * b0;
    let k = 2.0 * PI - g2 - g3;
    let solid_angle = g0 + g1 - k;
    let au = u * solid_angle + k;
    let fu = (au.cos() * b0 - b1) / au.sin();
    let cu = (1.0 / (fu * fu + b0sq).sqrt() * if fu > 0.0 { 1.0 } else { -1.0 }).clamp(-1.0, 1.0);
    let xu = (-(cu * z0) / (1.0 - cu * cu).sqrt()).clamp(x0, x1);
    let d_ = (xu * xu + z0sq).sqrt();
    let h0 = y0 / (d_ * d_ + y0sq).sqrt();
    let h1 = y1 / (d_ * d_ + y1sq).sqrt();
    let hv = h0 + v * (h1 - h0);
    let hv2 = hv * hv;
    let yv = if hv2 < 1.0 - 0.0001 {
        (hv * d_) / (1.0 - hv2).sqrt()
    } else {
        y1
    };
    (
        std::array::from_fn(|c| o[c] + xu * x[c] + yv * y[c] + z0 * z[c]),
        solid_angle,
    )
}

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

    /// What the light sends to a point of a surface for a sample of `rnd` (a point of the
    /// unit square): an area light's from the point of its surface the sample picks
    /// ([`Light::area_arriving`]), any other light's as [`Light::arriving`].
    pub fn sampled(
        &self,
        tables: &Tables,
        color: [f32; 3],
        position: [f32; 3],
        normal: [f32; 3],
        rnd: [f32; 2],
    ) -> Option<Arriving> {
        if self.kind.is_area() {
            self.area_arriving(color, position, normal, rnd)
        } else {
            self.arriving(tables, color, position, normal)
        }
    }

    /// What an area light sends to a point of a surface from the point of its own surface a
    /// sample picks: a rectangle's by its solid angle as seen from the point (sampled
    /// uniformly over that solid angle: [`spherical_rectangle`]) times the facing over pi; a
    /// disc's from a point sampled uniformly over its area, by the facing, the disc's own
    /// facing, its area over pi and the squared distance. Nothing behind the light, beyond its
    /// range, or where the sample fails. (The light emits along its forward axis; its height
    /// runs along its up axis.)
    pub fn area_arriving(
        &self,
        color: [f32; 3],
        position: [f32; 3],
        normal: [f32; 3],
        rnd: [f32; 2],
    ) -> Option<Arriving> {
        let to_point: [f32; 3] = std::array::from_fn(|k| position[k] - self.position[k]);
        // (The light faces where its forward axis points.)
        if dot(self.forward, to_point) < 0.0 {
            return None;
        }
        if dot(to_point, to_point) > self.range * self.range {
            return None;
        }
        let tangent = unit(self.up);
        let (direction, distance, factor) = if self.kind == Kind::Rectangle {
            let back = self.forward.map(|v| -v);
            let bitangent = unit_cross(back, tangent);
            let [width, height] = self.size;
            let corner: [f32; 3] = std::array::from_fn(|k| {
                self.position[k] - 0.5 * width * bitangent[k] - 0.5 * height * tangent[k]
            });
            let (point, solid_angle) = spherical_rectangle(
                corner,
                tangent.map(|v| v * height),
                bitangent.map(|v| v * width),
                position,
                rnd,
            );
            let v: [f32; 3] = std::array::from_fn(|k| point[k] - position[k]);
            let distance = dot(v, v).sqrt();
            if solid_angle.is_nan() || !(solid_angle > 0.0) || !(distance >= EPSILON) {
                return None;
            }
            let direction = v.map(|x| x / distance);
            let facing = dot(direction, normal).max(0.0);
            (direction, distance, solid_angle * facing / PI)
        } else {
            let radius = self.size[0];
            let across = unit_cross(self.forward, tangent);
            let r = rnd[0].sqrt();
            let theta = 2.0 * PI * rnd[1];
            let local = [theta.cos() * r * radius, theta.sin() * r * radius];
            let point: [f32; 3] = std::array::from_fn(|k| {
                self.position[k] + local[0] * tangent[k] + local[1] * across[k]
            });
            let v: [f32; 3] = std::array::from_fn(|k| point[k] - position[k]);
            let distance = dot(v, v).sqrt();
            if !(distance >= EPSILON) {
                return None;
            }
            let direction = v.map(|x| x / distance);
            let facing = dot(direction, normal).max(0.0);
            let towards = -dot(self.forward, direction);
            (
                direction,
                distance,
                facing * radius * radius * towards / (distance * distance),
            )
        };
        Some(Arriving {
            color: color.map(|c| c * factor),
            direction,
            distance,
        })
    }

    fn arriving_facing(
        &self,
        tables: &Tables,
        color: [f32; 3],
        position: [f32; 3],
        normal: Option<[f32; 3]>,
    ) -> Option<Arriving> {
        if self.kind.is_area() {
            // (An area light is sampled: [`Light::sampled`].)
            return None;
        }
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
