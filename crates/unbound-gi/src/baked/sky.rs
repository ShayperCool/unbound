//! The sky of a bake: the environment's light as six square images around the scene, and how
//! a surface takes light from it. A sample of a surface looks at the sky in one direction,
//! drawn over the hemisphere around its normal by the cosine, or, with the Lighting Settings'
//! environment importance sampling, half of the time among directions picked beforehand
//! where the sky is bright (the two ways weighed against each other by how likely each was to
//! give the direction). Read as the Unity 6000.0.71f1 Editor's Progressive CPU lightmapper
//! reads it (tests/projects/bakedbuffers).
use super::indirect::Texture;
use super::sampling;
use std::f32::consts::PI;

/// Directions picked where the sky is bright.
pub struct Importance {
    /// Unit directions into the sky.
    pub directions: Vec<[f32; 3]>,
    /// The sky's light in each direction over how likely the direction was to be picked.
    pub weighted: Vec<[f32; 3]>,
    /// The sky's brightness (its largest channel) summed over the sphere.
    pub integral: f32,
}

/// The generator of the picked directions: a 32-bit xorshift over four words, started from 0.
struct Shift {
    words: [u32; 4],
}

impl Shift {
    fn new() -> Shift {
        let mut words = [0u32; 4];
        for k in 1..4 {
            words[k] = words[k - 1].wrapping_mul(1_812_433_253).wrapping_add(1);
        }
        Shift { words }
    }

    /// The next number of 0..=1, in steps of 1 / (2^23 - 1).
    fn next(&mut self) -> f32 {
        let [x, y, z, w] = self.words;
        let t = x ^ (x << 11);
        let next = w ^ (w >> 19) ^ t ^ (t >> 8);
        self.words = [y, z, w, next];
        (next & 0x7f_ffff) as f32 * (1.0 / 8_388_607.0)
    }
}

/// How many directions are picked.
pub const IMPORTANCE_DIRECTIONS: usize = 2048;
/// How often a direction is drawn before one is taken whatever the sky's brightness there.
const DRAWS: u32 = 100_000;

/// The solid angle seen from the cube's centre between its face's centre and the point
/// (x, y) of the face, of -1..1.
fn corner_angle(x: f32, y: f32) -> f32 {
    (x * y).atan2((x * x + y * y + 1.0).sqrt())
}

impl Importance {
    /// Picks `count` directions over the sky by its brightness: directions drawn evenly over
    /// the sphere are kept by the chance of the sky's brightness there over its greatest. A
    /// black sky has none.
    pub fn of(faces: &[Texture; 6], count: usize) -> Option<Importance> {
        let side = faces[0].width;
        if side == 0 || faces.iter().any(|f| f.width != side || f.height != side) {
            return None;
        }
        // The sky's brightness over the sphere: each texel's by the solid angle it covers.
        let step = 1.0 / side as f32;
        let (mut integral, mut greatest) = (0f32, 0f32);
        for face in faces {
            for row in 0..side {
                let y = ((row as f32 + 0.5) * 2.0) * step - 1.0;
                for column in 0..side {
                    let x = ((column as f32 + 0.5) * 2.0) * step - 1.0;
                    let texel = face.texels[row * side + column];
                    let bright = brightness([texel[0], texel[1], texel[2]]);
                    let angle = corner_angle(x - step, y - step)
                        - corner_angle(x - step, y + step)
                        - corner_angle(x + step, y - step)
                        + corner_angle(x + step, y + step);
                    greatest = greatest.max(bright);
                    integral += angle * bright;
                }
            }
        }
        if !(integral > 0.0) {
            return None;
        }
        let mut shift = Shift::new();
        let mut draw = |shift: &mut Shift| -> ([f32; 3], [f32; 3]) {
            let first = shift.next();
            let z = (1.0 - first) - first;
            let radius = (1.0 - z * z).max(0.0).sqrt();
            let angle = shift.next() as f64 * std::f64::consts::TAU;
            let direction = [
                angle.cos() as f32 * radius,
                -(angle.sin() as f32) * radius,
                z,
            ];
            (direction, mean_of_faces(faces, side, direction))
        };
        let (mut directions, mut weighted) = (Vec::with_capacity(count), Vec::with_capacity(count));
        for _ in 0..count {
            let mut picked = None;
            for _ in 0..DRAWS {
                let (direction, color) = draw(&mut shift);
                if shift.next() * greatest <= brightness(color) {
                    picked = Some((direction, color));
                    break;
                }
            }
            let (direction, color) = picked.unwrap_or_else(|| draw(&mut shift));
            let scale = integral / brightness(color);
            directions.push(direction);
            weighted.push(color.map(|v| scale * v));
        }
        Some(Importance {
            directions,
            weighted,
            integral,
        })
    }
}

