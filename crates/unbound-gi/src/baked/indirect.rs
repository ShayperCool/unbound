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
use super::directional::{NONE, Toward};
use super::gbuffer::GBuffer;
use super::grid::{Grid, Pick};
use super::light::{Light, Tables};
use super::sampling::{self, Samples};
use super::sky::Sky;
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

    /// The image at the coordinates: its four texels around them, mixed by the coordinates'
    /// place between their centres; beyond the centres of the outermost texels, those texels.
    pub(super) fn at(&self, uv: [f32; 2]) -> [f32; 4] {
        if self.width == 0 || self.height == 0 {
            return [0.0; 4];
        }
        let (x, y) = (
            uv[0] * self.width as f32 - 0.5,
            uv[1] * self.height as f32 - 0.5,
        );
        let (column, row) = (x.floor(), y.floor());
        let (tx, ty) = (x - column, y - row);
        let texel = |column: f32, row: f32| {
            let within = |v: f32, size: usize| (v.max(0.0) as usize).min(size - 1);
            self.texels[within(row, self.height) * self.width + within(column, self.width)]
        };
        let (a, b) = (texel(column, row), texel(column + 1.0, row));
        let (c, d) = (texel(column, row + 1.0), texel(column + 1.0, row + 1.0));
        std::array::from_fn(|k| {
            (a[k] * (1.0 - tx) + b[k] * tx) * (1.0 - ty) + (c[k] * (1.0 - tx) + d[k] * tx) * ty
        })
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
    /// Environment Samples as asked for.
    pub environment_samples: u32,
}

