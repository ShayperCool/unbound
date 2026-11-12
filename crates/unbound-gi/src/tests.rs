//! The solver against the Unity 6000.0.71f1 Editor's converged realtime lightmaps of
//! tests/projects/realtimegi (Room scene).
use crate::data::{Precomputed, System, read_precomputed};
use crate::probes::ProbeSetSolver;
use crate::solver::{
    Falloff, FalloffTable, Light, LightKind, SpotCone, SystemSolver, default_inner_spot_angle,
    gamma_to_linear,
};
use std::path::Path;
use std::sync::Arc;

fn probe() -> std::path::PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../tests/projects/realtimegi")
}

fn room_data() -> Precomputed {
    scene_data("Room")
}

/// The precomputed realtime GI of one of the probe's scenes.
fn scene_data(scene: &str) -> Precomputed {
    let file = probe().join(format!("Assets/Scenes/{scene}/LightingData.asset"));
    let bytes = std::fs::read(file).unwrap();
    let serialized = unbound_assets::serialized::SerializedFile::parse(&bytes).unwrap();
    read_precomputed(&serialized.read(112000000).unwrap())
        .unwrap()
        .expect("precomputed realtime GI")
}

/// A reference atlas: width, height, RGBA half floats (alpha 0: not covered).
fn reference(case: &str) -> (usize, Vec<[f32; 4]>) {
    reference_file(&format!("{case}.atlas"))
}

/// A reference atlas or directionality file.
fn reference_file(name: &str) -> (usize, Vec<[f32; 4]>) {
    let bytes = std::fs::read(probe().join(format!("Reference/unity-6000.0.71f1/{name}"))).unwrap();
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
    /// A gradient (Trilight) ambient instead: sky, equator and ground colors (gamma).
    gradient: Option<[[f32; 3]; 3]>,
    /// The cube the probe gives DynamicGI.SetEnvironmentData instead ([`probe_cube`]).
    cube: bool,
    point_light: bool,
    /// The spot light (intensity 3, range 9) at a position, direction and angle.
    spot: Option<([f32; 3], [f32; 3], f32)>,
    /// A directional light (color (1, 0.95, 0.8), intensity 1.2) at Euler angles x and y.
    sun: Option<(f32, f32)>,
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
    gradient: None,
    cube: false,
    point_light: true,
    spot: None,
    sun: None,
    threshold: 1.0,
};
const MOVED: [f32; 3] = [1.5, 2.5, -1.5];
/// The emissive panel's renderer (scene object file ID).
const PANEL: i64 = 1629154324;

/// The cube tests/projects/realtimegi gives DynamicGI.SetEnvironmentData (its
/// `EnvironmentData`): every face its own colors, brighter along its columns (red) and rows
/// (green).
fn probe_cube() -> Vec<[f32; 3]> {
    let size = crate::environment::RESOLUTION;
    let mut faces = Vec::with_capacity(crate::environment::FACE_VALUES);
    for face in 0..6 {
        for y in 0..size {
            for x in 0..size {
                faces.push([
                    (0.05 + 0.1 * face as f32) * (x + 1) as f32 / 4.0,
                    (0.6 - 0.1 * face as f32) * (y + 1) as f32 / 4.0,
                    0.2 * (1 + face % 2) as f32,
                ]);
            }
        }
    }
    faces
}

/// The environment values of `room`: the cube a script set, its gradient's faces (+X, -X, +Y,
/// -Y, +Z, -Z take the equator, sky and ground colors), or its flat ambient color.
fn environment(room: &Room) -> Vec<[f32; 3]> {
    let faces: Vec<[f32; 3]> = match room.gradient {
        _ if room.cube => probe_cube(),
        Some([sky, equator, ground]) => [equator, equator, sky, ground, equator, equator]
            .iter()
            .flat_map(|c| std::iter::repeat_n(c.map(gamma_to_linear), 64))
            .collect(),
        None => vec![room.ambient.map(gamma_to_linear); crate::environment::FACE_VALUES],
    };
    let mut values = vec![[0.0; 3]; crate::data::ENVIRONMENT_VALUES];
    crate::environment::environment_values(&faces, &mut values).unwrap();
    values
}

/// The scene's spot light position and rotation (x, y, z, w).
const SPOT_AT: [f32; 3] = [1.5, 3.5, -1.0];
const SPOT_ROTATION: [f32; 4] = [0.3630454, -0.2921564, 0.12101513, 0.8764691];

/// A rotation's forward vector (+z rotated by the quaternion x, y, z, w).
fn forward(q: [f32; 4]) -> [f32; 3] {
    let [x, y, z, w] = q;
    [
        2.0 * (x * z + w * y),
        2.0 * (y * z - w * x),
        1.0 - 2.0 * (x * x + y * y),
    ]
}

/// Values allowed outside the probe comparator's tolerance per case once the light moved: a few
/// isolated texels, since a part of the lightmap is solved again only when its inputs change by
/// the update threshold.
const ISOLATED: usize = 4;

/// The Room scene's system replayed through the probe's cases in order: the Editor's update
/// solves a segment again only when its inputs changed by `DynamicGI.updateThreshold`, so a
/// converged lightmap depends on the state it converged from.
struct Replay {
    data: Precomputed,
    solver: SystemSolver,
    probes: ProbeSetSolver,
    room: Option<Room>,
    /// The scene's renderers (file IDs) by the names of their objects.
    names: std::collections::HashMap<i64, String>,
}

impl Replay {
    fn new() -> Self {
        Self::scene("Room")
    }

