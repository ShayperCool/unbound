//! A development tool: rasterizes the texel buffers of one lightmap (unbound_gi::baked::gbuffer)
//! from meshes and placements given as a flat binary file, and writes them raw, to compare the
//! baker's core with an Editor bake of the same input (tests/projects/bakedbuffers
//! `bake_buffers.py replay`).
//!
//!     cargo run --release -p unbound-gi --example baked_gbuffer -- <input> <output folder>
//!
//! Input (little-endian): u32 width, height, supersampling, meshes; per mesh u32 vertices, then
//! positions (3 f32 each), normals (3), lightmap UVs brought to 0..1 (2), their scale and offset
//! (4 f32), u32 submeshes, per submesh u32 triangles and their indices (3 u32 each); then u32
//! instances, per instance u32 mesh, 4 f32 lightmap scale and offset, i32 place in the lightmap,
//! 16 f32 local-to-world matrix (column-major), u32 receives shadows. The file may go on with
//! the scene to light the texels from: u32 shadow casters, per caster u32 mesh, 16 f32 matrix,
//! u32 submeshes and per submesh u32 whether it has a material; u32 lights, per light u32 kind (0 directional, 1 point, 2 spot), u32 mode (1 mixed,
//! 2 baked), 3 f32 colour, 3 f32 position, 3 f32 forward axis, f32 range, f32 cone angle, u32
//! casts shadows; u32 lighting mode of mixed lights (0 baked indirect, 1 subtractive, 2
//! shadowmask); f32 pushoff. Then `irradianceDirect.f32` is written too. With the blue noise
//! data of a Unity installation (`Resources/GI/SamplingData/bluenoisedata`) as third argument
//! the file may go on with the indirect light: u32 Indirect Samples, Max Bounces, Min Bounces;
//! per light 3 f32 indirect colour; u32 renderers of the bake, per renderer u32 mesh, 16 f32
//! matrix, u32 mirrored, u32 submeshes and per submesh u32 0 for a material, 1 for a double
//! sided one, 2 for none, then its albedo and its
//! emission image (u32 width, height, 4 f32 per texel, rows from the first). Then
//! `irradianceIndirect.f32` and `validity.f32` are written.
use std::path::Path;
use unbound_gi::baked::direct::{self, MixedMode, Receiver};
use unbound_gi::baked::gbuffer::{Instance, Mesh, gbuffer};
use unbound_gi::baked::grid::{Bounds, Grid};
use unbound_gi::baked::indirect::{self, Material, Settings, Surface, Texture, World};
use unbound_gi::baked::light::{Kind, Light, Mode};
use unbound_gi::baked::probes;
use unbound_gi::baked::sampling::Samples;
use unbound_gi::baked::shadowmask;
use unbound_gi::baked::sky::{Importance, Sky};
use unbound_gi::baked::trace::Scene;

struct Input<'a>(&'a [u8]);

impl Input<'_> {
    fn bytes<const N: usize>(&mut self) -> [u8; N] {
        let (head, rest) = self.0.split_at(N);
        self.0 = rest;
        head.try_into().unwrap()
    }
    fn u32(&mut self) -> u32 {
        u32::from_le_bytes(self.bytes())
    }
    fn f32(&mut self) -> f32 {
        f32::from_le_bytes(self.bytes())
    }
    fn floats<const N: usize>(&mut self) -> [f32; N] {
        std::array::from_fn(|_| self.f32())
    }
}

