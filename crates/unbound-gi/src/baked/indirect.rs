//! The light that reaches each texel of a lightmap after leaving a surface: from every sample
//! of the texel rays go out over the hemisphere around its interpolated normal, by the cosine
//! of their angle to it. Where a ray meets a surface's front, the surface sends back what the
//! bake's lights give it there (their Indirect Multiplier's colour, shadowed, by the facing of
//! its corners' normals mixed at the point) times its albedo, and what it emits; the path goes on from there over the hemisphere around the triangle's own
//! normal, until Max Bounces, or earlier where it is ended by chance from Min Bounces on (the
//! path's weight then grows by the chance it had to go on). A ray that meets a surface's back
//! brings nothing and counts against the texel's validity.
//!
//! The rays' directions come from [`sampling`](super::sampling), read as the Unity 6000.0.71f1
//! Editor's Progressive CPU lightmapper reads them, which makes a texel's result the Editor's
//! but for the last bits of its sums (tests/projects/bakedbuffers).
use super::direct::{Receiver, normal_matrix, point, world_normal};
use super::gbuffer::GBuffer;
use super::light::{Light, Tables};
use super::sampling::{self, Samples};
use super::trace::{Ray, Scene};
use rayon::prelude::*;

/// The dimensions of the sample sequence a surface's hit takes; a texel's own samples take the
/// first three.
const DIMENSIONS_PER_BOUNCE: usize = 3;
/// The samples of a texel the blue noise sequence has values for; later ones take the rotated
/// Sobol sequence.
const BLUE_NOISE_SAMPLES: u32 = 256;

/// A colour image a material is read from by lightmap coordinates of its renderer (0..1 over
/// the renderer's own lightmap rectangle), rows from the first.
#[derive(Clone, Debug)]
pub struct Texture {
    pub width: usize,
    pub height: usize,
    pub texels: Vec<[f32; 4]>,
}

impl Texture {
    /// An image of one colour.
    pub fn uniform(color: [f32; 4]) -> Texture {
        Texture {
            width: 1,
            height: 1,
            texels: vec![color],
        }
    }

    /// The texel the coordinates fall in (the nearest one; coordinates outside 0..1 take the
    /// edge's).
    fn at(&self, uv: [f32; 2]) -> [f32; 4] {
        if self.width == 0 || self.height == 0 {
            return [0.0; 4];
        }
        let place =
            |v: f32, size: usize| ((v * size as f32).floor().max(0.0) as usize).min(size - 1);
        self.texels[place(uv[1], self.height) * self.width + place(uv[0], self.width)]
    }
}

/// What a renderer's surface gives back to the light: its albedo and its emission, each over
/// the renderer's lightmap rectangle.
#[derive(Clone, Debug)]
pub struct Material {
    pub albedo: Texture,
    pub emissive: Texture,
}

/// A triangle of the scene the paths run through, by its number in that [`Scene`].
#[derive(Clone, Copy, Debug)]
pub struct Surface {
    /// Its renderer's place in the materials.
    pub material: u32,
    /// Its renderer's transform mirrors it: its front is the side its corners run
    /// counter-clockwise on in the world.
    pub mirrored: bool,
    /// Its back counts as a front (its material's Double Sided Global Illumination).
    pub double_sided: bool,
    /// Its corners in the world.
    pub corners: [[f32; 3]; 3],
    /// Its corners' unit normals in the world.
    pub normals: [[f32; 3]; 3],
    /// Its corners' lightmap coordinates within its renderer's rectangle.
    pub uvs: [[f32; 2]; 3],
}

/// The bake's settings of indirect light.
#[derive(Clone, Copy, Debug)]
pub struct Settings {
    /// Indirect Samples as asked for ([`sampling::sample_count`] makes a texel's count of it).
    pub samples: u32,
    /// Max Bounces: the surfaces a path may meet.
    pub max_bounces: u32,
    /// Min Bounces: from the surface of this number on a path may end by chance.
    pub min_bounces: u32,
    /// How far a ray starts off its triangle.
    pub pushoff: f32,
}

/// The indirect light of a lightmap.
pub struct Indirect {
    /// Per texel red, green and blue, and 1 for a used texel (zeros for an unused one).
    pub irradiance: Vec<[f32; 4]>,
    /// Per texel the part of its rays that met a surface's back first.
    pub validity: Vec<f32>,
}