/// The sky in a direction as the picking reads it: on each face the direction's line crosses
/// within the face's square, the four texels around the point (texel centres at the square's
/// edges), and the mean of the faces (more than one only along the cube's edges).
fn mean_of_faces(faces: &[Texture; 6], side: usize, d: [f32; 3]) -> [f32; 3] {
    // Per axis: which components run along the face's columns and rows, and per face their
    // signs.
    const ALONG: [(usize, usize); 3] = [(2, 1), (0, 2), (0, 1)];
    const COLUMN_SIGN: [f32; 6] = [-1.0, 1.0, 1.0, 1.0, 1.0, -1.0];
    const ROW_SIGN: [f32; 6] = [-1.0, -1.0, 1.0, -1.0, -1.0, -1.0];
    let mut sum = [0f32; 3];
    let mut count = 0f32;
    let last = (side - 1) as f32;
    for axis in 0..3 {
        let c = d[axis];
        if c == 0.0 {
            continue;
        }
        let face = 2 * axis + usize::from(c <= 0.0);
        let u = (COLUMN_SIGN[face] * d[ALONG[axis].0] / c.abs() + 1.0) * 0.5;
        let v = (ROW_SIGN[face] * d[ALONG[axis].1] / c.abs() + 1.0) * 0.5;
        if !((0.0..=1.0).contains(&u) && (0.0..=1.0).contains(&v)) {
            continue;
        }
        let (x, y) = (u * last, v * last);
        let (x0, y0, x1, y1) = (x.floor(), y.floor(), x.ceil(), y.ceil());
        let texel =
            |column: f32, row: f32| faces[face].texels[row as usize * side + column as usize];
        let (a, b, c0, e) = (texel(x0, y0), texel(x1, y0), texel(x0, y1), texel(x1, y1));
        let (tx, ty) = (x - x0, y - y0);
        for k in 0..3 {
            let top = (b[k] - a[k]) * tx + a[k];
            let bottom = (e[k] - c0[k]) * tx + c0[k];
            sum[k] += (bottom - top) * ty + top;
        }
        count += 1.0;
    }
    sum.map(|v| v / count.max(1.0))
}

/// The environment's light.
pub struct Sky {
    /// The faces towards +x, -x, +y, -y, +z and -z.
    pub faces: [Texture; 6],
    pub importance: Option<Importance>,
}

fn dot(a: [f32; 3], b: [f32; 3]) -> f32 {
    a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
}

fn brightness(color: [f32; 3]) -> f32 {
    color[0].max(color[1]).max(color[2])
}

/// How much of two ways to draw a direction the first gets, by their densities.
fn share(first: f32, second: f32) -> f32 {
    let both = first + second;
    if both > 0.0 { first / both } else { 0.0 }
}

impl Sky {
    /// The sky's light from a direction.
    pub fn color(&self, direction: [f32; 3]) -> [f32; 3] {
        let length = dot(direction, direction).sqrt();
        let d = direction.map(|v| v / length);
        let a = d.map(f32::abs);
        let (face, u, v) = if a[0] > a[1] && a[0] > a[2] {
            if d[0] > 0.0 {
                (0, -d[2] / a[0], -d[1] / a[0])
            } else {
                (1, d[2] / a[0], -d[1] / a[0])
            }
        } else if a[1] > a[2] {
            if d[1] > 0.0 {
                (2, d[0] / a[1], d[2] / a[1])
            } else {
                (3, d[0] / a[1], -d[2] / a[1])
            }
        } else if d[2] > 0.0 {
            (4, d[0] / a[2], -d[1] / a[2])
        } else {
            (5, -d[0] / a[2], -d[1] / a[2])
        };
        let texel = self.faces[face].at([u / 2.0 + 0.5, v / 2.0 + 0.5]);
        [texel[0], texel[1], texel[2]]
    }

    /// The direction a sample of a surface looks at the sky in, and the light it takes from
    /// there if nothing is in the way: for the surface's shading normal (not necessarily of
    /// unit length: it is used as given), the normal of its triangle, and three numbers of
    /// 0..1. None where the direction lies behind the triangle.
    pub fn arriving(
        &self,
        normal: [f32; 3],
        triangle: [f32; 3],
        numbers: [f32; 3],
    ) -> Option<([f32; 3], [f32; 3])> {
        let (t, b) = sampling::basis(normal);
        let around = |local: [f32; 3]| -> [f32; 3] {
            std::array::from_fn(|k| local[0] * t[k] + local[1] * b[k] + local[2] * normal[k])
        };
        let Some(importance) = &self.importance else {
            let direction = around(sampling::cosine_direction([numbers[0], numbers[1]]));
            return (dot(direction, triangle) > 0.0).then(|| (direction, self.color(direction)));
        };
        if numbers[2] > 0.5 {
            let count = importance.directions.len();
            if count == 0 {
                return None;
            }
            let index = (sampling::fraction(numbers[0]) * count as f32) as usize % count;
            let direction = importance.directions[index];
            let cosine = dot(direction, normal).max(0.0);
            let by_cosine = cosine / PI;
            let by_sky = brightness(self.color(direction)) / importance.integral;
            let weight = 2.0 * share(by_sky, by_cosine);
            let intensity = importance.weighted[index].map(|v| weight * v * cosine / PI);
            (dot(direction, triangle) > 0.0 && cosine > 0.0).then_some((direction, intensity))
        } else {
            let local = sampling::cosine_direction([numbers[0], numbers[1]]);
            let by_cosine = local[2] / PI;
            let direction = around(local);
            let color = self.color(direction);
            let by_sky = brightness(color) / importance.integral;
            let weight = 2.0 * share(by_cosine, by_sky);
            (dot(direction, triangle) > 0.0).then(|| (direction, color.map(|v| weight * v)))
        }
    }
}
