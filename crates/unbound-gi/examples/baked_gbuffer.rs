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
//! the scene to light the texels from: u32 shadow casters, per caster u32 mesh and 16 f32
//! matrix; u32 lights, per light u32 kind (0 directional, 1 point, 2 spot), u32 mode (1 mixed,
//! 2 baked), 3 f32 colour, 3 f32 position, 3 f32 forward axis, f32 range, f32 cone angle, u32
//! casts shadows; u32 lighting mode of mixed lights (0 baked indirect, 1 subtractive, 2
//! shadowmask); f32 pushoff. Then `irradianceDirect.f32` is written too.
use std::path::Path;
use unbound_gi::baked::direct::{self, MixedMode, Receiver};
use unbound_gi::baked::gbuffer::{Instance, Mesh, gbuffer};
use unbound_gi::baked::light::{Kind, Light, Mode};
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
    let [_, input, output] = args.as_slice() else {
        eprintln!("usage: baked_gbuffer <input> <output folder>");
        std::process::exit(2);
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
        triangles.extend(mesh.submeshes.iter().flatten().map(|t| t.map(world)));
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
    let lit = direct::direct(
        &g,
        &receivers,
        &lights,
        mixed,
        &Scene::new(triangles),
        r.f32(),
    );
    std::fs::write(out.join("irradianceDirect.f32"), floats(&lit)).unwrap();
}