fn dot(a: [f32; 3], b: [f32; 3]) -> f32 {
    a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
}

fn unit(v: [f32; 3]) -> [f32; 3] {
    let length = dot(v, v).sqrt();
    if length > 0.0 {
        v.map(|x| x / length)
    } else {
        v
    }
}

/// The scene a bake's paths run through.
pub struct World<'a> {
    /// Every surface the paths can meet.
    pub scene: &'a Scene,
    /// What stands between a surface and a light (the shadow casters).
    pub casters: &'a Scene,
    /// The triangles of `scene`, by their numbers in it.
    pub surfaces: &'a [Surface],
    pub materials: &'a [Material],
    pub lights: &'a [Light],
}

struct Path<'a> {
    world: &'a World<'a>,
    samples: &'a Samples,
    tables: &'a Tables,
    settings: Settings,
    /// The texel and the lightmap's width, for the sequences.
    texel: (usize, usize),
    width: usize,
    distribution: usize,
}

impl Path<'_> {
    /// The three numbers of a sample at a dimension of the sequence.
    fn numbers(&self, index: u32, dimension: usize) -> [f32; 3] {
        let (x, y) = self.texel;
        if index < BLUE_NOISE_SAMPLES {
            std::array::from_fn(|k| {
                self.samples
                    .blue(index, dimension + k, x, y, self.distribution)
            })
        } else {
            let rotation = self.samples.rotation(x, y, self.width, dimension);
            std::array::from_fn(|k| {
                sampling::fraction(self.samples.sobol(index, dimension + k) + rotation[k])
            })
        }
    }

    /// Follows a path from a texel's sample: adds the light it brings to `sum` and answers
    /// whether its first surface was met from behind.
    fn follow(&self, index: u32, mut ray: Ray, sum: &mut [f32; 3]) -> bool {
        let world = self.world;
        let mut throughput = [1f32; 3];
        for depth in 0..self.settings.max_bounces {
            let Some(hit) = world.scene.closest(&ray, f32::INFINITY) else {
                return false;
            };
            let surface = &world.surfaces[hit.triangle as usize];
            let material = &world.materials[surface.material as usize];
            let front = hit.front != surface.mirrored;
            if !(front || surface.double_sided) {
                return depth == 0;
            }
            let [u, v] = hit.weights;
            let w = 1.0 - u - v;
            let mix3 = |a: &[[f32; 3]; 3]| -> [f32; 3] {
                std::array::from_fn(|k| w * a[0][k] + u * a[1][k] + v * a[2][k])
            };
            let uv: [f32; 2] = std::array::from_fn(|k| {
                w * surface.uvs[0][k] + u * surface.uvs[1][k] + v * surface.uvs[2][k]
            });
            // The triangle's own normal, on the side the ray came from.
            let (a, b, c) = (surface.corners[0], surface.corners[1], surface.corners[2]);
            let (e1, e2): ([f32; 3], [f32; 3]) = (
                std::array::from_fn(|k| b[k] - a[k]),
                std::array::from_fn(|k| c[k] - a[k]),
            );
            let mut geometric = unit([
                e1[1] * e2[2] - e1[2] * e2[1],
                e1[2] * e2[0] - e1[0] * e2[2],
                e1[0] * e2[1] - e1[1] * e2[0],
            ]);
            if dot(geometric, ray.direction) > 0.0 {
                geometric = geometric.map(|x| -x);
            }
            // The corners' normals, mixed and left at the length the mix has: a surface
            // between its corners takes a little less light than at them.
            let mut smooth = mix3(&surface.normals);
            if !front {
                smooth = smooth.map(|x| -x);
            }
            let position: [f32; 3] =
                std::array::from_fn(|k| ray.origin[k] + ray.direction[k] * hit.distance);
            let origin: [f32; 3] =
                std::array::from_fn(|k| position[k] + geometric[k] * self.settings.pushoff);
            let albedo = material.albedo.at(uv);
            let mut reflected: [f32; 3] = std::array::from_fn(|k| albedo[k] * throughput[k]);
            let numbers = self.numbers(index, (depth as usize + 1) * DIMENSIONS_PER_BOUNCE);
            for light in world.lights {
                let Some(arriving) =
                    light.arriving(self.tables, light.indirect_color, origin, smooth)
                else {
                    continue;
                };
                let shadowed = light.casts_shadows
                    && world.casters.occluded(
                        &Ray {
                            origin,
                            direction: arriving.direction,
                        },
                        arriving.distance,
                    );
                if !shadowed {
                    for k in 0..3 {
                        sum[k] += arriving.color[k] * reflected[k];
                    }
                }
            }
            let emissive = material.emissive.at(uv);
            for k in 0..3 {
                sum[k] += emissive[k] * throughput[k];
            }
            let mut goes_on = true;
            if depth + 1 >= self.settings.min_bounces {
                let chance = reflected[0].max(reflected[1]).max(reflected[2]);
                if chance < numbers[2] {
                    goes_on = false;
                }
                reflected = reflected.map(|x| (1.0 / chance) * x);
            }
            if !goes_on || depth + 1 >= self.settings.max_bounces {
                return false;
            }
            throughput = reflected;
            ray = Ray {
                origin,
                direction: sampling::around(geometric, [numbers[0], numbers[1]]),
            };
        }
        false
    }
}