/// The indirect light of a lightmap.
pub struct Indirect {
    /// Per texel red, green and blue, and 1 for a used texel (zeros for an unused one).
    pub irradiance: Vec<[f32; 4]>,
    /// Per texel the part of its rays that met a surface's back first.
    pub validity: Vec<f32>,
    /// Per texel the light its samples take straight from the sky, as `irradiance`.
    pub environment: Vec<[f32; 4]>,
    /// Where the indirect and the sky's light come from (see [`Toward::encode`]): a path's
    /// light from the direction it left the texel in.
    pub directionality: Vec<[f32; 4]>,
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
    /// Which of `lights` a surface takes (built from them).
    pub grid: &'a Grid,
    /// The environment's light, if the scene has any.
    pub sky: Option<&'a Sky>,
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
        self.numbers_of(index, dimension, self.distribution)
    }

    /// The same with the blue noise distribution of another sample count.
    fn numbers_of(&self, index: u32, dimension: usize, distribution: usize) -> [f32; 3] {
        let (x, y) = self.texel;
        if index < BLUE_NOISE_SAMPLES {
            std::array::from_fn(|k| self.samples.blue(index, dimension + k, x, y, distribution))
        } else {
            let rotation = self.samples.rotation(x, y, self.width, dimension);
            std::array::from_fn(|k| {
                sampling::fraction(self.samples.sobol(index, dimension + k) + rotation[k])
            })
        }
    }

    /// Follows a path from a texel's sample: adds the light it brings to `sum` and answers
    /// whether its first surface was met from behind.
    /// `path` takes the same light as `sum`, for this path alone.
    fn follow(&self, index: u32, mut ray: Ray, sum: &mut [f32; 3], path: &mut [f32; 3]) -> bool {
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
            // The lights: every directional one, and those of the surface's region of the
            // light grid, picked where they are more than the bounce tests.
            let mut lit = |pick: Pick| {
                let light = &world.lights[pick.light];
                let Some(arriving) =
                    light.arriving(self.tables, light.indirect_color, origin, smooth)
                else {
                    return;
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
                        let light = arriving.color[k] * reflected[k] * pick.weight;
                        sum[k] += light;
                        path[k] += light;
                    }
                }
            };
            for &light in &world.grid.directional {
                lit(Pick { light, weight: 1.0 });
            }
            if let Some(region) = world.grid.region(origin) {
                world
                    .grid
                    .lights_at(region, depth + 1, numbers[2], &mut lit);
            }
            let emissive = material.emissive.at(uv);
            for k in 0..3 {
                sum[k] += emissive[k] * throughput[k];
                path[k] += emissive[k] * throughput[k];
            }
            // The sky's light on the surface, unless the path ends here anyway. Any renderer
            // in the way stops it, also one that casts no shadows.
            if depth + 1 < self.settings.max_bounces
                && let Some(sky) = world.sky
                && let Some((direction, light)) = sky.arriving(smooth, geometric, numbers)
                && !world
                    .scene
                    .occluded(&Ray { origin, direction }, f32::INFINITY)
            {
                for k in 0..3 {
                    sum[k] += light[k] * reflected[k];
                    path[k] += light[k] * reflected[k];
                }
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
    let unlit = |used: &u8| [0.0, 0.0, 0.0, if *used == 0 { 0.0 } else { 1.0 }];
    if settings.max_bounces == 0 {
        // No bounce: no ray is sent, to a surface or to the sky, and none counts against a
        // texel.
        return Indirect {
            irradiance: g.occupancy.iter().map(unlit).collect(),
            validity: vec![0.0; g.width * g.height],
            environment: g.occupancy.iter().map(unlit).collect(),
            directionality: vec![NONE; g.width * g.height],
        };
    }
    let count = sampling::sample_count(settings.samples, s as u32);
    let distribution = sampling::distribution(count);
    let sky_count = sampling::sample_count(settings.environment_samples, s as u32);
    let sky_distribution = sampling::distribution(sky_count);
    let texels: Vec<([f32; 4], f32, [f32; 4], [f32; 4])> = (0..g.width * g.height)
        .into_par_iter()
        .map(|texel| {
            if g.occupancy[texel] == 0 {
                return ([0.0; 4], 0.0, [0.0; 4], NONE);
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
            // Where a ray of the texel starts: one of its samples, picked by the ray's number;
            // its point pushed off its triangle, its shading normal and its triangle's.
            let start = |index: u32| -> Option<([f32; 3], [f32; 3], [f32; 3])> {
                let pick = sampling::fraction(samples.sobol(index, 2) + place);
                let k = ((s * s) as f32 * pick).floor() as usize % (s * s);
                let sample = g.width * s * (ty * s + k / s) + tx * s + k % s;
                let local = g.positions[sample];
                let receiver = (local[3] >= 0.0)
                    .then(|| local[3] as usize)
                    .filter(|&i| i < receivers.len())?;
                let matrix = &receivers[receiver].local_to_world;
                let position = point(matrix, [local[0], local[1], local[2]]);
                let (smooth, plane) = (g.normals[sample], g.plane_normals[sample]);
                let smooth = world_normal(&normals[receiver], [smooth[0], smooth[1], smooth[2]]);
                let plane = world_normal(&normals[receiver], [plane[0], plane[1], plane[2]]);
                let origin: [f32; 3] =
                    std::array::from_fn(|c| position[c] + plane[c] * settings.pushoff);
                Some((origin, smooth, plane))
            };
            let mut sum = [0f32; 3];
            let (mut rays, mut behind) = (0u32, 0u32);
            let mut toward = Toward::default();
            for index in 0..count {
                let Some((origin, smooth, plane)) = start(index) else {
                    break;
                };
                let numbers = path.numbers(index, 0);
                let direction = sampling::around(smooth, [numbers[0], numbers[1]]);
                rays += 1;
                let facing = dot(direction, plane);
                if facing <= 0.0 || facing.is_nan() {
                    continue;
                }
                let mut light = [0f32; 3];
                if path.follow(index, Ray { origin, direction }, &mut sum, &mut light) {
                    behind += 1;
                }
                toward.add(unit(direction), light);
            }
            // The sky's light straight on the texel's samples.
            let mut sky_sum = [0f32; 3];
            let mut sky_rays = 0u32;
            if let Some(sky) = world.sky {
                for index in 0..sky_count {
                    let Some((origin, smooth, plane)) = start(index) else {
                        break;
                    };
                    sky_rays += 1;
                    let numbers = path.numbers_of(index, 0, sky_distribution);
                    if let Some((direction, light)) = sky.arriving(smooth, plane, numbers)
                        && !world
                            .scene
                            .occluded(&Ray { origin, direction }, f32::INFINITY)
                    {
                        for c in 0..3 {
                            sky_sum[c] += light[c];
                        }
                        toward.add(direction, light);
                    }
                }
            }
            let mean = |sum: [f32; 3], rays: u32| {
                let scale = if rays == 0 { 0.0 } else { 1.0 / rays as f32 };
                [sum[0] * scale, sum[1] * scale, sum[2] * scale, 1.0]
            };
            let validity = if rays == 0 {
                0.0
            } else {
                behind as f32 * (1.0 / rays as f32)
            };
            // How much the texel's samples face where the light comes from.
            let (mut facing, mut faced) = ([0f32; 3], 0u32);
            for k in 0..s * s {
                let sample = g.width * s * (ty * s + k / s) + tx * s + k % s;
                let local = g.positions[sample];
                let Some(receiver) = (local[3] >= 0.0)
                    .then(|| local[3] as usize)
                    .filter(|&i| i < receivers.len())
                else {
                    continue;
                };
                let n = g.normals[sample];
                let n = world_normal(&normals[receiver], [n[0], n[1], n[2]]);
                for c in 0..3 {
                    facing[c] += n[c];
                }
                faced += 1;
            }
            (
                mean(sum, rays),
                validity,
                mean(sky_sum, sky_rays),
                toward.encode(facing, faced),
            )
        })
        .collect();
    Indirect {
        irradiance: texels.iter().map(|t| t.0).collect(),
        validity: texels.iter().map(|t| t.1).collect(),
        environment: texels.iter().map(|t| t.2).collect(),
        directionality: texels.iter().map(|t| t.3).collect(),
    }
}
