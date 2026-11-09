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
    /// The panel material's emission color (HDR, in the material's gamma space).
    panel_emission: [f32; 3],
    /// What DynamicGI.SetEmissive gave the panel (linear), if anything.
    panel_set_emissive: Option<[f32; 3]>,
    /// The left wall's color.
    left: [f32; 3],
}

const BASE: Room = Room {
    light: [-1.0, 3.0, -0.5],
    intensity: 2.0,
    bounce: 1.0,
    indirect_scale: 1.0,
    panel_emission: [2.0, 1.0, 0.2], // (1, 0.5, 0.1) * 2
    panel_set_emissive: None,
    left: [0.8, 0.1, 0.1],
};
const MOVED: [f32; 3] = [1.5, 2.5, -1.5];
/// The emissive panel's renderer (scene object file ID).
const PANEL: i64 = 1629154324;

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
            PANEL => ([0.2, 0.2, 0.2], room.panel_emission),
            308039783 => (room.left, [0.0; 3]),        // Left
            1583214537 => ([0.1, 0.8, 0.1], [0.0; 3]), // Right
            973394265 | 152063223 | 605051979 | 948094850 => (white, [0.0; 3]),
            other => panic!("unknown renderer {other}"),
        })
        .collect();
    let (albedo, emission) = material_textures(&system, &colors);
    let panel = renderer_of(&data, PANEL);
    let mut solver = SystemSolver::new(system, true).unwrap();
    solver.set_albedo(&albedo).unwrap();
    solver.set_emission(&emission).unwrap();
    if let Some(color) = room.panel_set_emissive {
        solver.set_renderer_emission(panel, color).unwrap();
    }
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
fn cluster_albedo_follows_the_root_mean_square_model() {
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
    let mut solver = SystemSolver::new(system.clone(), true).unwrap();
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
fn set_emissive_keeps_a_renderers_color_over_later_textures() {
    let data = room_data();
    let system = Arc::new(data.systems[0].clone());
    let panel = renderer_of(&data, PANEL);
    let floor = renderer_of(&data, 152063223);
    let texels = (system.transport.width * system.transport.height) as usize;
    let uniform = |rgb: [f32; 3]| {
        (0..texels)
            .flat_map(|_| [rgb[0], rgb[1], rgb[2], 1.0])
            .collect::<Vec<f32>>()
    };
    let mut solver = SystemSolver::new(system.clone(), true).unwrap();
    solver.set_emission(&uniform([0.5, 0.25, 0.125])).unwrap();
    let panel_clusters = clusters_of(&system, panel);
    let floor_clusters = clusters_of(&system, floor);
    assert!(!panel_clusters.is_empty() && !floor_clusters.is_empty());
    for &c in &panel_clusters {
        assert_eq!(solver.cluster_materials().1[c], [0.5, 0.25, 0.125]);
    }
    solver
        .set_renderer_emission(panel, [0.0, 3.0, 3.0])
        .unwrap();
    // A material change after it (the texture rendered again) keeps the renderer's own color
    // (measured for the renderer itself); other renderers take the new texture (Unbound's
    // model: the probe changes the panel alone).
    solver.set_emission(&uniform([1.0, 0.0, 1.0])).unwrap();
    for &c in &panel_clusters {
        assert_eq!(solver.cluster_materials().1[c], [0.0, 3.0, 3.0]);
    }
    for &c in &floor_clusters {
        assert_eq!(solver.cluster_materials().1[c], [1.0, 0.0, 1.0]);
    }
    // Emission is kept as a half float.
    solver.set_emission(&uniform([0.1, 1e9, -1.0])).unwrap();
    let e = solver.cluster_materials().1[floor_clusters[0]];
    assert_eq!(e, [half::f16::from_f32(0.1).to_f32(), 65504.0, 0.0]);
    solver.set_emission(&uniform([f32::NAN, 1.0, 1.0])).unwrap();
    assert_eq!(
        solver.cluster_materials().1[floor_clusters[0]],
        [0.0, 1.0, 1.0]
    );
    assert!(solver.set_renderer_emission(99, [0.0; 3]).is_err());
}

#[test]
fn gamma_space_projects_fail() {
    let data = room_data();
    let error = SystemSolver::new(Arc::new(data.systems[0].clone()), false)
        .err()
        .unwrap()
        .to_string();
    assert!(error.contains("gamma-space"), "{error}");
}

#[test]
fn material_textures_of_another_size_fail() {
    let data = room_data();
    let mut solver = SystemSolver::new(Arc::new(data.systems[0].clone()), true).unwrap();
    assert!(solver.set_albedo(&[0u8; 16]).is_err());
    assert!(solver.set_emission(&[0f32; 16]).is_err());
}

#[test]
fn material_changes_match_the_editor() {
    // The light moved and twice as intense, as in the probe from room-intensity on.
    let lit = Room {
        light: MOVED,
        intensity: 4.0,
        ..BASE
    };
    // The panel's material emission changed and its texture rendered again.
    let call = Room {
        panel_emission: [0.4, 2.0, 0.4], // (0.2, 1, 0.2) * 2
        ..lit
    };
    check(&call, "room-emission-call", 45, 0.006);
    // DynamicGI.SetEmissive(panel, (0, 1, 1) * 3): the color is taken to linear as a material
    // color is (raw, 948 of 5070 values are outside the tolerance).
    let set = Room {
        panel_set_emissive: Some([0.0, 3.0, 3.0].map(gamma_to_linear)),
        ..call
    };
    check(&set, "room-setemissive", 45, 0.006);
    // A material emission change rendered after it does not replace it.
    let then = Room {
        panel_emission: [3.0, 0.0, 3.0],
        ..set
    };
    check(&then, "room-setemissive-then-call", 45, 0.006);
    // The left wall turned blue and its texture rendered again (UpdateGIMaterials).
    check(
        &Room {
            left: [0.1, 0.1, 0.8],
            ..then
        },
        "room-albedo-call",
        45,
        0.006,
    );
}

/// Unity's built-in Cube: positions, lightmap UVs (UV2) and triangles.
const CUBE_POSITIONS: [[f32; 3]; 24] = [
    [0.5, -0.5, 0.5],
    [-0.5, -0.5, 0.5],
    [0.5, 0.5, 0.5],
    [-0.5, 0.5, 0.5],
    [0.5, 0.5, -0.5],
    [-0.5, 0.5, -0.5],
    [0.5, -0.5, -0.5],
    [-0.5, -0.5, -0.5],
    [0.5, 0.5, 0.5],
    [-0.5, 0.5, 0.5],
    [0.5, 0.5, -0.5],
    [-0.5, 0.5, -0.5],
    [0.5, -0.5, -0.5],
    [0.5, -0.5, 0.5],
    [-0.5, -0.5, 0.5],
    [-0.5, -0.5, -0.5],
    [-0.5, -0.5, 0.5],
    [-0.5, 0.5, 0.5],
    [-0.5, 0.5, -0.5],
    [-0.5, -0.5, -0.5],
    [0.5, -0.5, -0.5],
    [0.5, 0.5, -0.5],
    [0.5, 0.5, 0.5],
    [0.5, -0.5, 0.5],
];
const CUBE_UV2: [[f32; 2]; 24] = [
    [0.6909865, 0.3471644],
    [0.9966581, 0.3471644],
    [0.6909865, 0.65283585],
    [0.9966581, 0.65283585],
    [0.3097273, 0.34716424],
    [0.0040557664, 0.34716424],
    [0.34716424, 0.65283585],
    [0.65283585, 0.65283585],
    [0.3097273, 0.65283585],
    [0.0040557664, 0.65283585],
    [0.34716424, 0.34716424],
    [0.65283585, 0.34716424],
    [0.34716424, 0.0040557664],
    [0.34716424, 0.3097273],
    [0.65283585, 0.3097273],
    [0.65283585, 0.0040557664],
    [0.6902728, 0.3097273],
    [0.99594426, 0.3097273],
    [0.99594426, 0.0040557664],
    [0.6902728, 0.0040557664],
    [0.3097273, 0.0040557664],
    [0.0040557664, 0.0040557664],
    [0.0040557664, 0.3097273],
    [0.3097273, 0.3097273],
];
const CUBE_TRIANGLES: [[u32; 3]; 12] = [
    [0, 2, 3],
    [0, 3, 1],
    [8, 4, 5],
    [8, 5, 9],
    [10, 6, 7],
    [10, 7, 11],
    [12, 13, 14],
    [12, 14, 15],
    [16, 17, 18],
    [16, 18, 19],
    [20, 21, 22],
    [20, 22, 23],
];

fn cube_uvs(scale: [f32; 3], pixel_size: f32) -> crate::realtime_uv::Output {
    let positions: Vec<[f32; 3]> = CUBE_POSITIONS
        .iter()
        .map(|p| [p[0] * scale[0], p[1] * scale[1], p[2] * scale[2]])
        .collect();
    crate::realtime_uv::realtime_uvs(&crate::realtime_uv::Input {
        positions: &positions,
        uvs: &CUBE_UV2,
        triangles: &CUBE_TRIANGLES,
        pixel_size,
        min_chart_texels: 4,
    })
    .unwrap()
}

/// The Room scene's renderers (file IDs) and their scales.
const ROOM_SCALES: [(i64, [f32; 3]); 7] = [
    (152063223, [6.0, 0.2, 6.0]),  // Floor
    (973394265, [1.2, 1.5, 1.2]),  // Block
    (308039783, [0.2, 4.0, 6.0]),  // Left
    (1583214537, [0.2, 4.0, 6.0]), // Right
    (605051979, [6.0, 4.0, 0.2]),  // Back
    (948094850, [6.0, 0.2, 6.0]),  // Ceiling
    (PANEL, [1.0, 1.2, 0.1]),      // Panel
];

#[test]
fn realtime_uvs_match_the_precomputed_layout() {
    let data = room_data();
    let system = &data.systems[0];
    let mut sizes = Vec::new();
    for (k, &(object, _)) in data.renderers.iter().enumerate() {
        let scale = ROOM_SCALES.iter().find(|r| r.0 == object).unwrap().1;
        // Realtime resolution 2 texels per unit.
        let out = cube_uvs(scale, 0.5);
        crate::realtime_uv::check_layout(system, data.renderer_st[k], &out).unwrap();
        sizes.push((object, out.width, out.height));
        // Every chart's vertices lie half a texel inside its rectangle (its texel centres
        // inside the chart).
        assert_eq!(out.charts.len(), 6);
        for (c, tri) in CUBE_TRIANGLES.chunks(2).enumerate() {
            let rect = out.charts[c];
            for &v in tri.iter().flatten() {
                let uv = out.uvs[v as usize];
                let p = [uv[0] * out.width as f32, uv[1] * out.height as f32];
                for k in 0..2 {
                    let (lo, hi) = (rect[k] as f32 + 0.5, (rect[k] + rect[k + 2]) as f32 - 0.5);
                    assert!(
                        (p[k] - lo).abs() < 1e-3 || (p[k] - hi).abs() < 1e-3,
                        "chart {c}: {p:?} in {rect:?}"
                    );
                }
            }
        }
    }
    // The floor's 6 m faces take 14x14 texels, its thin faces the minimum 4x4.
    assert!(sizes.contains(&(152063223, 18, 28)));
    assert!(sizes.contains(&(973394265, 8, 12)));
    assert!(sizes.contains(&(308039783, 16, 24)));
    // A control: at twice the resolution the floor no longer fits its precomputed layout.
    let k = data
        .renderers
        .iter()
        .position(|r| r.0 == 152063223)
        .unwrap();
    let error = crate::realtime_uv::check_layout(
        system,
        data.renderer_st[k],
        &cube_uvs([6.0, 0.2, 6.0], 0.25),
    )
    .unwrap_err()
    .to_string();
    assert!(error.contains("realtime lightmap UVs of"), "{error}");
}

#[test]
fn realtime_uvs_of_irregular_meshes() {
    use crate::realtime_uv::{Input, realtime_uvs};
    // A 4 x 1 m quad whose UVs span 2 x 1: one chart, scaled to its area (2.83 m by 1.41 m in
    // UV proportions), its sides snapped to whole blocks separately.
    let positions = [
        [0.0, 0.0, 0.0],
        [4.0, 0.0, 0.0],
        [4.0, 0.0, 1.0],
        [0.0, 0.0, 1.0],
    ];
    let uvs = [[0.0, 0.0], [0.5, 0.0], [0.5, 0.25], [0.0, 0.25]];
    let triangles = [[0, 1, 2], [0, 2, 3]];
    let out = realtime_uvs(&Input {
        positions: &positions,
        uvs: &uvs,
        triangles: &triangles,
        pixel_size: 0.5,
        min_chart_texels: 4,
    })
    .unwrap();
    // At 2 texels per metre (1 block): 2.83 + 0.5 -> 4 blocks, 1.41 + 0.5 -> 2 blocks.
    assert_eq!((out.width, out.height), (8, 4));
    // A chart of no world area is a point: one block; an unused vertex stays at 0.
    let positions = [
        [0.0, 0.0, 0.0],
        [1.0, 0.0, 0.0],
        [2.0, 0.0, 0.0],
        [5.0, 5.0, 5.0],
    ];
    let uvs = [[0.0, 0.0], [0.5, 0.0], [1.0, 0.0], [0.3, 0.3]];
    let out = realtime_uvs(&Input {
        positions: &positions,
        uvs: &uvs,
        triangles: &[[0, 1, 2]],
        pixel_size: 0.5,
        min_chart_texels: 4,
    })
    .unwrap();
    assert_eq!((out.width, out.height), (2, 2));
    assert_eq!(out.uvs[3], [0.0, 0.0]);
    // Vertices with one position and UV join their triangles into one chart.
    let positions = [
        [0.0, 0.0, 0.0],
        [1.0, 0.0, 0.0],
        [1.0, 1.0, 0.0],
        [1.0, 1.0, 0.0],
        [0.0, 1.0, 0.0],
        [0.0, 0.0, 0.0],
    ];
    let uvs = [
        [0.0, 0.0],
        [1.0, 0.0],
        [1.0, 1.0],
        [1.0, 1.0],
        [0.0, 1.0],
        [0.0, 0.0],
    ];
    let out = realtime_uvs(&Input {
        positions: &positions,
        uvs: &uvs,
        triangles: &[[0, 1, 2], [3, 4, 5]],
        pixel_size: 0.1,
        min_chart_texels: 4,
    })
    .unwrap();
    assert_eq!(out.charts.len(), 1);
    // Malformed input fails without panicking.
    assert!(
        realtime_uvs(&Input {
            positions: &positions,
            uvs: &uvs,
            triangles: &[[0, 1, 9]],
            pixel_size: 0.1,
            min_chart_texels: 4
        })
        .is_err()
    );
    assert!(
        realtime_uvs(&Input {
            positions: &positions,
            uvs: &uvs[..2],
            triangles: &[[0, 1, 2]],
            pixel_size: 0.1,
            min_chart_texels: 4
        })
        .is_err()
    );
    let nan = [[f32::NAN, 0.0, 0.0], [1.0, 0.0, 0.0], [1.0, 1.0, 0.0]];
    assert!(
        realtime_uvs(&Input {
            positions: &nan,
            uvs: &uvs[..3],
            triangles: &[[0, 1, 2]],
            pixel_size: 0.1,
            min_chart_texels: 4
        })
        .is_err()
    );
    assert!(
        realtime_uvs(&Input {
            positions: &positions,
            uvs: &uvs,
            triangles: &[[0, 1, 2]],
            pixel_size: 0.0,
            min_chart_texels: 4
        })
        .is_err()
    );
}

/// Unity's built-in Plane: 10 x 10 m on XZ in 10 x 10 cells, UVs 0..1 (no UV2).
fn plane() -> (Vec<[f32; 3]>, Vec<[f32; 2]>, Vec<[u32; 3]>) {
    let (mut positions, mut uvs, mut triangles) = (Vec::new(), Vec::new(), Vec::new());
    for z in 0..=10 {
        for x in 0..=10 {
            let (u, v) = (x as f32 / 10.0, z as f32 / 10.0);
            positions.push([5.0 - 10.0 * u, 0.0, 5.0 - 10.0 * v]);
            uvs.push([u, v]);
        }
    }
    for z in 0..10u32 {
        for x in 0..10u32 {
            let i = z * 11 + x;
            triangles.push([i, i + 11, i + 12]);
            triangles.push([i, i + 12, i + 1]);
        }
    }
    (positions, uvs, triangles)
}

#[test]
fn realtime_uvs_of_another_scene_match_its_layout() {
    // tests/projects/lightingassets Realtime: a plane and three boxes of other sizes, realtime
    // resolution 2.
    let file = probe().join("../lightingassets/Assets/Scenes/Realtime/LightingData.asset");
    let bytes = std::fs::read(file).unwrap();
    let serialized = unbound_assets::serialized::SerializedFile::parse(&bytes).unwrap();
    let data = read_precomputed(&serialized.read(112000000).unwrap())
        .unwrap()
        .unwrap();
    assert_eq!(data.systems.len(), 1);
    let system = &data.systems[0];
    assert_eq!((system.transport.width, system.transport.height), (36, 42));
    let (positions, uvs, triangles) = plane();
    for (k, &(object, _)) in data.renderers.iter().enumerate() {
        let out = match object {
            333140154 => crate::realtime_uv::realtime_uvs(&crate::realtime_uv::Input {
                positions: &positions,
                uvs: &uvs,
                triangles: &triangles,
                pixel_size: 0.5,
                min_chart_texels: 4,
            })
            .unwrap(),
            859734017 => cube_uvs([0.2, 3.0, 6.0], 0.5),
            992040430 => cube_uvs([6.0, 3.0, 0.2], 0.5),
            1785088137 => cube_uvs([0.6, 1.2, 0.6], 0.5),
            other => panic!("unknown renderer {other}"),
        };
        crate::realtime_uv::check_layout(system, data.renderer_st[k], &out)
            .unwrap_or_else(|e| panic!("renderer {object}: {e:#}"));
    }
    // The solver runs on it too.
    let mut solver = SystemSolver::new(Arc::new(system.clone()), true).unwrap();
    solver.set_uniform_environment([0.1; 3]);
    solver.reset_bounce();
    assert!(solver.update().is_finite());
}
