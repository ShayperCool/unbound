//! The solver against the Unity 6000.0.71f1 Editor's converged realtime lightmaps of
//! tests/projects/realtimegi (Room scene).
use crate::data::{Precomputed, read_precomputed};
use crate::solver::{PointLight, SystemSolver, gamma_to_linear, light_color};
use std::path::Path;
use std::sync::Arc;

fn probe() -> std::path::PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../tests/projects/realtimegi")
}

fn room_data() -> Precomputed {
    let file = probe().join("Assets/Scenes/Room/LightingData.asset");
    let bytes = std::fs::read(file).unwrap();
    let serialized = unbound_assets::serialized::SerializedFile::parse(&bytes).unwrap();
    read_precomputed(&serialized.read(112000000).unwrap())
        .unwrap()
        .expect("precomputed realtime GI")
}

/// A reference atlas: width, height, RGBA half floats (alpha 0: not covered).
fn reference(case: &str) -> (usize, Vec<[f32; 4]>) {
    let bytes =
        std::fs::read(probe().join(format!("Reference/unity-6000.0.71f1/{case}.atlas"))).unwrap();
    let width = i32::from_le_bytes(bytes[0..4].try_into().unwrap()) as usize;
    let half = |h: u16| -> f32 {
        let (sign, exp, frac) = (
            (h >> 15) as u32,
            ((h >> 10) & 0x1F) as i32,
            (h & 0x3FF) as f32,
        );
        let v = match exp {
            0 => frac * 2f32.powi(-24),
            31 => f32::INFINITY,
            e => (1.0 + frac / 1024.0) * 2f32.powi(e - 15),
        };
        if sign == 1 { -v } else { v }
    };
    let texels = bytes[8..]
        .chunks(8)
        .map(|c| std::array::from_fn(|k| half(u16::from_le_bytes([c[2 * k], c[2 * k + 1]]))))
        .collect();
    (width, texels)
}

/// What the Standard shader's meta pass writes as albedo for an opaque, non-metallic,
/// zero-smoothness material of `color` in a linear project: the diffuse part plus half the
/// dielectric specular, back in gamma space (test input; the engine renders the meta pass).
fn standard_meta_albedo(color: [f32; 3]) -> [f32; 3] {
    color.map(|c| {
        let linear = gamma_to_linear(c) * 0.96 + 0.02;
        (1.055 * linear.powf(0.416_666_67) - 0.055).max(0.0)
    })
}

struct Room {
    light: [f32; 3],
    intensity: f32,
    bounce: f32,
    indirect_scale: f32,
}

const BASE: Room = Room {
    light: [-1.0, 3.0, -0.5],
    intensity: 2.0,
    bounce: 1.0,
    indirect_scale: 1.0,
};
const MOVED: [f32; 3] = [1.5, 2.5, -1.5];

/// Converges the Room scene's system under `room` and compares it with the reference `case`:
/// (values outside the probe comparator's tolerance, values compared, median relative error).
fn converge(room: &Room, case: &str) -> (usize, usize, f32) {
    let data = room_data();
    let system = Arc::new(data.systems[0].clone());
    // The scene mapping's renderers (the Room scene's renderer file IDs) and their materials.
    let white = [0.8, 0.8, 0.8];
    let colors: Vec<([f32; 3], [f32; 3])> = data.renderers[system.first_renderer..]
        [..system.renderer_count]
        .iter()
        .map(|&(object, _)| match object {
            1629154324 => ([0.2, 0.2, 0.2], [2.0, 1.0, 0.2]), // Panel, emission (1, 0.5, 0.1) * 2
            308039783 => ([0.8, 0.1, 0.1], [0.0; 3]),         // Left
            1583214537 => ([0.1, 0.8, 0.1], [0.0; 3]),        // Right
            973394265 | 152063223 | 605051979 | 948094850 => (white, [0.0; 3]),
            other => panic!("unknown renderer {other}"),
        })
        .collect();
    let albedo: Vec<_> = colors
        .iter()
        .map(|(c, _)| standard_meta_albedo(*c))
        .collect();
    let emission: Vec<_> = colors.iter().map(|(_, e)| e.map(gamma_to_linear)).collect();
    let mut solver = SystemSolver::new(system).unwrap();
    solver.set_materials(&albedo, &emission).unwrap();
    solver.set_uniform_environment([0.1, 0.12, 0.2].map(gamma_to_linear));
    solver.set_lights(&[PointLight {
        position: room.light,
        color: light_color([1.0, 0.95, 0.85], room.intensity, room.bounce, false),
        range: 10.0,
    }]);
    solver.set_indirect_scale(room.indirect_scale);
    solver.reset_bounce();
    for _ in 0..1000 {
        if solver.update() < 1e-7 {
            break;
        }
    }
    let (width, texels) = reference(case);
    let t = &solver.system().transport;
    let (mut fails, mut total, mut relative) = (0, 0, Vec::new());
    for y in 0..t.height as usize {
        for x in 0..t.width as usize {
            let r = texels[y * width + x];
            if r[3] <= 0.0 {
                continue;
            }
            let got = crate::solver::decode_rgb9e5(solver.encoded()[y * t.width as usize + x]);
            for k in 0..3 {
                let d = (got[k] - r[k]).abs();
                total += 1;
                fails += (d > 0.02 + 0.05 * r[k].abs()) as usize;
                relative.push(d / r[k].abs().max(1e-3));
            }
        }
    }
    relative.sort_by(f32::total_cmp);
    (fails, total, relative[relative.len() / 2])
}