    /// One of the probe's single-system scenes (the same room in each).
    fn scene(scene: &str) -> Self {
        let data = scene_data(scene);
        let solver = SystemSolver::new(Arc::new(data.systems[0].clone())).unwrap();
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
            names: renderer_names(scene),
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
                .map(|(object, _)| match self.names[object].as_str() {
                    "Panel" => ([0.2, 0.2, 0.2], room.panel_emission),
                    "Left" => (room.left_rendered, [0.0; 3]),
                    "Right" => ([0.1, 0.8, 0.1], [0.0; 3]),
                    "Floor" | "Ceiling" | "Back" | "Block" => (white, [0.0; 3]),
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
            let panel = self.data.renderers[system.first_renderer..][..system.renderer_count]
                .iter()
                .position(|(object, _)| self.names[object] == "Panel")
                .unwrap();
            self.solver.set_renderer_emission(panel, color).unwrap();
        }
        self.solver.set_environment(&environment(&room)).unwrap();
        // The project has no linear light intensity: the gamma colors are scaled, then
        // converted.
        let mut lights = Vec::new();
        if room.point_light {
            lights.push(Light {
                color: [1.0, 0.95, 0.85]
                    .map(|c: f32| gamma_to_linear(c * room.intensity * room.bounce)),
                kind: LightKind::Point {
                    position: room.light,
                    range: 10.0,
                    falloff: FalloffTable::of(Falloff::Legacy, 10.0),
                },
            });
        }
        // The scene's spot light (its inner angle, 21.8, is not what the built-in pipeline's
        // angular falloff uses).
        if let Some((position, direction, angle)) = room.spot {
            lights.push(Light {
                color: [0.6, 0.8, 1.0].map(|c: f32| gamma_to_linear(c * 3.0)),
                kind: LightKind::Spot {
                    position,
                    range: 9.0,
                    falloff: FalloffTable::of(Falloff::Legacy, 9.0),
                    cone: SpotCone::new(direction, angle, default_inner_spot_angle(angle)),
                },
            });
        }
        if let Some((x, y)) = room.sun {
            let (x, y) = (x.to_radians(), y.to_radians());
            let forward = [y.sin() * x.cos(), -x.sin(), y.cos() * x.cos()];
            lights.push(Light {
                color: [1.0, 0.95, 0.8].map(|c: f32| gamma_to_linear(c * 1.2)),
                kind: LightKind::Directional {
                    to_light: forward.map(|v| -v),
                },
            });
        }
        self.solver.set_lights(&lights).unwrap();
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
    /// 1% of its channel's band-0 coefficient: in the brightest case (room-bounce) some small
    /// higher-band coefficients differ by up to about 0.5% of band 0 (not from the direct light,
    /// which matches).
    fn probes(&self, case: &str) -> (usize, f32) {
        let room = self.room.unwrap();
        let environment = environment(&room);
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
    // The axis-aligned faces' normals decode to exact unit axes (bytes 0, 127 and 254), the
    // others keep their stored length.
    let axis = s
        .input
        .normals
        .iter()
        .filter(|n| n.iter().all(|&v| v == 0.0 || v.abs() == 1.0))
        .count();
    assert!(
        axis > s.input.normals.len() / 2,
        "{axis} axis-aligned normals"
    );
    assert!(
        s.input
            .normals
            .iter()
            .flatten()
            .all(|&v| (v - v.round()).abs() > 0.004 || v == v.round())
    );
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
    r.check(moved, "room-moved", 0, 0.001);
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
    // (The probe's skybox cases need the renderer; it returns to the same ambient color.)
    r.check(
        Room {
            point_light: false,
            ..ambient
        },
        "room-pointoff",
        ISOLATED,
        0.006,
    );
    // The spot light alone, from room-pointoff's state (whose isolated texels stay off, with a
    // few next to them).
    r.check(
        Room {
            point_light: false,
            spot: Some((SPOT_AT, forward(SPOT_ROTATION), 50.0)),
            ..ambient
        },
        "room-spot",
        2 * ISOLATED,
        0.006,
    );
    // The spot light at the block's side: the right wall behind the block, in its shadow, is
    // lit too (the realtime GI input has no shadows of spot lights).
    let behind = Room {
        point_light: false,
        spot: Some(([-1.5, 1.0, 0.8], [1.0, 0.0, 0.0], 60.0)),
        ..ambient
    };
    r.check(behind, "room-spot-behind", 2 * ISOLATED, 0.006);
    // A directional light through the open front, shadowed by the precomputed visibility, at
    // two directions between stored ones.
    let sun = Room {
        spot: None,
        sun: Some((40.0, 25.0)),
        ..behind
    };
    r.check(sun, "room-directional", 2 * ISOLATED, 0.006);
    r.check(
        Room {
            sun: Some((57.0, -33.0)),
            ..sun
        },
        "room-directional-turned",
        2 * ISOLATED,
        0.006,
    );
    // A new directional light without shadows (Light.shadows None) at the first direction: its
    // realtime GI is shadowed all the same.
    r.check(sun, "room-directional-noshadows", 2 * ISOLATED, 0.006);
    // The environment alone (the panel's emission set to black): a gradient ambient (its faces
    // flat, so their texel order does not matter here).
    let gradient = Room {
        sun: None,
        panel_set_emissive: Some([0.0; 3]),
        gradient: Some([[0.5, 0.6, 0.9], [0.4, 0.35, 0.3], [0.15, 0.12, 0.1]]),
        ..sun
    };
    r.check(gradient, "room-gradient", 2 * ISOLATED, 0.006);
    // A cube set by a script, whose faces are not flat: the order of a face's texels matters.
    let cube = Room {
        cube: true,
        ..gradient
    };
    r.check(cube, "room-envdata", 2 * ISOLATED, 0.006);
    // The ambient color again, as the skybox ambient mode without a skybox reads it.
    let flat = Room {
        ambient: [0.5, 0.6, 0.9],
        gradient: None,
        cube: false,
        ..cube
    };
    r.check(flat, "room-sky-none", 2 * ISOLATED, 0.006);
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
    // outputs differ (parts whose inputs changed by less keep earlier lighting), at 0 they reach
    // the same fixed point (but for the drawn texels' quantization).
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
    // Texels more than 1/256 of their largest channel apart (one or two RGB9E5 mantissa steps:
    // the quantization of the drawn texels, which the bounce reads, leaves fixed points that
    // close to each other).
    let apart = |a: &[u32], b: &[u32]| {
        a.iter()
            .zip(b)
            .filter(|&(&a, &b)| {
                let (a, b) = (
                    crate::solver::decode_rgb9e5(a),
                    crate::solver::decode_rgb9e5(b),
                );
                let step = a.iter().chain(&b).fold(0f32, |m, &v| m.max(v)) / 256.0;
                (0..3).any(|k| (a[k] - b[k]).abs() > step + 1e-6)
            })
            .count()
    };
    assert!(apart(&texels[0][0], &texels[0][1]) > 100);
    assert_eq!(apart(&texels[1][0], &texels[1][1]), 0);
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
            data.systems[0].input_hash,
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
    let solver = SystemSolver::new(Arc::new(data.systems[0].clone())).unwrap();
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
            // Blocks that cannot be (more block copies than copies, here) leave the lightmap's
            // data as it is and the system without a directionality.
            let whole = crate::data::read_transport(full).unwrap();
            assert!(whole.block_error.is_none());
            assert!(whole.segments.iter().all(|s| !s.block_normals.is_empty()));
            let mut other = full.clone();
            other[102..104].copy_from_slice(&1u16.to_le_bytes());
            let read = crate::data::read_transport(&other).unwrap();
            let error = read.block_error.clone().unwrap();
            assert!(error.contains("block copies"), "{error}");
            assert!(read.segments.iter().all(|s| s.block_normals.is_empty()
                && s.block_directions.is_empty()
                && s.block_copies == 0));
            assert!(read.block_neighbours.is_empty());
            assert_eq!(read.segments.len(), whole.segments.len());
            for (a, b) in read.segments.iter().zip(&whole.segments) {
                assert_eq!((&a.weights, &a.texels), (&b.weights, &b.texels));
            }
            // More blocks than texels cannot be: the block count places the lightmap's data too.
            let mut other = full.clone();
            let texels = u16::from_le_bytes([other[96], other[97]]);
            other[98..100].copy_from_slice(&(texels + 1).to_le_bytes());
            let error = crate::data::read_transport(&other).unwrap_err().to_string();
            assert!(error.contains("blocks for"), "{error}");
            // An odd width leaves the last column's texels outside it.
            let mut other = full.clone();
            other[80..82].copy_from_slice(&51u16.to_le_bytes());
            let error = crate::data::read_transport(&other).unwrap_err().to_string();
            assert!(error.contains("outside 51x52"), "{error}");
            let mut system = room_data().systems[0].clone();
            system.transport = read;
            let mut solver = SystemSolver::new(Arc::new(system)).unwrap();
            let error = solver.set_directional(true).unwrap_err().to_string();
            assert!(error.contains("no directionality"), "{error}");
            assert!(solver.directionality().is_none());
            solver.update();
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
    let mut solver = SystemSolver::new(system.clone()).unwrap();
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
fn material_textures_of_another_size_fail() {
    let data = room_data();
    let mut solver = SystemSolver::new(Arc::new(data.systems[0].clone())).unwrap();
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
        charts: crate::realtime_uv::Charts::Preserved,
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
            let rect = out.charts[c].rect;
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
        charts: crate::realtime_uv::Charts::Preserved,
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
        charts: crate::realtime_uv::Charts::Preserved,
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
        charts: crate::realtime_uv::Charts::Preserved,
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
            min_chart_texels: 4,
            charts: crate::realtime_uv::Charts::Preserved
        })
        .is_err()
    );
    assert!(
        realtime_uvs(&Input {
            positions: &positions,
            uvs: &uvs[..2],
            triangles: &[[0, 1, 2]],
            pixel_size: 0.1,
            min_chart_texels: 4,
            charts: crate::realtime_uv::Charts::Preserved
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
            min_chart_texels: 4,
            charts: crate::realtime_uv::Charts::Preserved
        })
        .is_err()
    );
    assert!(
        realtime_uvs(&Input {
            positions: &positions,
            uvs: &uvs,
            triangles: &[[0, 1, 2]],
            pixel_size: 0.0,
            min_chart_texels: 4,
            charts: crate::realtime_uv::Charts::Preserved
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
        charts: crate::realtime_uv::Charts::Preserved,
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
                charts: crate::realtime_uv::Charts::Preserved,
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
    let mut solver = SystemSolver::new(Arc::new(system.clone())).unwrap();
    solver
        .set_environment(&[[0.1; 3]; crate::data::ENVIRONMENT_VALUES])
        .unwrap();
    // The first update solves every segment; the updates then stop solving.
    assert_eq!(solver.update(), system.transport.segments.len());
    assert!((0..100).any(|_| solver.update() == 0));
    assert!(solver.encoded().iter().any(|&t| t != 0));
}

/// Per scene renderer object file id, its GameObject's name (from the scene's text YAML).
fn renderer_names(scene: &str) -> std::collections::HashMap<i64, String> {
    let text =
        std::fs::read_to_string(probe().join(format!("Assets/Scenes/{scene}.unity"))).unwrap();
    let mut owners = std::collections::HashMap::new();
    let mut names = std::collections::HashMap::new();
    for doc in text.split("--- !u!").skip(1) {
        let header = doc.lines().next().unwrap();
        let (class, id) = header.split_once(" &").unwrap();
        let id: i64 = id.split_whitespace().next().unwrap().parse().unwrap();
        let field = |key: &str| {
            doc.lines()
                .find_map(|l| l.trim().strip_prefix(key))
                .map(str::trim)
        };
        match class {
            "1" => {
                names.insert(id, field("m_Name:").unwrap().to_string());
            }
            "23" => {
                let owner = field("m_GameObject: {fileID:").unwrap();
                owners.insert(
                    id,
                    owner.trim_end_matches('}').trim().parse::<i64>().unwrap(),
                );
            }
            _ => {}
        }
    }
    owners
        .into_iter()
        .map(|(renderer, go)| (renderer, names[&go].clone()))
        .collect()
}

#[test]
fn a_scene_of_two_systems_converges_as_the_editors() {
    // RoomSystems: the Room with the left and right walls and the panel in a second system
    // (System Tag 1), each system's transport referencing the other's values.
    let file = probe().join("Assets/Scenes/RoomSystems/LightingData.asset");
    let bytes = std::fs::read(file).unwrap();
    let serialized = unbound_assets::serialized::SerializedFile::parse(&bytes).unwrap();
    let data = read_precomputed(&serialized.read(112000000).unwrap())
        .unwrap()
        .unwrap();
    assert_eq!(data.systems.len(), 2);
    for (k, s) in data.systems.iter().enumerate() {
        let other = data.systems[1 - k].input_hash;
        assert!(
            s.transport.dependencies.contains(&other),
            "system {k} does not reference the other"
        );
    }
    let names = renderer_names("RoomSystems");
    let scene: Vec<Arc<System>> = data.systems.iter().cloned().map(Arc::new).collect();
    let mut solvers: Vec<SystemSolver> = scene
        .iter()
        .map(|s| SystemSolver::new_in_scene(s.clone(), &scene).unwrap())
        .collect();
    let white = [0.8, 0.8, 0.8];
    for (solver, system) in solvers.iter_mut().zip(&scene) {
        let colors: Vec<([f32; 3], [f32; 3])> = data.renderers[system.first_renderer..]
            [..system.renderer_count]
            .iter()
            .map(|(object, _)| match names[object].as_str() {
                "Panel" => ([0.2, 0.2, 0.2], BASE.panel_emission),
                "Left" => (BASE.left, [0.0; 3]),
                "Right" => ([0.1, 0.8, 0.1], [0.0; 3]),
                _ => (white, [0.0; 3]),
            })
            .collect();
        let (albedo, emission) = material_textures(system, &colors);
        solver.set_albedo(&albedo).unwrap();
        solver.set_emission(&emission).unwrap();
        solver.set_environment(&environment(&BASE)).unwrap();
        solver
            .set_lights(&[Light {
                color: [1.0, 0.95, 0.85].map(|c: f32| gamma_to_linear(c * 2.0)),
                kind: LightKind::Point {
                    position: BASE.light,
                    range: 10.0,
                    falloff: FalloffTable::of(Falloff::Legacy, 10.0),
                },
            }])
            .unwrap();
    }
    let mut last: Vec<Vec<u32>> = solvers.iter().map(|s| s.encoded().to_vec()).collect();
    let mut converged = false;
    for _ in 0..1000 {
        for k in 0..solvers.len() {
            crate::solver::update_in(&mut solvers, k);
        }
        let now: Vec<Vec<u32>> = solvers.iter().map(|s| s.encoded().to_vec()).collect();
        if now == last {
            converged = true;
            break;
        }
        last = now;
    }
    assert!(converged);
    let (width, texels) = reference("roomsystems-base");
    let (mut fails, mut total) = (0, 0);
    for (solver, system) in solvers.iter().zip(&scene) {
        let t = &system.transport;
        let (ox, oy) = (
            system.atlas_offset.0 as usize,
            system.atlas_offset.1 as usize,
        );
        for y in 0..t.height as usize {
            for x in 0..t.width as usize {
                let r = texels[(oy + y) * width + ox + x];
                if r[3] <= 0.0 {
                    continue;
                }
                let got = crate::solver::decode_rgb9e5(solver.encoded()[y * t.width as usize + x]);
                for k in 0..3 {
                    total += 1;
                    if (got[k] - r[k]).abs() > 0.02 + 0.05 * r[k].abs() {
                        fails += 1;
                        eprintln!(
                            "  ({},{}) channel {k}: {} vs {}",
                            ox + x,
                            oy + y,
                            got[k],
                            r[k]
                        );
                    }
                }
            }
        }
    }
    eprintln!("roomsystems-base: {fails} of {total} values outside the tolerance");
    assert!(total > 4000, "{total} values");
    assert!(fails <= ISOLATED, "{fails} values outside the tolerance");
}

#[test]
fn references_to_systems_outside_the_scene_fail() {
    // RoomSystems' first system alone: its transport, and the probe set, read the second.
    let file = probe().join("Assets/Scenes/RoomSystems/LightingData.asset");
    let bytes = std::fs::read(file).unwrap();
    let serialized = unbound_assets::serialized::SerializedFile::parse(&bytes).unwrap();
    let data = read_precomputed(&serialized.read(112000000).unwrap())
        .unwrap()
        .unwrap();
    let scene: Vec<Arc<System>> = data.systems.iter().cloned().map(Arc::new).collect();
    let alone = std::slice::from_ref(&scene[0]);
    let error = SystemSolver::new_in_scene(scene[0].clone(), alone)
        .err()
        .unwrap()
        .to_string();
    assert!(error.contains("another scene"), "{error}");
    let error = SystemSolver::new(scene[0].clone())
        .err()
        .unwrap()
        .to_string();
    assert!(error.contains("another scene"), "{error}");
    // With both systems the probe set resolves; with one, it fails.
    let solvers: Vec<SystemSolver> = scene
        .iter()
        .map(|s| SystemSolver::new_in_scene(s.clone(), &scene).unwrap())
        .collect();
    let set = Arc::new(data.probe_sets[0].clone());
    assert!(ProbeSetSolver::new(set.clone(), &solvers).is_ok());
    let error = ProbeSetSolver::new(set, &solvers[..1])
        .err()
        .unwrap()
        .to_string();
    assert!(error.contains("another scene"), "{error}");
    // A reference past the other system's values fails.
    let mut short = (*scene[1]).clone();
    short.input.tree_children.clear();
    short.input.tree_weights.clear();
    short.input.cluster_points.truncate(2);
    let shortened = vec![scene[0].clone(), Arc::new(short)];
    let error = SystemSolver::new_in_scene(scene[0].clone(), &shortened)
        .err()
        .unwrap()
        .to_string();
    assert!(error.contains("a transport reference to value"), "{error}");
}

#[test]
fn a_directional_scene_solves_the_editors_directionality() {
    // RoomDirectional: the Room with directional realtime lightmaps, through the probe's cases.
    let mut r = Replay::scene("RoomDirectional");
    r.solver.set_directional(true).unwrap();
    let moved = Room {
        light: MOVED,
        ..BASE
    };
    let bright = Room {
        bounce: 3.0,
        ..moved
    };
    let scaled = Room {
        indirect_scale: 2.0,
        ..moved
    };
    let cases = [
        (None, BASE, "roomdirectional-base"),
        (None, moved, "roomdirectional-moved"),
        (Some(bright), scaled, "roomdirectional-indirectscale"),
        (
            Some(bright),
            Room {
                threshold: 0.0,
                ..scaled
            },
            "roomdirectional-threshold0",
        ),
        // Hardly any light (no light on, the panel black, a dark ambient color): the directions
        // of the texels that receive less than 0.001 shorten.
        (
            None,
            Room {
                point_light: false,
                panel_set_emissive: Some([0.0; 3]),
                ambient: [0.02, 0.025, 0.03],
                ..moved
            },
            "roomdirectional-dim",
        ),
    ];
    for (before, room, case) in cases {
        if let Some(before) = before {
            r.set(before);
            r.converge();
        }
        r.check(room, case, ISOLATED, 0.006);
        let (width, texels) = reference_file(&format!("{case}.dir"));
        let t = &r.solver.system().transport;
        let ours = r.solver.directionality().unwrap();
        let (mut total, mut off, mut worst, mut sum) = (0, 0, 0f32, 0f32);
        for y in 0..t.height as usize {
            for x in 0..t.width as usize {
                let e = texels[y * width + x];
                if e[3] <= 0.0 {
                    continue;
                }
                let got = ours[y * t.width as usize + x];
                for k in 0..4 {
                    let d = (got[k] as f32 - e[k] * 255.0).abs();
                    total += 1;
                    sum += d;
                    worst = worst.max(d);
                    if d > 2.5 {
                        off += 1;
                        eprintln!(
                            "  {case} ({x},{y}) channel {k}: {} vs {}",
                            got[k],
                            e[k] * 255.0
                        );
                    }
                }
            }
        }
        let mean = sum / total as f32;
        eprintln!(
            "{case}.dir: {off} of {total} values more than 2/255 off, worst {worst}, mean {mean}"
        );
        // Every covered texel's four channels: but for a few values (the update's history, as in
        // the lightmap) within 2/255 of the Editor's, 0.2/255 on average.
        assert!(total > 6000, "{case}: {total} values");
        assert!(off <= ISOLATED, "{case}: {off} directionality values off");
        assert!(
            worst < 5.0 && mean < 0.3,
            "{case}: worst {worst}, mean {mean}"
        );
    }
    // The other scenes' lightmaps have the same data: a system is directional when asked to be.
    let mut room = Replay::new();
    assert!(room.solver.directionality().is_none());
    room.solver.set_directional(true).unwrap();
    room.set(BASE);
    room.converge();
    assert!(
        room.solver
            .directionality()
            .unwrap()
            .iter()
            .any(|t| t[3] != 0)
    );
    room.solver.set_directional(false).unwrap();
    assert!(room.solver.directionality().is_none());
}

/// The span of a chart panel's plane that its lightmap UVs map to 0..1, in metres.
const PANEL_UV_SPAN: f32 = 8.0;

/// A flat panel of tests/projects/realtimegi's RoomCharts scene (RealtimeGISetup.Panel): the
/// solid cells (`#`) of a grid over `xs` x `ys` (metres; `rows` from the top one down), which
/// share their corners and so make one chart, and loose plates (x, y, width, height), each a
/// chart of its own. Lightmap UVs are the positions over `PANEL_UV_SPAN`.
fn panel(
    xs: &[f32],
    ys: &[f32],
    rows: &[&str],
    plates: &[[f32; 4]],
) -> (Vec<[f32; 3]>, Vec<[f32; 2]>, Vec<[u32; 3]>) {
    let (mut positions, mut uvs, mut triangles) = (Vec::new(), Vec::new(), Vec::new());
    let mut corner_of = std::collections::HashMap::new();
    let vertex = |positions: &mut Vec<[f32; 3]>, uvs: &mut Vec<[f32; 2]>, x: f32, y: f32| {
        positions.push([x, y, 0.0]);
        uvs.push([x / PANEL_UV_SPAN, y / PANEL_UV_SPAN]);
        positions.len() as u32 - 1
    };
    for (r, row) in rows.iter().rev().enumerate() {
        for (c, cell) in row.chars().enumerate() {
            if cell != '#' {
                continue;
            }
            let corners = [(c, r), (c + 1, r), (c + 1, r + 1), (c, r + 1)].map(|key| {
                *corner_of
                    .entry(key)
                    .or_insert_with(|| vertex(&mut positions, &mut uvs, xs[key.0], ys[key.1]))
            });
            triangles.push([corners[0], corners[2], corners[1]]);
            triangles.push([corners[0], corners[3], corners[2]]);
        }
    }
    for &[x, y, w, h] in plates {
        let corners = [(x, y), (x + w, y), (x + w, y + h), (x, y + h)]
            .map(|(px, py)| vertex(&mut positions, &mut uvs, px, py));
        triangles.push([corners[0], corners[2], corners[1]]);
        triangles.push([corners[0], corners[3], corners[2]]);
    }
    (positions, uvs, triangles)
}

/// The RoomCharts scene's panels by renderer name: grid columns, rows, cells, plates and, for a
/// mesh with a third UV channel, that channel's scale of the lightmap UVs.
#[allow(clippy::type_complexity)]
const PANELS: [(&str, &[f32], &[f32], &[&str], &[[f32; 4]], Option<[f32; 2]>); 5] = [
    // A ring whose hole takes two plates exactly.
    (
        "Ring",
        &[0.0, 0.3, 4.7, 5.0],
        &[0.0, 0.3, 3.1, 3.4],
        &["###", "#.#", "###"],
        &[[0.6, 1.1, 1.2, 1.2], [3.2, 1.1, 1.2, 1.2]],
        None,
    ),
    // The same ring with a third plate, which no hole holds once two are in.
    (
        "RingGrown",
        &[0.0, 0.3, 4.7, 5.0],
        &[0.0, 0.3, 3.1, 3.4],
        &["###", "#.#", "###"],
        &[
            [0.6, 1.1, 1.2, 1.2],
            [1.9, 1.1, 1.2, 1.2],
            [3.2, 1.1, 1.2, 1.2],
        ],
        None,
    ),
    // Two holes, the larger one first from the bottom left.
    (
        "Holes",
        &[0.0, 0.3, 2.65, 2.95, 5.3, 5.6],
        &[0.0, 0.3, 3.2, 4.1, 4.4],
        &["#####", "#.###", "#.#.#", "#####"],
        &[[0.9, 1.0, 1.2, 1.2], [3.5, 1.0, 1.2, 1.2]],
        None,
    ),
    // An arch: its pocket opens downwards.
    (
        "Arch",
        &[0.0, 0.3, 3.1, 3.4],
        &[0.0, 2.1, 2.4],
        &["###", "#.#"],
        &[[1.1, 0.4, 1.2, 1.2]],
        None,
    ),
    // A 2 x 1 m plate whose third UV channel is a square.
    (
        "Channels",
        &[0.0, 2.0],
        &[0.0, 1.0],
        &["#"],
        &[],
        Some([0.5, 1.0]),
    ),
];

/// The realtime lightmap UVs of a panel of the RoomCharts scene, from its third UV channel when
/// it has one.
fn panel_uvs(name: &str) -> crate::realtime_uv::Output {
    panel_uvs_from(name, true)
}

/// The same from the mesh's third UV channel (if any) or from its lightmap UVs alone.
fn panel_uvs_from(name: &str, third_channel: bool) -> crate::realtime_uv::Output {
    let &(_, xs, ys, rows, plates, scale) = PANELS.iter().find(|p| p.0 == name).unwrap();
    let (positions, mut uvs, triangles) = panel(xs, ys, rows, plates);
    if let Some(scale) = scale.filter(|_| third_channel) {
        for uv in &mut uvs {
            *uv = [uv[0] * scale[0], uv[1] * scale[1]];
        }
    }
    crate::realtime_uv::realtime_uvs(&crate::realtime_uv::Input {
        positions: &positions,
        uvs: &uvs,
        triangles: &triangles,
        pixel_size: 0.5,
        min_chart_texels: 4,
        charts: crate::realtime_uv::Charts::Preserved,
    })
    .unwrap()
}

/// Checks every renderer of a scene of the Room and panels: its realtime lightmap UVs lie in the
/// precomputed layout; a panel's charts use exactly the texels of the precomputed chart mask
/// (a hole that took no chart stays empty) and lie on charts numbered in their order. Returns
/// how many panels were checked.
fn check_panel_layouts(
    scene: &str,
    panel: &dyn Fn(&str) -> Option<crate::realtime_uv::Output>,
) -> usize {
    let data = scene_data(scene);
    assert_eq!(data.systems.len(), 1);
    let system = &data.systems[0];
    let names = renderer_names(scene);
    let mut panels = 0;
    for (k, &(object, _)) in data.renderers.iter().enumerate() {
        let name = names[&object].as_str();
        let made = panel(name);
        let is_panel = made.is_some();
        let out = match name {
            "Floor" | "Ceiling" => cube_uvs([6.0, 0.2, 6.0], 0.5),
            "Back" => cube_uvs([6.0, 4.0, 0.2], 0.5),
            "Left" | "Right" => cube_uvs([0.2, 4.0, 6.0], 0.5),
            "Block" => cube_uvs([1.2, 1.5, 1.2], 0.5),
            "Panel" => cube_uvs([1.0, 1.2, 0.1], 0.5),
            _ => made.unwrap_or_else(|| panic!("no panel {name}")),
        };
        let st = data.renderer_st[k];
        crate::realtime_uv::check_layout(system, st, &out)
            .unwrap_or_else(|e| panic!("{name}: {e:#}"));
        if !is_panel {
            continue;
        }
        panels += 1;
        // In these scenes' panels, the blocks no chart uses are no chart's in the precomputed
        // mask either: a hole that took no chart stays empty. (Not a rule of the check: the
        // system's packing puts other instances' charts into such blocks, as in the ceiling's.)
        let sw = system.transport.width;
        let origin =
            [st[2] * sw as f32, st[3] * system.transport.height as f32].map(|v| v.round() as u32);
        for y in 0..out.height {
            for x in 0..out.width {
                let used = out.charts.iter().any(|chart| {
                    let [left, bottom, width, height] = chart.rect;
                    x >= left
                        && x < left + width
                        && y >= bottom
                        && y < bottom + height
                        && chart.blocks[((y - bottom) / 2 * (width / 2) + (x - left) / 2) as usize]
                });
                let found = system.chart_mask[((origin[1] + y) * sw + origin[0] + x) as usize];
                assert_eq!(
                    used,
                    found != crate::data::NO_CHART,
                    "{name}: texel ({x}, {y})"
                );
            }
        }
        // The precompute numbers a renderer's charts in their order: each chart lies on its own
        // one (two equal plates in each other's places would pass the check too).
        let ids: Vec<u32> = out
            .charts
            .iter()
            .map(|chart| {
                let at = chart.blocks.iter().position(|&used| used).unwrap() as u32;
                let across = chart.rect[2] / 2;
                let (x, y) = (
                    chart.rect[0] + at % across * 2,
                    chart.rect[1] + at / across * 2,
                );
                system.chart_mask[((origin[1] + y) * sw + origin[0] + x) as usize]
            })
            .collect();
        assert!(
            ids.windows(2).all(|pair| pair[0] < pair[1]),
            "{name}: {ids:?}"
        );
    }
    panels
}

#[test]
fn charts_fill_holes_as_the_precompute_packs_them() {
    // RoomCharts: the Room with four panels whose charts leave holes and a plate with a third
    // UV channel.
    let panels = check_panel_layouts("RoomCharts", &|name: &str| {
        PANELS.iter().any(|p| p.0 == name).then(|| panel_uvs(name))
    });
    let data = scene_data("RoomCharts");
    let system = &data.systems[0];
    let names = renderer_names("RoomCharts");
    assert_eq!(panels, PANELS.len());
    let rects = |name: &str| {
        let out = panel_uvs(name);
        let rects: Vec<[u32; 4]> = out.charts.iter().map(|c| c.rect).collect();
        (out.width, out.height, rects)
    };
    // The ring's two plates lie in its hole, side by side.
    assert_eq!(
        rects("Ring"),
        (12, 8, vec![[0, 0, 12, 8], [2, 2, 4, 4], [6, 2, 4, 4]])
    );
    // With a third plate the hole cannot take them all: the UV space grows, and in the larger
    // one all three lie on top of the ring, where the skylines put them; the hole stays empty.
    assert_eq!(
        rects("RingGrown"),
        (
            12,
            12,
            vec![[0, 0, 12, 8], [0, 8, 4, 4], [4, 8, 4, 4], [8, 8, 4, 4]]
        )
    );
    // The first plate takes the hole of its own size, although the larger one comes first from
    // the bottom left; the second takes the larger hole's lowest, leftmost place.
    assert_eq!(
        rects("Holes"),
        (14, 10, vec![[0, 0, 14, 10], [8, 2, 4, 4], [2, 2, 4, 4]])
    );
    // A pocket open to the UV space's lower edge takes a plate as a hole does.
    assert_eq!(rects("Arch"), (8, 6, vec![[0, 0, 8, 6], [2, 0, 4, 4]]));
    // The plate with a third UV channel is laid out from it: a square of the plate's area (two
    // blocks a side), where its lightmap UVs would give three blocks by two.
    assert_eq!(rects("Channels"), (4, 4, vec![[0, 0, 4, 4]]));
    let k = data
        .renderers
        .iter()
        .position(|r| names[&r.0] == "Channels")
        .unwrap();
    let other = panel_uvs_from("Channels", false);
    assert_eq!((other.width, other.height), (6, 4));
    let error = crate::realtime_uv::check_layout(system, data.renderer_st[k], &other)
        .unwrap_err()
        .to_string();
    assert!(error.contains("realtime lightmap UVs of 6x4"), "{error}");
    // The ring's own blocks leave its hole out.
    let ring = &panel_uvs("Ring").charts[0];
    assert_eq!(ring.blocks.iter().filter(|&&used| !used).count(), 4 * 2);
    // A control: a plate elsewhere in the same UV space does not pass the check.
    let k = data
        .renderers
        .iter()
        .position(|r| names[&r.0] == "Ring")
        .unwrap();
    let mut moved = panel_uvs("Ring");
    moved.charts[2].rect = [2, 2, 4, 4];
    let error = crate::realtime_uv::check_layout(system, data.renderer_st[k], &moved)
        .unwrap_err()
        .to_string();
    assert!(error.contains("two charts over one"), "{error}");
    moved.charts[2].rect = [0, 0, 4, 4];
    let error = crate::realtime_uv::check_layout(system, data.renderer_st[k], &moved)
        .unwrap_err()
        .to_string();
    assert!(error.contains("did not lay out for it"), "{error}");
    // A rectangle past every bound (a damaged cache entry) is an error, not a wrapped sum.
    moved.charts[2].rect = [u32::MAX - 1, 0, 4, 4];
    let error = crate::realtime_uv::check_layout(system, data.renderer_st[k], &moved)
        .unwrap_err()
        .to_string();
    assert!(error.contains("in UVs of 12x8 texels"), "{error}");
}

/// The built-in cube's normals, by face.
const CUBE_NORMALS: [[f32; 3]; 24] = {
    let (x, y, z) = ([1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]);
    let (nx, ny, nz) = ([-1.0, 0.0, 0.0], [0.0, -1.0, 0.0], [0.0, 0.0, -1.0]);
    [
        z, z, z, z, y, y, nz, nz, y, y, nz, nz, ny, ny, ny, ny, nx, nx, nx, nx, x, x, x, x,
    ]
};

/// The limits of Optimize Realtime UVs as a new renderer has them.
const DEFAULT_LIMITS: crate::auto_charts::Params = crate::auto_charts::Params {
    max_distance: 0.5,
    max_angle: 89.0,
    ignore_normals: false,
};

/// The realtime lightmap UVs of a mesh instance with Preserve UVs off: its positions and
/// normals with `scale` applied.
fn automatic_uvs(
    positions: &[[f32; 3]],
    normals: &[[f32; 3]],
    uvs: &[[f32; 2]],
    triangles: &[[u32; 3]],
    matrix: crate::instance::Matrix,
    params: crate::auto_charts::Params,
) -> anyhow::Result<crate::realtime_uv::Output> {
    let (positions, normals) = crate::instance::apply(&matrix, positions, normals).unwrap();
    crate::realtime_uv::realtime_uvs(&crate::realtime_uv::Input {
        positions: &positions,
        uvs,
        triangles,
        pixel_size: 0.5,
        min_chart_texels: 4,
        charts: crate::realtime_uv::Charts::Automatic {
            normals: &normals,
            params,
        },
    })
}

type AutoMesh = (Vec<[f32; 3]>, Vec<[f32; 3]>, Vec<[f32; 2]>, Vec<[u32; 3]>);

/// A mesh of flat faces as the probe's `RealtimeGISetup.Solid` makes it: each face a polygon
/// with vertices and a normal of its own, looking away from `inside`, cut into a fan of
/// triangles (wound against its normal with `inverted`); its lightmap UVs lay the faces side
/// by side, each stretched over a square cell.
fn solid(inside: [f32; 3], inverted: bool, faces: &[Vec<[f32; 3]>]) -> AutoMesh {
    let sub = |a: [f32; 3], b: [f32; 3]| [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
    let dot = |a: [f32; 3], b: [f32; 3]| a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    let cross = |a: [f32; 3], b: [f32; 3]| {
        [
            a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0],
        ]
    };
    let unit = |a: [f32; 3]| {
        let length = dot(a, a).sqrt();
        a.map(|x| x / length)
    };
    let (mut positions, mut normals, mut uvs, mut triangles) =
        (Vec::new(), Vec::new(), Vec::new(), Vec::new());
    for (f, face) in faces.iter().enumerate() {
        let mut face = face.clone();
        let mut normal = unit(cross(sub(face[1], face[0]), sub(face[2], face[0])));
        if dot(normal, sub(face[0], inside)) < 0.0 {
            face.reverse();
            normal = normal.map(|x| -x);
        }
        let across = unit(sub(face[1], face[0]));
        let up = cross(normal, across);
        let flat: Vec<[f32; 2]> = face
            .iter()
            .map(|&c| [dot(sub(c, face[0]), across), dot(sub(c, face[0]), up)])
            .collect();
        let (mut min, mut max) = (flat[0], flat[0]);
        for point in &flat {
            for k in 0..2 {
                min[k] = min[k].min(point[k]);
                max[k] = max[k].max(point[k]);
            }
        }
        let first = positions.len() as u32;
        for (i, &corner) in face.iter().enumerate() {
            positions.push(corner);
            normals.push(normal);
            let cell = [0, 1].map(|k| (flat[i][k] - min[k]) / (max[k] - min[k]) * 0.9 + 0.05);
            uvs.push([
                (f as f32 + cell[0]) / faces.len() as f32,
                cell[1] / faces.len() as f32,
            ]);
        }
        for i in 1..face.len() as u32 - 1 {
            triangles.push(if inverted {
                [first, first + i + 1, first + i]
            } else {
                [first, first + i, first + i + 1]
            });
        }
    }
    (positions, normals, uvs, triangles)
}

/// A prism (`RealtimeGISetup.Prism`): a polygon in XY pushed along z, two caps and a side per
/// edge.
fn prism(section: &[[f32; 2]], depth: f32) -> Vec<Vec<[f32; 3]>> {
    let count = section.len();
    let front: Vec<[f32; 3]> = section.iter().map(|p| [p[0], p[1], 0.0]).collect();
    let back: Vec<[f32; 3]> = section.iter().rev().map(|p| [p[0], p[1], depth]).collect();
    let mut faces = vec![front, back];
    for i in 0..count {
        let (a, b) = (section[i], section[(i + 1) % count]);
        faces.push(vec![
            [a[0], a[1], 0.0],
            [a[0], a[1], depth],
            [b[0], b[1], depth],
            [b[0], b[1], 0.0],
        ]);
    }
    faces
}

/// An open roof (`RealtimeGISetup.Roof`): two rectangles falling away from a ridge on the z
/// axis by `degrees`.
fn roof(degrees: f32, width: f32, length: f32) -> Vec<Vec<[f32; 3]>> {
    let radians = degrees * (std::f32::consts::PI / 180.0);
    let side = [radians.cos() * width, -radians.sin() * width, 0.0];
    let other = [-side[0], side[1], 0.0];
    let along = [0.0, 0.0, length];
    let add = |a: [f32; 3], b: [f32; 3]| [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
    vec![
        vec![[0.0; 3], along, add(side, along), side],
        vec![[0.0; 3], other, add(other, along), along],
    ]
}

const WEDGE: [[f32; 2]; 3] = [[0.0, 0.0], [1.2, 0.0], [0.0, 0.9]];
const STEP: [[f32; 2]; 6] = [
    [0.0, 0.0],
    [1.0, 0.0],
    [1.0, 0.3],
    [0.4, 0.3],
    [0.4, 0.7],
    [0.0, 0.7],
];

/// A mesh of the RoomAuto scene, as `RealtimeGISetup.CreateRoom` makes it.
fn auto_mesh(name: &str) -> AutoMesh {
    match name {
        "Wedge" => solid([0.4, 0.3, 0.4], false, &prism(&WEDGE, 0.8)),
        "WedgeInverted" => solid([0.4, 0.3, 0.4], true, &prism(&WEDGE, 0.8)),
        "Step" => solid([0.2, 0.15, 0.45], false, &prism(&STEP, 0.9)),
        "RoofFlat" => solid([0.0, -1.0, 0.65], false, &roof(10.0, 0.7, 1.3)),
        "RoofSteep" => solid([0.0, -1.0, 0.65], false, &roof(50.0, 0.7, 1.3)),
        "RoofAngleWide" => solid([0.0, -1.0, 0.65], false, &roof(20.0, 0.7, 1.3)),
        "RoofAngleTight" => solid([0.0, -1.0, 0.65], false, &roof(20.0, 0.6, 1.3)),
        _ => panic!("no mesh {name}"),
    }
}

/// The RoomAuto scene's renderers by name: a mesh (or the built-in cube), a scale, and Max
/// Distance and Max Angle.
const AUTO_SHAPES: [(&str, Option<&str>, [f32; 3], f32, f32); 18] = [
    ("Floor", None, [6.0, 0.2, 6.0], 0.5, 89.0),
    ("Ceiling", None, [6.0, 0.2, 6.0], 0.5, 89.0),
    ("Back", None, [6.0, 4.0, 0.2], 0.5, 89.0),
    ("Left", None, [0.2, 4.0, 6.0], 0.5, 89.0),
    ("Right", None, [0.2, 4.0, 6.0], 0.5, 89.0),
    ("Block", None, [1.2, 1.5, 1.2], 0.5, 89.0),
    ("Panel", None, [1.0, 1.2, 0.1], 0.5, 89.0),
    ("Bar", None, [2.0, 0.05, 0.3], 0.5, 89.0),
    ("Wedge", Some("Wedge"), [1.0, 1.0, 1.0], 0.5, 89.0),
    ("WedgeTall", Some("Wedge"), [1.0, 1.4, 0.75], 0.5, 89.0),
    (
        "WedgeInverted",
        Some("WedgeInverted"),
        [1.0, 1.0, 1.0],
        0.5,
        89.0,
    ),
    ("Step", Some("Step"), [1.0, 1.0, 1.0], 0.5, 89.0),
    ("RoofFlat", Some("RoofFlat"), [1.0, 1.0, 1.0], 0.5, 89.0),
    ("RoofSteep", Some("RoofSteep"), [1.0, 1.0, 1.0], 0.5, 89.0),
    // The limits decide: the roof's sides are 40 degrees apart and 20 from the plane through
    // both; the step's treads, as its risers, are 0.4 apart and have corners 0.2 from the
    // plane through both.
    (
        "RoofAngleWide",
        Some("RoofAngleWide"),
        [1.0, 1.0, 1.0],
        0.5,
        30.0,
    ),
    (
        "RoofAngleTight",
        Some("RoofAngleTight"),
        [1.0, 1.0, 1.0],
        0.5,
        15.0,
    ),
    ("StepMid", Some("Step"), [1.0, 1.0, 1.2], 0.3, 89.0),
    ("StepNear", Some("Step"), [1.0, 1.0, 0.8], 0.15, 89.0),
];

/// The instance matrix of a renderer that is not turned: its scale.
fn scaled(scale: [f32; 3]) -> crate::instance::Matrix {
    [scale[0], 0.0, 0.0, 0.0, scale[1], 0.0, 0.0, 0.0, scale[2]]
}

fn auto_shape_uvs(name: &str) -> crate::realtime_uv::Output {
    let &(_, asset, scale, max_distance, max_angle) =
        AUTO_SHAPES.iter().find(|s| s.0 == name).unwrap();
    let scale = scaled(scale);
    let params = crate::auto_charts::Params {
        max_distance,
        max_angle,
        ..DEFAULT_LIMITS
    };
    match asset {
        None => automatic_uvs(
            &CUBE_POSITIONS,
            &CUBE_NORMALS,
            &CUBE_UV2,
            &CUBE_TRIANGLES,
            scale,
            params,
        ),
        Some(asset) => {
            let (positions, normals, uvs, triangles) = auto_mesh(asset);
            automatic_uvs(&positions, &normals, &uvs, &triangles, scale, params)
        }
    }
    .unwrap_or_else(|e| panic!("{name}: {e:#}"))
}

/// Checks each renderer of a scene with automatic charts that `uvs_of` makes: its charts cover
/// the texels of the precomputed chart mask, chart by chart in the precompute's order, and
/// there are `expected` of them where that is given. Returns how many were checked.
fn check_automatic_layouts(
    scene: &str,
    uvs_of: &dyn Fn(&str) -> Option<crate::realtime_uv::Output>,
    expected: &dyn Fn(&str) -> Option<usize>,
) -> usize {
    let data = scene_data(scene);
    let names = renderer_names(scene);
    let mut checked = 0;
    for (k, &(object, _)) in data.renderers.iter().enumerate() {
        let name = names[&object].as_str();
        let system = data
            .systems
            .iter()
            .find(|s| (s.first_renderer..s.first_renderer + s.renderer_count).contains(&k))
            .unwrap();
        let Some(out) = uvs_of(name) else {
            continue;
        };
        let st = data.renderer_st[k];
        crate::realtime_uv::check_layout(system, st, &out)
            .unwrap_or_else(|e| panic!("{name}: {e:#}"));
        let sw = system.transport.width;
        let origin =
            [st[2] * sw as f32, st[3] * system.transport.height as f32].map(|v| v.round() as u32);
        let ids: Vec<u32> = out
            .charts
            .iter()
            .map(|chart| {
                let at = chart.blocks.iter().position(|&used| used).unwrap() as u32;
                let across = chart.rect[2] / 2;
                let (x, y) = (
                    chart.rect[0] + at % across * 2,
                    chart.rect[1] + at / across * 2,
                );
                system.chart_mask[((origin[1] + y) * sw + origin[0] + x) as usize]
            })
            .collect();
        assert!(
            ids.windows(2).all(|pair| pair[0] < pair[1]),
            "{name}: {ids:?}"
        );
        if let Some(expected) = expected(name) {
            assert_eq!(out.charts.len(), expected, "{name}");
        }
        checked += 1;
    }
    checked
}

#[test]
fn automatic_charts_lie_where_the_precompute_put_them() {
    // RoomAuto: the Room and other shapes, every renderer with Preserve UVs off. (The scene's
    // grid of shared vertices and the built-in quad and plane are single flat charts; the
    // probe's gate draws them.)
    let uvs_of = |name: &str| {
        if !AUTO_SHAPES.iter().any(|s| s.0 == name) {
            assert!(["Grid", "Quad", "Plane"].contains(&name), "{name}");
            return None;
        }
        Some(auto_shape_uvs(name))
    };
    // How many charts the shapes that show a rule have: a slab's last end face folded into a
    // large face's chart; roofs of 20, 40 and 100 degrees between their sides, and the one of
    // 40 within a Max Angle of 30 and of 15; the step with its treads and its risers joined,
    // and with a Max Distance of 0.3 and of 0.15 a chart a face (at 0.3 their corners are
    // within the limit of a plane through both, 0.2 away, and their bounds, 0.4 apart and
    // grown by 0.18 each, do not meet).
    let expected = |name: &str| match name {
        "Floor" | "Wedge" | "Step" => Some(5),
        "RoofFlat" | "RoofAngleWide" => Some(1),
        "RoofSteep" | "RoofAngleTight" => Some(2),
        "StepMid" | "StepNear" => Some(8),
        _ => None,
    };
    assert_eq!(
        check_automatic_layouts("RoomAuto", &uvs_of, &expected),
        AUTO_SHAPES.len()
    );
}

/// A quad of its own four vertices (`RealtimeGISetup.AddQuad`): corners in order, one normal,
/// its lightmap UVs the rectangle `cell` (x, y, width, height).
fn add_quad(mesh: &mut AutoMesh, corners: [[f32; 3]; 4], normal: [f32; 3], cell: [f32; 4]) {
    let first = mesh.0.len() as u32;
    mesh.0.extend(corners);
    mesh.1.extend([normal; 4]);
    let (x0, y0, x1, y1) = (cell[0], cell[1], cell[0] + cell[2], cell[1] + cell[3]);
    mesh.2.extend([[x0, y0], [x1, y0], [x1, y1], [x0, y1]]);
    mesh.3
        .extend([[first, first + 1, first + 2], [first, first + 2, first + 3]]);
}

/// A mesh of the RoomTurned scene, as `RealtimeGISetup.CreateRoom` makes it.
fn turned_mesh(name: &str) -> AutoMesh {
    let empty = || (Vec::new(), Vec::new(), Vec::new(), Vec::new());
    match name {
        "WedgeTurned" => solid([0.4, 0.3, 0.4], false, &prism(&WEDGE, 0.8)),
        "RoofDistance" => solid([0.0, -1.0, 0.65], false, &roof(20.0, 0.7, 1.3)),
        "StepNoDistance" => solid([0.2, 0.15, 0.45], false, &prism(&STEP, 0.9)),
        "StepNoAngle" => solid([0.2, 0.15, 0.45], false, &prism(&STEP, 0.8)),
        "FoldKept" | "FoldIgnored" => {
            // A floor and a wall that share their edge's positions and UVs, not its normals,
            // unfolded side by side in UV, and a loose quad.
            let mut mesh = empty();
            let (o, z) = ([0.0, 0.0, 0.0], [0.0, 0.0, 1.0]);
            add_quad(
                &mut mesh,
                [o, z, [1.0, 0.0, 1.0], [1.0, 0.0, 0.0]],
                [0.0, 1.0, 0.0],
                [0.5, 0.1, 0.4, 0.4],
            );
            mesh.2[0..4].copy_from_slice(&[[0.5, 0.1], [0.5, 0.5], [0.9, 0.5], [0.9, 0.1]]);
            add_quad(
                &mut mesh,
                [[0.0, 1.0, 0.0], [0.0, 1.0, 1.0], z, o],
                [1.0, 0.0, 0.0],
                [0.1, 0.1, 0.4, 0.4],
            );
            mesh.2[4..8].copy_from_slice(&[[0.1, 0.1], [0.1, 0.5], [0.5, 0.5], [0.5, 0.1]]);
            add_quad(
                &mut mesh,
                [
                    [2.0, 0.0, 0.0],
                    [2.0, 0.0, 0.5],
                    [2.5, 0.0, 0.5],
                    [2.5, 0.0, 0.0],
                ],
                [0.0, 1.0, 0.0],
                [0.1, 0.6, 0.3, 0.3],
            );
            mesh
        }
        "BoxSliver" | "BoxPoint" => {
            // A box of 0.6 m and a triangle of no area of its own 1.3 m away.
            let mut mesh = empty();
            let mut cells = 0;
            for axis in 0..3 {
                for side in [-1.0f32, 1.0] {
                    let mut normal = [0.0; 3];
                    normal[axis] = side;
                    let (mut u, mut w) = ([0.0f32; 3], [0.0f32; 3]);
                    u[(axis + 1) % 3] = 0.3;
                    w[(axis + 2) % 3] = 0.3;
                    let c = normal.map(|x| x * 0.3);
                    let at = |su: f32, sw: f32| [0, 1, 2].map(|k| c[k] + su * u[k] + sw * w[k]);
                    let corners = if side > 0.0 {
                        [at(-1.0, -1.0), at(1.0, -1.0), at(1.0, 1.0), at(-1.0, 1.0)]
                    } else {
                        [at(-1.0, -1.0), at(-1.0, 1.0), at(1.0, 1.0), at(1.0, -1.0)]
                    };
                    let cell = [
                        0.05 + 0.15 * (cells % 6) as f32,
                        0.05 + 0.5 * (cells / 6) as f32,
                        0.12,
                        0.4,
                    ];
                    cells += 1;
                    add_quad(&mut mesh, corners, normal, cell);
                }
            }
            let first = mesh.0.len() as u32;
            let at = [1.3, 0.0, 0.0];
            mesh.0.extend(if name == "BoxSliver" {
                [at, [1.3, 0.2, 0.0], [1.3, 0.4, 0.0]]
            } else {
                [at; 3]
            });
            mesh.1.extend([[0.0, 0.0, 1.0]; 3]);
            mesh.2.extend([[0.95, 0.9], [0.95, 0.92], [0.95, 0.94]]);
            mesh.3.push([first, first + 1, first + 2]);
            mesh
        }
        "Many" => {
            // 11 x 10 loose quads of 0.1 m, 0.25 m apart, facing -z.
            let mut mesh = empty();
            for row in 0..10 {
                for column in 0..11 {
                    let (x, y) = (0.25 * column as f32, 0.25 * row as f32);
                    add_quad(
                        &mut mesh,
                        [
                            [x, y, 0.0],
                            [x, y + 0.1, 0.0],
                            [x + 0.1, y + 0.1, 0.0],
                            [x + 0.1, y, 0.0],
                        ],
                        [0.0, 0.0, -1.0],
                        [
                            (column as f32 + 0.1) / 11.0,
                            (row as f32 + 0.1) / 10.0,
                            0.8 / 11.0,
                            0.8 / 10.0,
                        ],
                    );
                }
            }
            mesh
        }
        _ => panic!("no mesh {name}"),
    }
}

/// The RoomTurned scene's renderers by name besides the Room's: a mesh (or the built-in cube),
/// the instance matrix (`RealtimeGI.WorldScale`, as bits), and Max Distance.
fn turned_shape(name: &str) -> Option<(Option<&str>, [u32; 9], f32)> {
    let diagonal = |x: u32, y: u32, z: u32| [x, 0, 0, 0, y, 0, 0, 0, z];
    Some(match name {
        // Turned 30 degrees about Y: the rotation is taken out exactly.
        "SlabTurned" => (None, diagonal(0x3fa66666, 0x3e4ccccd, 0x3f666666), 0.5),
        "WedgeTurned" => (
            Some(name),
            diagonal(0x3f000000, 0x3ecccccd, 0x3ee66666),
            0.5,
        ),
        // Turned (20, 40, 10), and turned 10 about X under a parent turned 45 about Y.
        "BoxTurned" => (
            None,
            [
                0x3f333333, 0x32400000, 0xb3000000, 0, 0x3f000000, 0x31c00000, 0xb3000000,
                0xb1800000, 0x3f19999a,
            ],
            0.5,
        ),
        "Child" => (
            None,
            [
                0x3f400000, 0x32000000, 0, 0xb2000000, 0x3ee66667, 0x31800000, 0, 0, 0x3f19999a,
            ],
            0.5,
        ),
        // A cube of 0.35 turned 25 degrees about Y, and one not turned: one geometry.
        "SharedTurned" | "SharedFlat" => (None, diagonal(0x3eb33333, 0x3eb33333, 0x3eb33333), 0.5),
        "RoofDistance" | "Many" => (Some(name), ONE, 0.1),
        "FoldKept" | "FoldIgnored" | "BoxSliver" | "BoxPoint" => (Some(name), ONE, 0.5),
        // Limits of zero (Max Distance 0, and Max Angle 0) keep the mesh's charts.
        "StepNoDistance" | "StepNoAngle" => (Some(name), ONE, 0.0),
        _ => return None,
    })
}

/// The instance matrix of a renderer neither turned nor scaled, as bits.
const ONE: [u32; 9] = [0x3f800000, 0, 0, 0, 0x3f800000, 0, 0, 0, 0x3f800000];

#[test]
fn automatic_charts_of_turned_renderers_lie_where_the_precompute_put_them() {
    // RoomTurned: the Room and shapes that are turned (the precompute's geometry of them, to
    // the last bit, from the instance matrix), or whose limits or parts decide their charts.
    let uvs_of = |name: &str| {
        let params = DEFAULT_LIMITS;
        let (asset, matrix, max_distance) = match turned_shape(name) {
            Some(shape) => shape,
            None => {
                let &(_, asset, scale, _, _) = AUTO_SHAPES.iter().find(|s| s.0 == name)?;
                assert!(asset.is_none(), "{name}");
                (None, scaled(scale).map(f32::to_bits), 0.5)
            }
        };
        let params = crate::auto_charts::Params {
            max_distance,
            ignore_normals: name == "FoldIgnored",
            ..params
        };
        let matrix = matrix.map(f32::from_bits);
        if max_distance == 0.0 {
            // With a limit of zero the precompute keeps the mesh's charts.
            let (positions, _, uvs, triangles) = turned_mesh(asset.unwrap());
            return Some(
                crate::realtime_uv::realtime_uvs(&crate::realtime_uv::Input {
                    positions: &positions,
                    uvs: &uvs,
                    triangles: &triangles,
                    pixel_size: 0.5,
                    min_chart_texels: 4,
                    charts: crate::realtime_uv::Charts::Preserved,
                })
                .unwrap_or_else(|e| panic!("{name}: {e:#}")),
            );
        }
        Some(
            match asset {
                None => automatic_uvs(
                    &CUBE_POSITIONS,
                    &CUBE_NORMALS,
                    &CUBE_UV2,
                    &CUBE_TRIANGLES,
                    matrix,
                    params,
                ),
                Some(asset) => {
                    let (positions, normals, uvs, triangles) = turned_mesh(asset);
                    automatic_uvs(&positions, &normals, &uvs, &triangles, matrix, params)
                }
            }
            .unwrap_or_else(|e| panic!("{name}: {e:#}")),
        )
    };
    // A box turned about all three axes, and one under a turned parent, have two pairs of
    // faces at a right angle but for the rounding of the rotation's removal joined; Ignore
    // Normals makes the fold's floor and wall one part; a Max Distance of 0.1 keeps a roof's
    // sides apart (their eaves are 0.12 from the plane through both); with one of 0 the step
    // keeps its own charts, a face each; a part without area is a chart of its own; 110 parts are 110 charts.
    let expected = |name: &str| match name {
        "SlabTurned" | "SharedTurned" | "SharedFlat" => Some(6),
        "BoxTurned" | "Child" => Some(4),
        "WedgeTurned" => Some(5),
        "RoofDistance" => Some(2),
        "FoldKept" => Some(3),
        "FoldIgnored" => Some(2),
        "StepNoDistance" | "StepNoAngle" => Some(8),
        "BoxSliver" | "BoxPoint" => Some(7),
        "Many" => Some(110),
        _ => None,
    };
    let checked = check_automatic_layouts("RoomTurned", &uvs_of, &expected);
    assert_eq!(checked, 7 + 14);
}

#[test]
fn matrices_a_unit_in_the_last_place_apart_lay_charts_out_alike() {
    // A cube scaled 2.1, and one scaled 0.3 under a parent of 7: matrices a unit in the last
    // place apart, which the managed side takes to share one geometry (alike to thousandths;
    // not measured for such a pair). The layouts made from either agree (managed
    // `RealtimeGI.LayoutsAgree`, within 1e-5 of the UV space), so both are made.
    let (near, far) = (2.1f32, 0.3f32 * 7.0);
    assert_ne!(near, far);
    let uvs = |s: f32| {
        automatic_uvs(
            &CUBE_POSITIONS,
            &CUBE_NORMALS,
            &CUBE_UV2,
            &CUBE_TRIANGLES,
            scaled([s; 3]),
            DEFAULT_LIMITS,
        )
        .unwrap()
    };
    let (a, b) = (uvs(near), uvs(far));
    assert_eq!((a.width, a.height), (b.width, b.height));
    for (p, q) in a.uvs.iter().zip(&b.uvs) {
        assert!(
            (p[0] - q[0]).abs() <= 1e-5 && (p[1] - q[1]).abs() <= 1e-5,
            "{p:?} {q:?}"
        );
    }
}

/// The RoomHoles scene's panels: two holes of one size side by side, and the same stacked,
/// each with one plate either holds.
#[allow(clippy::type_complexity)]
const HOLE_PANELS: [(&str, &[f32], &[f32], &[&str], &[[f32; 4]]); 2] = [
    (
        "TwinHoles",
        &[0.0, 0.3, 2.65, 2.95, 5.3, 5.6],
        &[0.0, 0.3, 2.8, 3.1],
        &["#####", "#.#.#", "#####"],
        &[[0.9, 0.4, 1.2, 1.2]],
    ),
    (
        "TwinHolesStacked",
        &[0.0, 0.3, 2.8, 3.1],
        &[0.0, 0.3, 2.65, 2.95, 5.3, 5.6],
        &["###", "#.#", "###", "#.#", "###"],
        &[[0.9, 0.4, 1.2, 1.2]],
    ),
];

fn hole_panel_uvs(name: &str) -> Option<crate::realtime_uv::Output> {
    let &(_, xs, ys, rows, plates) = HOLE_PANELS.iter().find(|p| p.0 == name)?;
    let (positions, uvs, triangles) = panel(xs, ys, rows, plates);
    Some(
        crate::realtime_uv::realtime_uvs(&crate::realtime_uv::Input {
            positions: &positions,
            uvs: &uvs,
            triangles: &triangles,
            pixel_size: 0.5,
            min_chart_texels: 4,
            charts: crate::realtime_uv::Charts::Preserved,
        })
        .unwrap(),
    )
}

#[test]
fn of_two_holes_of_one_size_a_chart_takes_the_one_the_precompute_does() {
    assert_eq!(
        check_panel_layouts("RoomHoles", &hole_panel_uvs),
        HOLE_PANELS.len()
    );
    let rects = |name: &str| {
        let out = hole_panel_uvs(name).unwrap();
        let rects: Vec<[u32; 4]> = out.charts.iter().map(|c| c.rect).collect();
        (out.width, out.height, rects)
    };
    // Side by side the plate takes the right hole, stacked the upper one.
    assert_eq!(
        rects("TwinHoles"),
        (14, 8, vec![[0, 0, 14, 8], [8, 2, 4, 4]])
    );
    assert_eq!(
        rects("TwinHolesStacked"),
        (8, 14, vec![[0, 0, 8, 14], [2, 8, 4, 4]])
    );
}

/// The Room scene's renderers (file IDs) and their positions.
const ROOM_POSITIONS: [(i64, [f32; 3]); 7] = [
    (152063223, [0.0, -0.1, 0.0]),
    (973394265, [0.8, 0.75, 0.8]),
    (308039783, [-3.1, 2.0, 0.0]),
    (1583214537, [3.1, 2.0, 0.0]),
    (605051979, [0.0, 2.0, 3.1]),
    (948094850, [0.0, 4.1, 0.0]),
    (PANEL, [-1.2, 0.6, 1.6]),
];

/// A renderer of the Room system as Unbound's precompute takes it: world positions and normals
/// and its realtime lightmap UVs in the system's texels (Unbound's realtime UVs placed where the
/// scene mapping puts the renderer).
struct OwnInstance {
    positions: Vec<[f32; 3]>,
    normals: Vec<[f32; 3]>,
    texels: Vec<[f32; 2]>,
}

fn room_instances(data: &Precomputed) -> Vec<OwnInstance> {
    let system = &data.systems[0];
    let (w, h) = (
        system.transport.width as f32,
        system.transport.height as f32,
    );
    (0..system.renderer_count)
        .map(|k| {
            let object = data.renderers[system.first_renderer + k].0;
            let scale = ROOM_SCALES.iter().find(|r| r.0 == object).unwrap().1;
            let at = ROOM_POSITIONS.iter().find(|r| r.0 == object).unwrap().1;
            let st = data.renderer_st[system.first_renderer + k];
            OwnInstance {
                positions: CUBE_POSITIONS
                    .iter()
                    .map(|p| std::array::from_fn(|i| p[i] * scale[i] + at[i]))
                    .collect(),
                normals: CUBE_NORMALS.to_vec(),
                texels: cube_uvs(scale, 0.5)
                    .uvs
                    .iter()
                    .map(|uv| [(uv[0] * st[0] + st[2]) * w, (uv[1] * st[1] + st[3]) * h])
                    .collect(),
            }
        })
        .collect()
}

/// Per plane of the Room's faces (normal rounded to an axis, offset in decimetres) with more than
/// 50 points: the points of `input` on it and their mean direct light from an unshadowed point
/// light at `light` (the built-in pipeline's falloff over a range of 10).
fn light_per_face(
    input: &crate::data::InputSamples,
    light: [f32; 3],
) -> std::collections::BTreeMap<([i32; 3], i32), (usize, f64)> {
    let mut out: std::collections::BTreeMap<([i32; 3], i32), (usize, f64)> = Default::default();
    for (p, n) in input.positions.iter().zip(&input.normals) {
        let axis = n.map(|v| v.round() as i32);
        let offset = (0..3).map(|k| p[k] * axis[k] as f32).sum::<f32>() * 10.0;
        let to: [f32; 3] = std::array::from_fn(|k| light[k] - p[k]);
        let distance = to.iter().map(|v| v * v).sum::<f32>().sqrt();
        let facing = ((0..3).map(|k| to[k] * n[k]).sum::<f32>() / distance).max(0.0);
        let x = distance / 10.0;
        let e = out.entry((axis, offset.round() as i32)).or_default();
        e.0 += 1;
        e.1 += (facing / (1.0 + 25.0 * x * x)) as f64;
    }
    out.retain(|_, v| v.0 > 50);
    out
}

/// The world placement of each renderer (scene file ID) of a scene without parents: position,
/// rotation (x, y, z, w) and scale.
fn renderer_transforms(
    scene: &str,
) -> std::collections::HashMap<i64, ([f32; 3], [f32; 4], [f32; 3])> {
    let text =
        std::fs::read_to_string(probe().join(format!("Assets/Scenes/{scene}.unity"))).unwrap();
    let (mut owners, mut transforms) = (
        std::collections::HashMap::new(),
        std::collections::HashMap::new(),
    );
    let numbers = |line: &str| -> Vec<f32> {
        line.split(|c| c == '{' || c == '}' || c == ',')
            .filter_map(|part| part.split(':').nth(1).and_then(|v| v.trim().parse().ok()))
            .collect()
    };
    for doc in text.split("--- !u!").skip(1) {
        let class = doc.split(' ').next().unwrap();
        let field = |key: &str| {
            doc.lines()
                .find_map(|l| l.trim().strip_prefix(key))
                .map(str::trim)
        };
        let id_of = |key: &str| {
            field(key)
                .unwrap()
                .trim_start_matches("{fileID:")
                .trim_end_matches('}')
                .trim()
                .parse::<i64>()
                .unwrap()
        };
        match class {
            "23" => {
                let id: i64 = doc
                    .split(" &")
                    .nth(1)
                    .unwrap()
                    .split_whitespace()
                    .next()
                    .unwrap()
                    .parse()
                    .unwrap();
                owners.insert(id, id_of("m_GameObject:"));
            }
            "4" => {
                assert_eq!(id_of("m_Father:"), 0, "{scene}: a transform with a parent");
                let v = |key: &str| numbers(field(key).unwrap());
                let (p, q, k) = (
                    v("m_LocalPosition:"),
                    v("m_LocalRotation:"),
                    v("m_LocalScale:"),
                );
                transforms.insert(
                    id_of("m_GameObject:"),
                    (
                        [p[0], p[1], p[2]],
                        [q[0], q[1], q[2], q[3]],
                        [k[0], k[1], k[2]],
                    ),
                );
            }
            _ => {}
        }
    }
    owners
        .into_iter()
        .map(|(r, go)| (r, transforms[&go]))
        .collect()
}

/// `p` turned by the rotation `q` (x, y, z, w).
fn rotated(q: [f32; 4], p: [f32; 3]) -> [f32; 3] {
    let [x, y, z, w] = q;
    let t = [
        2.0 * (y * p[2] - z * p[1]),
        2.0 * (z * p[0] - x * p[2]),
        2.0 * (x * p[1] - y * p[0]),
    ];
    [
        p[0] + w * t[0] + (y * t[2] - z * t[1]),
        p[1] + w * t[1] + (z * t[0] - x * t[2]),
        p[2] + w * t[2] + (x * t[1] - y * t[0]),
    ]
}

/// What Unbound's clusters of a system and the Editor's share, over the Editor's clusters of
/// the renderers Unbound clusters (`own[k]`, `None` for a renderer left out).
struct ClusterComparison {
    clusters: (usize, usize),
    points: (usize, usize),
    /// Per face plane: points and mean light per point, Unbound's and the Editor's.
    faces: Vec<(([i32; 3], i32), (usize, f64), (usize, f64))>,
    /// Editor clusters with 90% of their points in one of Unbound's, of those compared.
    within: (usize, usize),
    /// Of those, how many have the same renderer, and how many read their bounce from a texel
    /// of the Editor's cluster's points.
    same_renderer: usize,
    same_bounce: usize,
}

fn compare_clusters(
    data: &Precomputed,
    own: &[Option<(OwnInstance, Vec<[u32; 3]>)>],
) -> ClusterComparison {
    use crate::precompute::clusters::{Instance, cluster};
    let editor = &data.systems[0];
    let instances: Vec<Instance> = own
        .iter()
        .flatten()
        .map(|(i, triangles)| Instance {
            positions: &i.positions,
            normals: &i.normals,
            texels: &i.texels,
            triangles,
            cluster_size: 1.0,
            // The probe's Lightmap Parameters' Modelling Tolerance.
            modelling_tolerance: 0.01,
        })
        .collect();
    // Our instance index per system renderer, and back.
    let ours_of: Vec<Option<usize>> = own
        .iter()
        .scan(0, |n, o| {
            Some(o.as_ref().map(|_| {
                *n += 1;
                *n - 1
            }))
        })
        .collect();
    let (w, h) = (editor.transport.width, editor.transport.height);
    let ours = cluster(&instances, w, h).unwrap();
    // A cluster's renderer: its largest material group's.
    let renderer = |m: &crate::data::ClusterMaterials, c: usize| {
        let g = (m.cluster_groups[c] as usize..m.cluster_groups[c + 1] as usize)
            .max_by_key(|&g| m.groups[g].1)
            .unwrap();
        m.instance_renderers[m.groups[g].0 as usize] as usize
    };
    let compared: Vec<usize> = (0..editor.input.clusters())
        .filter(|&c| ours_of[renderer(&editor.materials, c)].is_some())
        .collect();
    let editor_points = crate::data::InputSamples {
        positions: compared
            .iter()
            .flat_map(|&c| {
                editor.input.positions[editor.input.cluster_points[c] as usize
                    ..editor.input.cluster_points[c + 1] as usize]
                    .iter()
                    .copied()
            })
            .collect(),
        normals: compared
            .iter()
            .flat_map(|&c| {
                editor.input.normals[editor.input.cluster_points[c] as usize
                    ..editor.input.cluster_points[c + 1] as usize]
                    .iter()
                    .copied()
            })
            .collect(),
        cluster_points: vec![0],
        tree_children: vec![],
        tree_weights: vec![],
    };
    let light = BASE.light;
    let (a, b) = (
        light_per_face(&ours.input, light),
        light_per_face(&editor_points, light),
    );
    let faces = a
        .iter()
        .filter_map(|(k, &x)| b.get(k).map(|&y| (*k, x, y)))
        .collect();
    let triangles: Vec<(usize, [[f32; 3]; 3])> = (0..ours.triangle_cluster.len())
        .map(|t| {
            (
                ours.triangle_cluster[t] as usize,
                ours.bvh.triangle(t as u32),
            )
        })
        .collect();
    let (mut within, mut same_renderer, mut same_bounce) = (0, 0, 0);
    for &c in &compared {
        let range =
            editor.input.cluster_points[c] as usize..editor.input.cluster_points[c + 1] as usize;
        let mut count: std::collections::HashMap<usize, usize> = Default::default();
        for p in range.clone() {
            if let Some(o) = cluster_containing(&triangles, editor.input.positions[p]) {
                *count.entry(o).or_default() += 1;
            }
        }
        let Some((&o, &most)) = count.iter().max_by_key(|x| (*x.1, std::cmp::Reverse(*x.0))) else {
            continue;
        };
        if most * 10 < range.len() * 9 {
            continue;
        }
        within += 1;
        let theirs = renderer(&editor.materials, c);
        same_renderer += (ours_of[theirs] == Some(renderer(&ours.materials, o))) as usize;
        // The texel the runtime reads for our cluster's bounce lies on the Editor's cluster: it
        // is one of the texels of its points.
        let ours_first = ours.bounce[ours.bounce_samples[o] as usize].0;
        let t = &editor.transport;
        same_bounce += t.bounce[t.bounce_samples[c] as usize..t.bounce_samples[c + 1] as usize]
            .iter()
            .any(|s| s.0 == ours_first) as usize;
    }
    ClusterComparison {
        clusters: (ours.input.clusters(), compared.len()),
        points: (ours.input.positions.len(), editor_points.positions.len()),
        faces,
        within: (within, compared.len()),
        same_renderer,
        same_bounce,
    }
}

/// The checks both scenes' comparisons pass, and what they print: `points`, the share by which
/// the number of points may differ; `light`, the share by which a large face's mean light may.
fn check_cluster_comparison(label: &str, c: &ClusterComparison, points: f32, light: f64) {
    eprintln!(
        "{label}: clusters {} (Editor {}), points {} (Editor {}); {} of {} Editor clusters with 90% of their points in one of Unbound's, {} of them of the same renderer, {} reading their bounce from a texel of the Editor's cluster",
        c.clusters.0,
        c.clusters.1,
        c.points.0,
        c.points.1,
        c.within.0,
        c.within.1,
        c.same_renderer,
        c.same_bounce
    );
    for &(face, (count, sum), (theirs, their_sum)) in &c.faces {
        let (mine, their_mean) = (sum / count as f64, their_sum / theirs as f64);
        eprintln!(
            "  {face:?}: {count} points (Editor {theirs}), light per point {mine:.4} (Editor {their_mean:.4})"
        );
    }
    // About the Editor's number of points and of clusters.
    assert!(
        (c.points.0 as f32 / c.points.1 as f32 - 1.0).abs() < points,
        "{label}"
    );
    assert!(
        (c.clusters.0 as f32 / c.clusters.1 as f32 - 1.0).abs() < 0.1,
        "{label}"
    );
    let large_light = light;
    for &(face, (count, sum), (theirs, their_sum)) in &c.faces {
        let (mine, their_mean) = (sum / count as f64, their_sum / theirs as f64);
        // The large faces within a tenth of the Editor's points and `light` of its mean direct
        // light. The small ones are the edge faces of slabs, where the two precomputes join
        // small parts to other planes (the Editor's groups of four points of two faces have a
        // normal between them), and sloped faces of few points: their mean light within 6%.
        let light = if theirs > 500 {
            assert!(
                (count as f32 / theirs as f32 - 1.0).abs() < 0.1,
                "{label} {face:?}"
            );
            large_light
        } else {
            0.06
        };
        assert!(
            (mine - their_mean).abs() <= light * their_mean.max(1e-3),
            "{label} {face:?}"
        );
    }
    // A matched cluster is of the Editor's cluster's renderer, and nearly always reads its
    // bounce from a texel of the Editor's cluster's points (the texel of most points differs
    // between two sets of points; 187 of 194 in the Room, 207 of 225 in RoomAuto).
    assert_eq!(c.same_renderer, c.within.0, "{label}");
    assert!(c.same_bounce * 10 >= c.within.0 * 9, "{label}");
}

#[test]
fn own_clusters_of_the_room_compare_with_the_editors() {
    let data = room_data();
    let own: Vec<Option<(OwnInstance, Vec<[u32; 3]>)>> = room_instances(&data)
        .into_iter()
        .map(|i| Some((i, CUBE_TRIANGLES.to_vec())))
        .collect();
    let c = compare_clusters(&data, &own);
    // Measured: 0.7% fewer points, the largest difference of a large face's light 1.4%.
    check_cluster_comparison("Room", &c, 0.02, 0.02);
    // 194 of 312: the others are the Editor's clusters of a large part and small parts of
    // other planes beside it, which Unbound joins otherwise (docs/REALTIME_GI.md).
    assert!(c.within.0 * 10 >= c.within.1 * 6);
    // No point lies where the block and the panel stand on the floor (the Editor's
    // precompute leaves those parts out).
    let own = room_instances(&data);
    let instances: Vec<crate::precompute::clusters::Instance> = own
        .iter()
        .map(|i| crate::precompute::clusters::Instance {
            positions: &i.positions,
            normals: &i.normals,
            texels: &i.texels,
            triangles: &CUBE_TRIANGLES,
            cluster_size: 1.0,
            modelling_tolerance: 0.01,
        })
        .collect();
    let ours = crate::precompute::clusters::cluster(&instances, 52, 52).unwrap();
    for p in &ours.input.positions {
        let under = |lo: [f32; 2], hi: [f32; 2]| {
            p[1].abs() < 1e-4
                && p[0] > lo[0] + 0.01
                && p[0] < hi[0] - 0.01
                && p[2] > lo[1] + 0.01
                && p[2] < hi[1] - 0.01
        };
        assert!(
            !under([0.2, 0.2], [1.4, 1.4]) && !under([-1.7, 1.55], [-0.7, 1.65]),
            "{p:?}"
        );
    }
}

#[test]
fn own_clusters_of_sloped_shapes_compare_with_the_editors() {
    // RoomAuto: the room and wedges, steps and roofs of sloped faces, each placed by its
    // scene transform (the grid, quad and plane, made otherwise, are left out, with the
    // Editor's clusters on them).
    let data = scene_data("RoomAuto");
    let system = &data.systems[0];
    let names = renderer_names("RoomAuto");
    let transforms = renderer_transforms("RoomAuto");
    let (w, h) = (
        system.transport.width as f32,
        system.transport.height as f32,
    );
    let own: Vec<Option<(OwnInstance, Vec<[u32; 3]>)>> = (0..system.renderer_count)
        .map(|k| {
            let object = data.renderers[system.first_renderer + k].0;
            let name = names[&object].as_str();
            let &(_, asset, ..) = AUTO_SHAPES.iter().find(|s| s.0 == name)?;
            let (positions, normals, triangles) = match asset {
                None => (
                    CUBE_POSITIONS.to_vec(),
                    CUBE_NORMALS.to_vec(),
                    CUBE_TRIANGLES.to_vec(),
                ),
                Some(asset) => {
                    let (p, n, _, t) = auto_mesh(asset);
                    (p, n, t)
                }
            };
            let (at, q, scale) = transforms[&object];
            let uvs = auto_shape_uvs(name);
            let st = data.renderer_st[system.first_renderer + k];
            Some((
                OwnInstance {
                    positions: positions
                        .iter()
                        .map(|p| {
                            let r = rotated(q, std::array::from_fn(|i| p[i] * scale[i]));
                            std::array::from_fn(|i| r[i] + at[i])
                        })
                        .collect(),
                    normals: normals
                        .iter()
                        .map(|n| {
                            let v = rotated(q, std::array::from_fn(|i| n[i] / scale[i]));
                            let l = (v[0] * v[0] + v[1] * v[1] + v[2] * v[2]).sqrt();
                            v.map(|x| x / l)
                        })
                        .collect(),
                    texels: uvs
                        .uvs
                        .iter()
                        .map(|uv| [(uv[0] * st[0] + st[2]) * w, (uv[1] * st[1] + st[3]) * h])
                        .collect(),
                },
                triangles,
            ))
        })
        .collect();
    assert!(own.iter().filter(|o| o.is_some()).count() >= 18);
    let c = compare_clusters(&data, &own);
    // Measured: 5.4% fewer points (the Editor's walls here have 5-7% more than in the Room and
    // than Unbound's, the floor round the shapes 6% more), the floor's light 3.7% above the
    // Editor's (docs/REALTIME_GI.md).
    check_cluster_comparison("RoomAuto", &c, 0.06, 0.04);
    assert!(c.within.0 * 10 >= c.within.1 * 6);
}

/// The cluster of `triangles` whose triangle holds `p` (within 2 mm of its plane).
fn cluster_containing(triangles: &[(usize, [[f32; 3]; 3])], p: [f32; 3]) -> Option<usize> {
    let sub = |a: [f32; 3], b: [f32; 3]| [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
    let cross = |a: [f32; 3], b: [f32; 3]| {
        [
            a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0],
        ]
    };
    let dot = |a: [f32; 3], b: [f32; 3]| a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    let mut best: Option<(f32, usize)> = None;
    for &(c, t) in triangles {
        let n = cross(sub(t[1], t[0]), sub(t[2], t[0]));
        let len = dot(n, n).sqrt();
        if len == 0.0 {
            continue;
        }
        let d = (dot(sub(p, t[0]), n) / len).abs();
        let inside =
            (0..3).all(|k| dot(cross(sub(t[(k + 1) % 3], t[k]), sub(p, t[k])), n) >= -2e-3 * len);
        if d <= 2e-3 && inside && best.is_none_or(|(bd, _)| d < bd) {
            best = Some((d, c));
        }
    }
    best.map(|b| b.1)
}
