//! The solver against the Unity 6000.0.71f1 Editor's converged realtime lightmaps of
//! tests/projects/realtimegi (Room scene).
use crate::data::{Precomputed, System, read_precomputed};
use crate::probes::ProbeSetSolver;
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

/// The Editor's LightProbes.GetInterpolatedProbe at the probe positions in case `case` (27
/// coefficients each, red, green, blue), from the probe's values.jsonl.
fn reference_probes(case: &str) -> Vec<Vec<f32>> {
    let text =
        std::fs::read_to_string(probe().join("Reference/unity-6000.0.71f1/values.jsonl")).unwrap();
    let line = text
        .lines()
        .find(|l| l.contains(&format!("\"case\":\"{case}\"")))
        .unwrap();
    let start = line.find("\"probes\":[").unwrap() + "\"probes\":[".len();
    let end = start + line[start..].find("]]").unwrap() + 1;
    line[start..end]
        .split("],")
        .map(|p| {
            p.trim_matches(|c| c == '[' || c == ']')
                .split(',')
                .map(|v| v.parse().unwrap())
                .collect()
        })
        .collect()
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

#[derive(Clone, Copy)]
struct Room {
    light: [f32; 3],
    intensity: f32,
    bounce: f32,
    indirect_scale: f32,
    /// The panel material's emission color (HDR, in the material's gamma space).
    panel_emission: [f32; 3],
    /// What DynamicGI.SetEmissive gave the panel (linear), if anything.
    panel_set_emissive: Option<[f32; 3]>,
    /// The left wall's color, and the one its albedo texture was last rendered with
    /// (Renderer.UpdateGIMaterials).
    left: [f32; 3],
    left_rendered: [f32; 3],
    /// The flat ambient color (gamma).
    ambient: [f32; 3],
    point_light: bool,
    /// DynamicGI.updateThreshold.
    threshold: f32,
}

const BASE: Room = Room {
    light: [-1.0, 3.0, -0.5],
    intensity: 2.0,
    bounce: 1.0,
    indirect_scale: 1.0,
    panel_emission: [2.0, 1.0, 0.2], // (1, 0.5, 0.1) * 2
    panel_set_emissive: None,
    left: [0.8, 0.1, 0.1],
    left_rendered: [0.8, 0.1, 0.1],
    ambient: [0.1, 0.12, 0.2],
    point_light: true,
    threshold: 1.0,
};
const MOVED: [f32; 3] = [1.5, 2.5, -1.5];
/// The emissive panel's renderer (scene object file ID).
const PANEL: i64 = 1629154324;

/// Values allowed outside the probe comparator's tolerance per case once the light moved: a few
/// isolated texels, since a part of the lightmap is solved again only when its inputs change by
/// the update threshold, and the point light's direct input is within about 2% per cluster.
const ISOLATED: usize = 4;

/// The Room scene's system replayed through the probe's cases in order: the Editor's update
/// solves a segment again only when its inputs changed by `DynamicGI.updateThreshold`, so a
/// converged lightmap depends on the state it converged from.
struct Replay {
    data: Precomputed,
    solver: SystemSolver,
    probes: ProbeSetSolver,
    room: Option<Room>,
}

impl Replay {
    fn new() -> Self {
        let data = room_data();
        let solver = SystemSolver::new(Arc::new(data.systems[0].clone()), true).unwrap();
        let probes = ProbeSetSolver::new(
            Arc::new(data.probe_sets[0].clone()),
            std::slice::from_ref(&solver),
        )
        .unwrap();
        Self {
            data,
            solver,
            probes,
            room: None,
        }
    }

    /// Sets the inputs of `room` that differ from the last ones.
    fn set(&mut self, room: Room) {
        let old = self.room.take();
        let changed = |f: &dyn Fn(&Room) -> bool| old.as_ref().is_none_or(|o| !f(o));
        let system = &self.data.systems[0];
        // The scene mapping's renderers (the Room scene's renderer file IDs) and their materials.
        if changed(&|o| {
            o.panel_emission == room.panel_emission && o.left_rendered == room.left_rendered
        }) {
            let white = [0.8, 0.8, 0.8];
            let colors: Vec<([f32; 3], [f32; 3])> = self.data.renderers[system.first_renderer..]
                [..system.renderer_count]
                .iter()
                .map(|&(object, _)| match object {
                    PANEL => ([0.2, 0.2, 0.2], room.panel_emission),
                    308039783 => (room.left_rendered, [0.0; 3]), // Left
                    1583214537 => ([0.1, 0.8, 0.1], [0.0; 3]),   // Right
                    973394265 | 152063223 | 605051979 | 948094850 => (white, [0.0; 3]),
                    other => panic!("unknown renderer {other}"),
                })
                .collect();
            let (albedo, emission) = material_textures(system, &colors);
            self.solver.set_albedo(&albedo).unwrap();
            self.solver.set_emission(&emission).unwrap();
        }
        if let Some(color) = room.panel_set_emissive
            && changed(&|o| o.panel_set_emissive == room.panel_set_emissive)
        {
            let panel = renderer_of(&self.data, PANEL);
            self.solver.set_renderer_emission(panel, color).unwrap();
        }
        self.solver
            .set_uniform_environment(room.ambient.map(gamma_to_linear));
        let lights = [PointLight {
            position: room.light,
            // The project has no linear light intensity: the gamma color is scaled, then
            // converted.
            color: [1.0, 0.95, 0.85].map(|c: f32| (c * room.intensity * room.bounce).powf(2.2)),
            range: 10.0,
        }];
        self.solver
            .set_lights(if room.point_light { &lights } else { &[] });
        self.solver.set_indirect_scale(room.indirect_scale);
        self.solver.set_update_threshold(room.threshold);
        self.room = Some(room);
    }

    /// Updates until the drawn texels stop changing (as the engine decides convergence).
    fn converge(&mut self) {
        let mut last = self.solver.encoded().to_vec();
        for _ in 0..1000 {
            self.solver.update();
            if self.solver.encoded() == &last[..] {
                return;
            }
            last.copy_from_slice(self.solver.encoded());
        }
        panic!("no convergence in 1000 updates");
    }

    /// Sets `room`, converges and compares with the reference `case`: (values outside the probe
    /// comparator's tolerance, values compared, median relative error).
    fn case(&mut self, room: Room, case: &str) -> (usize, usize, f32) {
        self.set(room);
        self.converge();
        let (width, texels) = reference(case);
        let t = &self.solver.system().transport;
        let (mut fails, mut total, mut relative) = (0, 0, Vec::new());
        for y in 0..t.height as usize {
            for x in 0..t.width as usize {
                let r = texels[y * width + x];
                if r[3] <= 0.0 {
                    continue;
                }
                let got =
                    crate::solver::decode_rgb9e5(self.solver.encoded()[y * t.width as usize + x]);
                for k in 0..3 {
                    let d = (got[k] - r[k]).abs();
                    total += 1;
                    if d > 0.02 + 0.05 * r[k].abs() {
                        fails += 1;
                        eprintln!("  {case} ({x},{y}) channel {k}: {} vs {}", got[k], r[k]);
                    }
                    relative.push(d / r[k].abs().max(1e-3));
                }
            }
        }
        relative.sort_by(f32::total_cmp);
        (fails, total, relative[relative.len() / 2])
    }

    /// The light probes' coefficients compared with the Editor's in `case`: (coefficients off,
    /// the largest difference). A coefficient is off beyond the probe comparator's tolerance plus
    /// 1% of its channel's band-0 coefficient: the direct light's residual (about 2% per
    /// cluster) moves the small higher-band coefficients of bright probes by that much.
    fn probes(&self, case: &str) -> (usize, f32) {
        let room = self.room.unwrap();
        let environment = vec![room.ambient.map(gamma_to_linear); crate::data::ENVIRONMENT_VALUES];
        let mut sh = vec![[0.0; 27]; self.probes.probe_set().probes.len()];
        self.probes.solve(
            std::slice::from_ref(&self.solver),
            &environment,
            room.indirect_scale,
            &mut sh,
        );
        let (mut fails, mut worst) = (0, 0f32);
        for (ours, theirs) in sh.iter().zip(reference_probes(case)) {
            for (k, (a, b)) in ours.iter().zip(&theirs).enumerate() {
                let d = (a - b).abs();
                worst = worst.max(d);
                fails += (d > 0.02 + 0.05 * b.abs() + 0.01 * theirs[k / 9 * 9].abs()) as usize;
            }
        }
        (fails, worst)
    }

    /// `allowed`: values allowed outside the comparator tolerance; `median_limit`: the largest
    /// median relative error.
    fn check(&mut self, room: Room, case: &str, allowed: usize, median_limit: f32) {
        let (fails, total, median) = self.case(room, case);
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
        let (fails, worst) = self.probes(case);
        eprintln!("{case} probes: {fails} coefficients off, worst {worst}");
        assert_eq!(fails, 0, "{case}: light probe coefficients");
    }
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
fn replays_the_probes_cases() {
    // The probe's Room cases in their order (up to room-pointoff), each converged from the one
    // before.
    let mut r = Replay::new();
    r.check(BASE, "room-base", 0, 0.001);
    let moved = Room {
        light: MOVED,
        ..BASE
    };
    r.check(moved, "room-moved", 0, 0.006);
    // Twice as intense, then with bounce intensity 3 (scaled before the conversion to linear).
    let lit = Room {
        intensity: 4.0,
        ..moved
    };
    r.check(lit, "room-intensity", ISOLATED, 0.006);
    r.check(Room { bounce: 3.0, ..lit }, "room-bounce", ISOLATED, 0.006);
    // DynamicGI.indirectScale 2, converged from the brighter room-bounce state: the Editor's
    // texels are 2.00 to 2.07 times room-intensity's, as segments stop being solved once their
    // inputs change by less than the update threshold.
    r.check(
        Room {
            indirect_scale: 2.0,
            ..lit
        },
        "room-indirectscale",
        ISOLATED,
        0.006,
    );
    r.set(lit);
    r.converge();
    // The panel's material emission changed and its texture rendered again.
    let call = Room {
        panel_emission: [0.4, 2.0, 0.4], // (0.2, 1, 0.2) * 2
        ..lit
    };
    r.check(call, "room-emission-call", ISOLATED, 0.006);
    // DynamicGI.SetEmissive(panel, (0, 1, 1) * 3): the color is taken to linear as a material
    // color is.
    let set = Room {
        panel_set_emissive: Some([0.0, 3.0, 3.0].map(gamma_to_linear)),
        ..call
    };
    r.check(set, "room-setemissive", ISOLATED, 0.006);
    // A material emission change rendered after it does not replace it.
    let then = Room {
        panel_emission: [3.0, 0.0, 3.0],
        ..set
    };
    r.check(then, "room-setemissive-then-call", ISOLATED, 0.006);
    // The left wall turned blue: no change until its texture is rendered again
    // (UpdateGIMaterials).
    let blue = Room {
        left: [0.1, 0.1, 0.8],
        ..then
    };
    r.check(blue, "room-albedo-nocall", ISOLATED, 0.006);
    let blue = Room {
        left_rendered: blue.left,
        ..blue
    };
    r.check(blue, "room-albedo-call", ISOLATED, 0.006);
    // The ambient color changed (DynamicGI.UpdateEnvironment changes nothing more).
    let ambient = Room {
        ambient: [0.6, 0.5, 0.2],
        ..blue
    };
    r.check(ambient, "room-env-nocall", ISOLATED, 0.006);
    r.check(ambient, "room-env-call", ISOLATED, 0.006);
    // (The skybox cases are not supported yet; the probe returns to the same flat ambient.)
    r.check(
        Room {
            point_light: false,
            ..ambient
        },
        "room-pointoff",
        ISOLATED,
        0.006,
    );
}

#[test]
fn a_threshold_of_zero_reaches_the_fixed_point() {
    // room-indirectscale's history (indirect scale 2 after the brighter bounce state) at
    // DynamicGI.updateThreshold 0 and 1 (tests/projects/realtimegi records both in RoomScaled and
    // RoomDirectional). At 0 the output is exactly twice the scale-1 fixed point, whatever the
    // state before; at the default of 1 parts keep some of the brighter state.
    let lit = Room {
        light: MOVED,
        intensity: 4.0,
        ..BASE
    };
    let run = |threshold: f32, before: Room, scale: f32| {
        let mut r = Replay::new();
        r.set(Room {
            threshold,
            ..before
        });
        r.converge();
        r.set(Room {
            threshold,
            indirect_scale: scale,
            ..lit
        });
        r.converge();
        r.solver.encoded().to_vec()
    };
    let bright = Room { bounce: 3.0, ..lit };
    let fixed = run(0.0, BASE, 1.0);
    // Texels more than 1% brighter than twice the scale-1 fixed point (RGB9E5 rounding lets the
    // updates stop at neighbouring values).
    let brighter = |texels: &[u32]| {
        texels
            .iter()
            .zip(&fixed)
            .filter(|&(&a, &b)| {
                let (a, b) = (
                    crate::solver::decode_rgb9e5(a),
                    crate::solver::decode_rgb9e5(b),
                );
                (0..3).any(|k| a[k] > 2.0 * b[k] * 1.01 + 1e-3)
            })
            .count()
    };
    let zero = brighter(&run(0.0, bright, 2.0));
    let kept = brighter(&run(1.0, bright, 2.0));
    eprintln!("texels kept brighter: {zero} at threshold 0, {kept} at 1");
    assert!(zero <= 10, "{zero} texels brighter at threshold 0");
    assert!(kept > 10 * zero.max(10), "{kept} texels kept brighter");
}

#[test]
fn a_threshold_of_zero_converges_at_a_scale_that_rounds() {
    // Every segment solved in every update, with drawn texels that RGB9E5 rounds after an
    // indirect scale of 1.5 (as RoomScaled's): the output still stops changing.
    let mut r = Replay::new();
    r.set(Room {
        threshold: 0.0,
        indirect_scale: 1.5,
        light: MOVED,
        ..BASE
    });
    r.converge();
}

#[test]
fn the_update_threshold_keeps_parts_that_changed_less() {
    // Converged from a brighter and from a darker state: at the default threshold of 1% the
    // outputs differ (parts whose inputs changed by less keep earlier lighting), at 0 they are
    // the same fixed point.
    let mut texels = Vec::new();
    for threshold in [1.0, 0.0] {
        let mut outputs = Vec::new();
        for start in [1.0, 3.0] {
            let mut r = Replay::new();
            r.set(Room {
                bounce: start,
                ..BASE
            });
            r.converge();
            r.set(Room {
                bounce: 2.0,
                threshold,
                ..BASE
            });
            r.converge();
            outputs.push(r.solver.encoded().to_vec());
        }
        texels.push(outputs);
    }
    assert_ne!(texels[0][0], texels[0][1]);
    assert_eq!(texels[1][0], texels[1][1]);
    // A threshold above the change of a part's inputs leaves it as it was.
    let mut r = Replay::new();
    r.set(BASE);
    r.converge();
    let before = r.solver.encoded().to_vec();
    r.set(Room {
        intensity: 2.02,
        threshold: 50.0,
        ..BASE
    });
    assert_eq!(r.solver.update(), 0);
    assert_eq!(r.solver.encoded(), &before[..]);
}

#[test]
fn reads_the_rooms_light_probe_transport() {
    let data = room_data();
    assert_eq!(data.probe_sets.len(), 1);
    let set = &data.probe_sets[0];
    assert_eq!(
        set.dependencies,
        vec![
            data.systems[0].hash,
            crate::data::ENVIRONMENT_ID,
            crate::data::ABSENT_ID
        ]
    );
    assert_eq!(set.probes.len(), 9);
    for p in &set.probes {
        assert_eq!(p.references.len(), p.weights.len());
        assert!(p.references.len() > 10);
    }
    // A probe set lit by a system the scene does not have fails.
    let mut other = set.clone();
    other.dependencies[0] = [7; 16];
    let solver = SystemSolver::new(Arc::new(data.systems[0].clone()), true).unwrap();
    assert!(ProbeSetSolver::new(Arc::new(other), std::slice::from_ref(&solver)).is_err());
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
            let _ = crate::data::read_probe_set([0; 16], part);
        }
        if f.path.ends_with(".rpc.ref") {
            assert!(crate::data::read_probe_set([0; 16], full).is_ok());
            // A coefficient order outside the scale table fails.
            let mut other = full.clone();
            other[0x44] = 9;
            assert!(crate::data::read_probe_set([0; 16], &other).is_err());
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
    // A sliver of UV area on a large face would need a chart larger than the largest UV space:
    // it fails before any mask is made.
    let sliver = [[0.0, 0.0, 0.0], [100.0, 0.0, 0.0], [0.0, 0.0, 100.0]];
    let thin = [[0.0, 0.0], [1.0, 0.0], [0.0, 1e-6]];
    let error = realtime_uvs(&Input {
        positions: &sliver,
        uvs: &thin,
        triangles: &[[0, 1, 2]],
        pixel_size: 0.5,
        min_chart_texels: 4,
    })
    .unwrap_err()
    .to_string();
    assert!(error.contains("more than 512"), "{error}");
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
    // The first update solves every segment; the updates then stop solving.
    assert_eq!(solver.update(), system.transport.segments.len());
    assert!((0..100).any(|_| solver.update() == 0));
    assert!(solver.encoded().iter().any(|&t| t != 0));
}