/// The indirect light of every texel of the lightmap. `receivers` are the lightmap's renderers
/// by their place in it.
pub fn indirect(
    g: &GBuffer,
    receivers: &[Receiver],
    world: &World,
    samples: &Samples,
    settings: Settings,
) -> Indirect {
    let tables = Tables::new();
    let normals: Vec<[[f32; 3]; 3]> = receivers
        .iter()
        .map(|r| normal_matrix(&r.local_to_world))
        .collect();
    let s = g.supersampling;
    if settings.max_bounces == 0 {
        // No bounce: no ray is sent, and none counts against a texel.
        return Indirect {
            irradiance: g
                .occupancy
                .iter()
                .map(|&used| [0.0, 0.0, 0.0, if used == 0 { 0.0 } else { 1.0 }])
                .collect(),
            validity: vec![0.0; g.width * g.height],
        };
    }
    let count = sampling::sample_count(settings.samples, s as u32);
    let distribution = sampling::distribution(count);
    let texels: Vec<([f32; 4], f32)> = (0..g.width * g.height)
        .into_par_iter()
        .map(|texel| {
            if g.occupancy[texel] == 0 {
                return ([0.0; 4], 0.0);
            }
            let (tx, ty) = (texel % g.width, texel / g.width);
            let path = Path {
                world,
                samples,
                tables: &tables,
                settings,
                texel: (tx, ty),
                width: g.width,
                distribution,
            };
            let place = samples.rotation(tx, ty, g.width, 0)[0];
            let mut sum = [0f32; 3];
            let (mut rays, mut behind) = (0u32, 0u32);
            for index in 0..count {
                // Which of the texel's samples the ray starts from.
                let pick = sampling::fraction(samples.sobol(index, 2) + place);
                let k = ((s * s) as f32 * pick).floor() as usize % (s * s);
                let sample = g.width * s * (ty * s + k / s) + tx * s + k % s;
                let local = g.positions[sample];
                let Some(receiver) = (local[3] >= 0.0)
                    .then(|| local[3] as usize)
                    .filter(|&i| i < receivers.len())
                else {
                    break;
                };
                let matrix = &receivers[receiver].local_to_world;
                let position = point(matrix, [local[0], local[1], local[2]]);
                let (smooth, plane) = (g.normals[sample], g.plane_normals[sample]);
                let smooth = world_normal(&normals[receiver], [smooth[0], smooth[1], smooth[2]]);
                let plane = world_normal(&normals[receiver], [plane[0], plane[1], plane[2]]);
                let origin: [f32; 3] =
                    std::array::from_fn(|c| position[c] + plane[c] * settings.pushoff);
                let numbers = path.numbers(index, 0);
                let direction = sampling::around(smooth, [numbers[0], numbers[1]]);
                rays += 1;
                let facing = dot(direction, plane);
                if facing <= 0.0 || facing.is_nan() {
                    continue;
                }
                if path.follow(index, Ray { origin, direction }, &mut sum) {
                    behind += 1;
                }
            }
            if rays == 0 {
                return ([0.0, 0.0, 0.0, 1.0], 0.0);
            }
            let scale = 1.0 / rays as f32;
            (
                [sum[0] * scale, sum[1] * scale, sum[2] * scale, 1.0],
                behind as f32 * scale,
            )
        })
        .collect();
    Indirect {
        irradiance: texels.iter().map(|t| t.0).collect(),
        validity: texels.iter().map(|t| t.1).collect(),
    }
}
