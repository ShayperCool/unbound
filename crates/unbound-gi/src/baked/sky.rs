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