/// `percent`: the share of values allowed outside the comparator tolerance.
fn check(room: &Room, case: &str, percent: usize) {
    let (fails, total, median) = converge(room, case);
    eprintln!(
        "{case}: {fails} of {total} values outside the tolerance, median relative error {median}"
    );
    assert!(total > 4000, "{case}: {total} values");
    assert!(
        fails * 100 <= total * percent,
        "{case}: {fails} of {total} values outside the tolerance"
    );
    assert!(median < 0.01, "{case}: median relative error {median}");
}

#[test]
fn reads_the_rooms_precomputed_data() {
    let data = room_data();
    assert_eq!(data.atlas_sizes, vec![64]);
    assert_eq!(data.renderers.len(), 7);
    let s = &data.systems[0];
    assert_eq!(
        (s.first_renderer, s.renderer_count, s.atlas, s.atlas_offset),
        (0, 7, 0, (0, 0))
    );
    assert_eq!(
        (
            s.input.clusters(),
            s.input.positions.len(),
            s.input.values()
        ),
        (312, 10192, 746)
    );
    assert_eq!((s.transport.width, s.transport.height), (52, 52));
    let texels: usize = s.transport.segments.iter().map(|g| g.texels.len()).sum();
    assert_eq!(texels, 2160);
    assert_eq!(s.transport.bounce.len(), 3100);
    assert_eq!(s.materials.instance_renderers.len(), 7);
}

#[test]
fn converges_to_the_editors_lightmap() {
    // Known residual: a few texels at chart corners (up to about 15% off, under 1% of the
    // values) and the falloff curve within 1%.
    check(&BASE, "room-base", 1);
}

#[test]
fn a_moved_brighter_light() {
    // Controls outside the base setup: the light moved, then twice as intense, then with bounce
    // intensity 3 (scaled before the conversion to linear), then DynamicGI.indirectScale 2.
    check(
        &Room {
            light: MOVED,
            ..BASE
        },
        "room-moved",
        1,
    );
    check(
        &Room {
            light: MOVED,
            intensity: 4.0,
            ..BASE
        },
        "room-intensity",
        1,
    );
    check(
        &Room {
            light: MOVED,
            intensity: 4.0,
            bounce: 3.0,
            ..BASE
        },
        "room-bounce",
        1,
    );
    check(
        &Room {
            light: MOVED,
            intensity: 4.0,
            indirect_scale: 2.0,
            ..BASE
        },
        "room-indirectscale",
        2,
    );
    // (The Editor's texels are 2.00 to 2.07 times those without the scale; Unbound's exactly 2.)
}

#[test]
fn rgb9e5_round_trip() {
    use crate::solver::{decode_rgb9e5, encode_rgb9e5};
    for rgb in [
        [0.0, 0.0, 0.0],
        [1.0, 0.5, 0.25],
        [123.4, 0.001, 7.0],
        [0.01, 0.0134, 0.0331],
    ] {
        let back = decode_rgb9e5(encode_rgb9e5(rgb));
        let max = rgb[0].max(rgb[1]).max(rgb[2]);
        for k in 0..3 {
            assert!(
                (back[k] - rgb[k]).abs() <= max / 256.0,
                "{rgb:?} -> {back:?}"
            );
        }
    }
    assert_eq!(encode_rgb9e5([f32::NAN, -1.0, 0.0]), 0);
}

#[test]
fn malformed_data_fails_without_panicking() {
    let file = probe().join("Assets/Scenes/Room/LightingData.asset");
    let bytes = std::fs::read(file).unwrap();
    let serialized = unbound_assets::serialized::SerializedFile::parse(&bytes).unwrap();
    let object = serialized.read(112000000).unwrap();
    let payload = object
        .get("m_EnlightenData")
        .and_then(unbound_yaml::Value::as_str)
        .unwrap();
    let bundle_bytes: Vec<u8> = (0..payload.len() / 2)
        .map(|k| u8::from_str_radix(&payload[2 * k..2 * k + 2], 16).unwrap())
        .collect();
    let bundle = unbound_assets::bundle::Bundle::parse(&bundle_bytes).unwrap();
    for f in &bundle.files {
        let full = &f.data;
        // Every truncation and a changed version are errors, never panics.
        for cut in [0, 8, 24, full.len() / 3, full.len() / 2, full.len() - 1] {
            let part = &full[..cut];
            let _ = crate::data::read_input_samples(part);
            let _ = crate::data::read_transport(part);
            let _ = crate::data::read_cluster_materials(part);
        }
        if f.path.ends_with(".iws.ref") {
            let mut other = full.clone();
            other[4] = other[4].wrapping_add(1);
            assert!(crate::data::read_input_samples(&other).is_err());
        }
    }
}
