//! The solver against the Unity 6000.0.71f1 Editor's converged realtime lightmaps of
//! tests/projects/realtimegi (Room scene).
use crate::data::{Precomputed, System, read_precomputed};
use crate::solver::{PointLight, SystemSolver, gamma_to_linear};
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

/// Material textures as a meta pass would render them with each renderer's color constant: every
/// texel a renderer's clusters sample holds its (Standard) meta albedo and its linear emission.
fn material_textures(system: &System, colors: &[([f32; 3], [f32; 3])]) -> (Vec<u8>, Vec<f32>) {
    let t = &system.transport;
    let texels = (t.width * t.height) as usize;
    let (mut albedo, mut emission) = (vec![0u8; 4 * texels], vec![0f32; 4 * texels]);
    let m = &system.materials;
    for (g, &(instance, _)) in m.groups.iter().enumerate() {
        let (color, emitted) = colors[m.instance_renderers[instance as usize] as usize];
        let bytes = standard_meta_albedo(color).map(|v| (v * 255.0 + 0.5).floor() as u8);
        for &((x, y), _) in &m.samples[m.group_samples[g] as usize..m.group_samples[g + 1] as usize]
        {
            let at = y as usize * t.width as usize + x as usize;
            albedo[4 * at..4 * at + 3].copy_from_slice(&bytes);
            albedo[4 * at + 3] = 255;
            for k in 0..3 {
                emission[4 * at + k] = gamma_to_linear(emitted[k]);
            }
            emission[4 * at + 3] = 1.0;
        }
    }
    (albedo, emission)
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
    let (albedo, emission) = material_textures(&system, &colors);
    let mut solver = SystemSolver::new(system).unwrap();
    solver.set_albedo(&albedo).unwrap();
    solver.set_emission(&emission).unwrap();
    solver.set_uniform_environment([0.1, 0.12, 0.2].map(gamma_to_linear));
    solver.set_lights(&[PointLight {
        position: room.light,
        // The project has no linear light intensity: the gamma color is scaled, then converted.
        color: [1.0, 0.95, 0.85].map(|c: f32| gamma_to_linear(c * room.intensity * room.bounce)),
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

/// `allowed`: values allowed outside the comparator tolerance; `median`: the largest median
/// relative error (both just above what was measured: chart-corner texels and the falloff).
fn check(room: &Room, case: &str, allowed: usize, median_limit: f32) {
    let (fails, total, median) = converge(room, case);
    eprintln!(
        "{case}: {fails} of {total} values outside the tolerance, median relative error {median}"
    );
    assert!(total > 4000, "{case}: {total} values");
    assert!(
        fails <= allowed,
        "{case}: {fails} of {total} values outside the tolerance"
    );
    assert!(
        median < median_limit,
        "{case}: median relative error {median}"
    );
}

#[test]
fn reads_the_rooms_precomputed_data() {
    let data = room_data();
    assert_eq!(data.atlas_sizes, vec![64]);
    assert_eq!(data.renderers.len(), 7);
    assert_eq!(data.renderer_st.len(), 7);
    assert_eq!(
        data.renderer_st[0],
        [0.15384616, 0.23076925, 0.61538464, 0.4615385]
    );
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
    // Known residual: a few texels at chart corners (up to about 15% off) and the falloff
    // curve within 1%.
    check(&BASE, "room-base", 15, 0.006);
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
        30,
        0.006,
    );
    check(
        &Room {
            light: MOVED,
            intensity: 4.0,
            ..BASE
        },
        "room-intensity",
        45,
        0.006,
    );
    check(
        &Room {
            light: MOVED,
            intensity: 4.0,
            bounce: 3.0,
            ..BASE
        },
        "room-bounce",
        60,
        0.006,
    );
    check(
        &Room {
            light: MOVED,
            intensity: 4.0,
            indirect_scale: 2.0,
            ..BASE
        },
        "room-indirectscale",
        110,
        0.0075,
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
            let _ = crate::data::read_cluster_materials(part, 52, 52);
        }
        if f.path.ends_with(".rsc.ref") {
            // The transport's width cut to 10 leaves texels outside it.
            let mut other = full.clone();
            other[80..82].copy_from_slice(&10u16.to_le_bytes());
            let error = crate::data::read_transport(&other).unwrap_err().to_string();
            assert!(error.contains("outside 10x52"), "{error}");
        }
        if f.path.ends_with(".iws.ref") {
            let mut other = full.clone();
            other[4] = other[4].wrapping_add(1);
            assert!(crate::data::read_input_samples(&other).is_err());
        }
    }
}

/// The Room system's renderer index (within the system) of scene object `object`.
fn renderer_of(data: &Precomputed, object: i64) -> usize {
    data.renderers[..data.systems[0].renderer_count]
        .iter()
        .position(|&(o, _)| o == object)
        .unwrap()
}

/// Clusters whose points all belong to renderer `renderer`.
fn clusters_of(system: &System, renderer: usize) -> Vec<usize> {
    let m = &system.materials;
    (0..system.input.clusters())
        .filter(|&c| {
            let groups = &m.groups[m.cluster_groups[c] as usize..m.cluster_groups[c + 1] as usize];
            !groups.is_empty()
                && groups
                    .iter()
                    .all(|&(i, _)| m.instance_renderers[i as usize] as usize == renderer)
        })
        .collect()
}

#[test]
fn cluster_albedo_is_the_root_mean_square_of_its_samples() {
    let data = room_data();
    let system = Arc::new(data.systems[0].clone());
    let t = &system.transport;
    // Texels alternate between black and white: the mean of the squares is 1/2 where a cluster's
    // samples weigh both equally, never the mean of the bytes.
    let texels = (t.width * t.height) as usize;
    let mut albedo = vec![0u8; 4 * texels];
    for (k, texel) in albedo.chunks_exact_mut(4).enumerate() {
        let v = if (k + k / t.width as usize) % 2 == 0 {
            255
        } else {
            0
        };
        texel.copy_from_slice(&[v, v, v, 255]);
    }
    let mut solver = SystemSolver::new(system.clone()).unwrap();
    solver.set_albedo(&albedo).unwrap();
    let m = &system.materials;
    for c in 0..system.input.clusters() {
        let (mut sum, mut points) = (0.0f64, 0u32);
        for g in m.cluster_groups[c] as usize..m.cluster_groups[c + 1] as usize {
            points += m.groups[g].1 as u32;
            for &((x, y), w) in
                &m.samples[m.group_samples[g] as usize..m.group_samples[g + 1] as usize]
            {
                let v = albedo[4 * (y as usize * t.width as usize + x as usize)] as f64 / 255.0;
                sum += w as f64 * v * v;
            }
        }
        let byte = ((sum / points.max(1) as f64).sqrt() * 255.0 + 0.5).floor() / 255.0;
        let got = solver.cluster_materials().0[c][0] as f64;
        assert!(
            (got - byte * byte).abs() < 1e-6,
            "cluster {c}: {got} vs {}",
            byte * byte
        );
    }
    // Mixed clusters exist: the rule is not a plain copy of one texel.
    assert!(
        solver
            .cluster_materials()
            .0
            .iter()
            .any(|a| a[0] > 0.2 && a[0] < 0.8),
        "no cluster mixes black and white texels"
    );
}

#[test]
fn set_emissive_overrides_a_renderers_texels_until_cleared() {
    let data = room_data();
    let system = Arc::new(data.systems[0].clone());
    let panel = renderer_of(&data, 1629154324);
    let block = renderer_of(&data, 152063223);
    let texels = (system.transport.width * system.transport.height) as usize;
    let uniform = |rgb: [f32; 3]| {
        (0..texels)
            .flat_map(|_| [rgb[0], rgb[1], rgb[2], 1.0])
            .collect::<Vec<f32>>()
    };
    let mut solver = SystemSolver::new(system.clone()).unwrap();
    solver.set_emission(&uniform([0.5, 0.25, 0.125])).unwrap();
    let panel_clusters = clusters_of(&system, panel);
    let block_clusters = clusters_of(&system, block);
    assert!(!panel_clusters.is_empty() && !block_clusters.is_empty());
    for &c in &panel_clusters {
        assert_eq!(solver.cluster_materials().1[c], [0.5, 0.25, 0.125]);
    }
    solver
        .set_renderer_emission(panel, Some([0.0, 3.0, 3.0]))
        .unwrap();
    // A material change after it (the texture rendered again) keeps the renderer's own color.
    solver.set_emission(&uniform([1.0, 0.0, 1.0])).unwrap();
    for &c in &panel_clusters {
        assert_eq!(solver.cluster_materials().1[c], [0.0, 3.0, 3.0]);
    }
    for &c in &block_clusters {
        assert_eq!(solver.cluster_materials().1[c], [1.0, 0.0, 1.0]);
    }
    solver.set_renderer_emission(panel, None).unwrap();
    for &c in &panel_clusters {
        assert_eq!(solver.cluster_materials().1[c], [1.0, 0.0, 1.0]);
    }
    // Emission is kept as a half float.
    solver.set_emission(&uniform([0.1, 1e9, -1.0])).unwrap();
    let e = solver.cluster_materials().1[block_clusters[0]];
    assert_eq!(e, [half::f16::from_f32(0.1).to_f32(), 65504.0, 0.0]);
    assert!(solver.set_renderer_emission(99, None).is_err());
}

#[test]
fn material_textures_of_another_size_fail() {
    let data = room_data();
    let mut solver = SystemSolver::new(Arc::new(data.systems[0].clone())).unwrap();
    assert!(solver.set_albedo(&[0u8; 16]).is_err());
    assert!(solver.set_emission(&[0f32; 16]).is_err());
}