fn main() {
    let args: Vec<String> = std::env::args().collect();
    let (input, output) = match args.as_slice() {
        [_, input, output] | [_, input, output, _] => (input, output),
        _ => {
            eprintln!("usage: baked_gbuffer <input> <output folder> [<blue noise data>]");
            std::process::exit(2);
        }
    };
    let data = std::fs::read(input).expect("the input");
    let mut r = Input(&data);
    let (width, height, supersampling) = (r.u32() as usize, r.u32() as usize, r.u32() as usize);
    let meshes: Vec<Mesh> = (0..r.u32())
        .map(|_| {
            let n = r.u32() as usize;
            Mesh {
                positions: (0..n).map(|_| r.floats()).collect(),
                normals: (0..n).map(|_| r.floats()).collect(),
                uvs: (0..n).map(|_| r.floats()).collect(),
                uv_scale_offset: r.floats(),
                submeshes: (0..r.u32())
                    .map(|_| (0..r.u32()).map(|_| [r.u32(), r.u32(), r.u32()]).collect())
                    .collect(),
            }
        })
        .collect();
    let mut receivers = Vec::new();
    let instances: Vec<Instance> = (0..r.u32())
        .map(|_| {
            let instance = Instance {
                mesh: &meshes[r.u32() as usize],
                scale_offset: r.floats(),
                index: r.u32() as i32,
            };
            receivers.push(Receiver {
                local_to_world: r.floats(),
                receives_shadows: r.u32() != 0,
            });
            instance
        })
        .collect();
    let g = gbuffer(width, height, supersampling, &instances);
    let out = Path::new(output);
    std::fs::create_dir_all(out).expect("the output folder");
    let floats = |v: &[[f32; 4]]| {
        v.iter()
            .flatten()
            .flat_map(|f| f.to_le_bytes())
            .collect::<Vec<u8>>()
    };
    std::fs::write(out.join("positions.f32"), floats(&g.positions)).unwrap();
    std::fs::write(out.join("normals.f32"), floats(&g.normals)).unwrap();
    std::fs::write(out.join("planeNormals.f32"), floats(&g.plane_normals)).unwrap();
    std::fs::write(
        out.join("charts.i32"),
        g.charts
            .iter()
            .flat_map(|c| c.to_le_bytes())
            .collect::<Vec<u8>>(),
    )
    .unwrap();
    std::fs::write(out.join("occupancy.u8"), &g.occupancy).unwrap();
    if r.0.is_empty() {
        return;
    }
    let mut triangles = Vec::new();
    for _ in 0..r.u32() {
        let mesh = &meshes[r.u32() as usize];
        let m: [f32; 16] = r.floats();
        let world = |v: u32| direct::point(&m, mesh.positions[v as usize]);
        let drawn: Vec<bool> = (0..r.u32()).map(|_| r.u32() != 0).collect();
        let submeshes = mesh.submeshes.iter().enumerate();
        triangles.extend(
            submeshes
                .filter(|(submesh, _)| drawn.get(*submesh).copied().unwrap_or(false))
                .flat_map(|(_, triangles)| triangles)
                .map(|t| t.map(world)),
        );
    }
    let lights: Vec<Light> = (0..r.u32())
        .map(|_| {
            let kind = [Kind::Directional, Kind::Point, Kind::Spot][r.u32() as usize];
            let mode = if r.u32() == 1 {
                Mode::Mixed
            } else {
                Mode::Baked
            };
            let color = r.floats();
            Light {
                kind,
                mode,
                color,
                indirect_color: color,
                position: r.floats(),
                forward: r.floats(),
                right: r.floats(),
                range: r.f32(),
                cone_angle: r.f32(),
                casts_shadows: r.u32() != 0,
            }
        })
        .collect();
    let mixed = [
        MixedMode::IndirectOnly,
        MixedMode::Subtractive,
        MixedMode::Shadowmask,
    ][r.u32() as usize];
    let casters = Scene::new(triangles);
    // The lights' shadowmask channels (-1: none).
    let mut lights = lights;
    let assigned = shadowmask::assign(&mut lights, mixed == MixedMode::Shadowmask);
    let channels: Vec<String> = assigned
        .iter()
        .map(|c| c.map_or(-1, |c| c as i32).to_string())
        .collect();
    std::fs::write(out.join("channels.txt"), channels.join(" ")).unwrap();
    let pushoff = r.f32();
    let lit = direct::direct(&g, &receivers, &lights, &assigned, mixed, &casters, pushoff);
    std::fs::write(out.join("shadowmask.f32"), floats(&lit.shadowmask)).unwrap();
    // Light probes at the points a file names (three floats each), with the lights' grid over
    // the lightmap's own renderers.
    let probe_points: Vec<[f32; 3]> = std::env::var("PROBES_IN")
        .map(|path| {
            let bytes = std::fs::read(path).expect("the probes' points");
            let values: Vec<f32> = bytes
                .chunks_exact(4)
                .map(|b| f32::from_le_bytes([b[0], b[1], b[2], b[3]]))
                .collect();
            values.chunks_exact(3).map(|c| [c[0], c[1], c[2]]).collect()
        })
        .unwrap_or_default();
    if !probe_points.is_empty() {
        let positions = &probe_points;
        let around = Bounds::around(
            g.positions
                .iter()
                .filter_map(|p| {
                    (p[3] >= 0.0).then(|| {
                        let r = &receivers[p[3] as usize];
                        direct::point(&r.local_to_world, [p[0], p[1], p[2]])
                    })
                })
                .chain(positions.iter().copied()),
        )
        .unwrap_or(Bounds {
            min: [0.0; 3],
            max: [0.0; 3],
        });
        let grid = Grid::new(&lights, around);
        let probes = probes::direct(positions, &lights, &grid, &casters);
        let flat: Vec<f32> = probes.iter().flatten().flatten().copied().collect();
        std::fs::write(
            out.join("probesDirect.f32"),
            flat.iter()
                .flat_map(|v| v.to_le_bytes())
                .collect::<Vec<u8>>(),
        )
        .unwrap();
    }
    std::fs::write(out.join("irradianceDirect.f32"), floats(&lit.irradiance)).unwrap();
    std::fs::write(
        out.join("directionalityDirect.f32"),
        floats(&lit.directionality),
    )
    .unwrap();
    if r.0.is_empty() || args.len() < 4 {
        return;
    }
    // The indirect light: the settings, the lights' indirect colours, and every renderer of the
    // bake with its materials.
    let settings = Settings {
        samples: r.u32(),
        max_bounces: r.u32(),
        min_bounces: r.u32(),
        pushoff,
        environment_samples: 0,
    };
    let lights: Vec<Light> = lights
        .into_iter()
        .map(|light| Light {
            indirect_color: r.floats(),
            ..light
        })
        .collect();
    let mut surfaces = Vec::new();
    let mut materials = Vec::new();
    for _ in 0..r.u32() {
        let mesh = &meshes[r.u32() as usize];
        let m: [f32; 16] = r.floats();
        let mirrored = r.u32() != 0;
        let rows = direct::normal_matrix(&m);
        let sides: Vec<u32> = (0..r.u32()).map(|_| r.u32()).collect();
        let mut texture = || {
            let (width, height) = (r.u32() as usize, r.u32() as usize);
            Texture {
                width,
                height,
                texels: (0..width * height).map(|_| r.floats()).collect(),
            }
        };
        let (albedo, emissive) = (texture(), texture());
        for (submesh, triangles) in mesh.submeshes.iter().enumerate() {
            let Some(&sides) = sides.get(submesh).filter(|&&sides| sides < 2) else {
                continue;
            };
            for t in triangles {
                surfaces.push(Surface {
                    material: materials.len() as u32,
                    mirrored,
                    double_sided: sides == 1,
                    corners: t.map(|v| direct::point(&m, mesh.positions[v as usize])),
                    normals: t.map(|v| direct::world_normal(&rows, mesh.normals[v as usize])),
                    uvs: t.map(|v| mesh.uvs[v as usize]),
                });
            }
        }
        materials.push(Material { albedo, emissive });
    }
    // The sky, if the file goes on: u32 Environment Samples, u32 side of a face, six faces
    // (+x, -x, +y, -y, +z, -z; 4 f32 per texel, rows from the first), u32 directions picked
    // by brightness (0: none), f32 the sky's summed brightness, then the directions (3 f32
    // each) and the light over the chance of each (3 f32).
    let mut settings = settings;
    let sky = (!r.0.is_empty()).then(|| {
        settings.environment_samples = r.u32();
        let side = r.u32() as usize;
        let faces: [Texture; 6] = std::array::from_fn(|_| Texture {
            width: side,
            height: side,
            texels: (0..side * side).map(|_| r.floats()).collect(),
        });
        let count = r.u32() as usize;
        let integral = r.f32();
        let directions: Vec<[f32; 3]> = (0..count).map(|_| r.floats()).collect();
        let weighted: Vec<[f32; 3]> = (0..count).map(|_| r.floats()).collect();
        // With UNBOUND_OWN_DIRECTIONS set the directions are picked here instead of taken
        // from the file, and how far the two are apart is said.
        let given = (count > 0).then_some(Importance {
            directions,
            weighted,
            integral,
        });
        let importance = match (given, std::env::var_os("UNBOUND_OWN_DIRECTIONS")) {
            (Some(given), Some(_)) => {
                let own = Importance::of(&faces, count);
                let apart = |a: &[[f32; 3]], b: &[[f32; 3]]| {
                    a.iter()
                        .zip(b)
                        .flat_map(|(x, y)| {
                            (0..3).map(move |k| (x[k] - y[k]).abs() / y[k].abs().max(1.0))
                        })
                        .fold(0f32, f32::max)
                };
                eprintln!(
                    "own directions: {} apart, their light {}, the sky's brightness {} (given {})",
                    apart(&own.directions, &given.directions),
                    apart(&own.weighted, &given.weighted),
                    own.integral,
                    given.integral
                );
                Some(own)
            }
            (given, _) => given,
        };
        Sky { faces, importance }
    });
    let blue = std::fs::read(&args[3]).expect("the blue noise data");
    let samples = Samples::new(&blue).expect("the blue noise data's size");
    let scene = Scene::new(surfaces.iter().map(|s| s.corners));
    let around = Bounds::around(
        surfaces
            .iter()
            .flat_map(|s| s.corners)
            .chain(probe_points.iter().copied()),
    )
    .unwrap_or(Bounds {
        min: [0.0; 3],
        max: [0.0; 3],
    });
    let grid = Grid::new(&lights, around);
    let world = World {
        scene: &scene,
        casters: &casters,
        surfaces: &surfaces,
        materials: &materials,
        lights: &lights,
        grid: &grid,
        sky: sky.as_ref(),
    };
    let bounced = indirect::indirect(&g, &receivers, &world, &samples, settings);
    // The light probes' indirect light, with the probes' sample counts given as "paths,sky"
    // and the number of probes of each group (baked by itself) as `PROBE_GROUPS`
    // ("3,2"; one group of them all without it).
    if let (false, Ok(counts)) = (probe_points.is_empty(), std::env::var("PROBE_COUNTS")) {
        let counts: Vec<u32> = counts.split(',').map(|c| c.parse().unwrap()).collect();
        let sizes: Vec<usize> = std::env::var("PROBE_GROUPS").map_or_else(
            |_| vec![probe_points.len()],
            |g| g.split(',').map(|c| c.parse().unwrap()).collect(),
        );
        let mut probed = indirect::Probes {
            radiance: Vec::new(),
            validity: Vec::new(),
        };
        let mut start = 0;
        for size in sizes {
            let group = indirect::probes(
                &probe_points[start..start + size],
                &world,
                &samples,
                Settings {
                    samples: counts[0],
                    environment_samples: counts[1],
                    ..settings
                },
            );
            probed.radiance.extend(group.radiance);
            probed.validity.extend(group.validity);
            start += size;
        }
        let flat: Vec<f32> = probed
            .radiance
            .iter()
            .flatten()
            .flatten()
            .copied()
            .collect();
        std::fs::write(
            out.join("probesIndirect.f32"),
            flat.iter()
                .flat_map(|v| v.to_le_bytes())
                .collect::<Vec<u8>>(),
        )
        .unwrap();
        std::fs::write(
            out.join("probesValidity.f32"),
            probed
                .validity
                .iter()
                .flat_map(|v| v.to_le_bytes())
                .collect::<Vec<u8>>(),
        )
        .unwrap();
    }
    std::fs::write(
        out.join("irradianceIndirect.f32"),
        floats(&bounced.irradiance),
    )
    .unwrap();
    std::fs::write(
        out.join("irradianceEnvironment.f32"),
        floats(&bounced.environment),
    )
    .unwrap();
    std::fs::write(
        out.join("directionalityIndirect.f32"),
        floats(&bounced.directionality),
    )
    .unwrap();
    std::fs::write(
        out.join("validity.f32"),
        bounced
            .validity
            .iter()
            .flat_map(|v| v.to_le_bytes())
            .collect::<Vec<u8>>(),
    )
    .unwrap();
}
