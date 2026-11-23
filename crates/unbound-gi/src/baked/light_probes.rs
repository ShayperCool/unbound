//! The light probes a bake writes into its lighting data (the LightProbes object; matches the
//! Unity 6000.0.71f1 Editor, tests/projects/bakedbuffers `Probes*` lightingData.json): each probe
//! set's hash of its points, each probe's coefficients as Unity keeps them (from the bake's direct
//! and indirect light, [`super::probes`]) and, in the Subtractive and Shadowmask Lighting Modes,
//! how much of each mixed light reaches it.

use super::light::{Light, Mode, Tables};
use super::probes::Coefficients;
use super::spooky;
use super::trace::{Ray, Scene};

/// The factor each of the nine coefficients of the bake's light takes in Unity's: the
/// harmonic's constant times the cosine lobe's (1, 2/3, 1/4 by band).
const NORMALIZE: [f32; 9] = [
    0.282_094_78,
    0.325_734_97,
    0.325_734_97,
    0.325_734_97,
    0.273_137_1,
    0.273_137_1,
    0.078_847_89,
    0.273_137_1,
    0.136_568_55,
];
/// Where each of the bake's coefficients goes among Unity's: its first band's are x, y, z,
/// Unity's y, z, x.
const UNITY_ORDER: [usize; 9] = [0, 3, 1, 2, 4, 5, 6, 7, 8];
/// The window the direct light's first and second bands take with Remove Ringing.
const DIRECT_WINDOW: (f32, f32) = (0.922_065_97, 0.731_864);

/// The coefficients of a probe as Unity keeps them (red's nine, green's, blue's; in Unity's
/// order), of the bake's direct and indirect light: each times its factor, the direct light
/// windowed with Remove Ringing (`dering`), the two summed, and with Remove Ringing the sum's
/// bands scaled down until no direction's light is below zero.
pub fn coefficients(direct: &Coefficients, indirect: &Coefficients, dering: bool) -> [f32; 27] {
    let unity = |c: &Coefficients| -> [f32; 27] {
        let mut out = [0.0f32; 27];
        for (i, coefficient) in c.iter().enumerate() {
            for (channel, v) in coefficient.iter().enumerate() {
                out[channel * 9 + UNITY_ORDER[i]] = v * NORMALIZE[i];
            }
        }
        out
    };
    let mut d = unity(direct);
    if dering {
        scale_bands(&mut d, DIRECT_WINDOW.0, DIRECT_WINDOW.1);
    }
    let i = unity(indirect);
    let mut sum: [f32; 27] = std::array::from_fn(|k| d[k] + i[k]);
    if dering {
        let (first, second) = dering_window(&sum);
        scale_bands(&mut sum, first, second);
    }
    sum
}

/// Multiplies each channel's first band by `first` and its second by `second`.
fn scale_bands(sh: &mut [f32; 27], first: f32, second: f32) {
    for channel in 0..3 {
        for k in 1..4 {
            sh[channel * 9 + k] *= first;
        }
        for k in 4..9 {
            sh[channel * 9 + k] *= second;
        }
    }
}

/// The windows Remove Ringing scales the bands by, from (1, 1) down, in pairs between which
/// it searches.
const WINDOWS: [(f32, f32); 7] = [
    (1.0, 1.0),
    (0.970_142_25, 0.906_294_7),
    (0.940_284_55, 0.812_589_47),
    (0.921_331_4, 0.764_526_37),
    (0.902_378_2, 0.716_463_3),
    (0.829_950_63, 0.564_606_3),
    (0.757_523_06, 0.412_749_23),
];
const Y0: f32 = 0.282_094_8;
const Y1: f32 = 0.488_602_52;
const Y20: f32 = 0.315_391_57;
const SQRT3: f32 = 1.732_050_8;
const INV_SQRT3: f32 = 0.577_350_26;

/// The least of `a` and `b` as the search takes it (`b` only when below `a`).
fn least(a: f32, b: f32) -> f32 {
    if b < a { b } else { a }
}

/// The least over t in [-1, 1] of a t² + b t + c.
fn quadratic_least(a: f32, b: f32, c: f32) -> f32 {
    if a > 0.0 {
        let t = b / (a * -2.0);
        if -1.0 < t && t < 1.0 {
            return (c + t * b) + (a * t) * t;
        }
    }
    least((a + b) + c, (a - b) + c)
}

/// Below zero where a t² + b t + c + d t √(1 − t²) falls below zero for some t in [-1, 1]
/// (a value there), else above zero: a Newton search from t = −1/√2 for the least.
fn quartic_least(a: f32, b: f32, c: f32, d: f32) -> f32 {
    let at_minus = (a - b) + c;
    let at_plus = (a + b) + c;
    let mut q = None;
    if a > 0.0 {
        let t = b / (-2.0 * a);
        if -1.0 < t && t < 1.0 {
            q = Some((t * b + (t * a) * t) + c);
        }
    }
    let q = q.unwrap_or(least(at_plus, at_minus));
    if -0.5 * d + q > 0.0 {
        return 1.0;
    }
    let mut x: f32 = -0.707_107;
    let a2 = a + a;
    let mut iteration = 0;
    let mut valid;
    loop {
        let s2 = 1.0 - x * x;
        let root = s2.sqrt();
        let slope = a2 * x + b;
        let x2 = (x + x) * x;
        let q0 = (d * (1.0 - x2)) / root;
        let q1 = ((x * d) * (3.0 - x2)) / (s2 * root);
        let step = (slope + q0) / (a2 - q1);
        let next = x - step;
        let mut count = 10;
        valid = false;
        if next.abs() != f32::INFINITY && next.abs() < 3.0 {
            let f = (root * (next * d)) + ((next * b + (next * a) * next) + c);
            valid = true;
            x = next;
            count = iteration;
            if f < 0.0 {
                return f;
            }
        }
        count += 1;
        if step.abs() < 0.005 {
            count = 10;
        }
        iteration = count;
        if iteration >= 10 {
            break;
        }
    }
    if valid {
        let s = (1.0 - x * x).sqrt();
        let f = ((x * d) * s) + ((x * b + (a * x) * x) + c);
        if !(at_minus <= f) && !(at_plus <= f) {
            return f;
        }
    }
    least(at_minus, at_plus)
}

/// One channel's first band along its own direction and its second band turned so that that
/// direction is z: (z, xy, yz, z², xz, x² − y²).
fn turned(sh: &[f32]) -> (f32, f32, f32, f32, f32, f32) {
    let n = ((sh[3] * sh[3] + sh[1] * sh[1]) + sh[2] * sh[2]).sqrt();
    if !(n > 0.0) {
        return (sh[2], sh[4], sh[5], sh[6], sh[7], sh[8]);
    }
    let inv = 1.0 / n;
    let (xn, yn, zn) = (sh[3] * inv, sh[1] * inv, inv * sh[2]);
    let myn = -yn;
    let (a, b, c) = if xn.abs() > zn.abs() {
        (yn, xn, 0.0)
    } else {
        (0.0, zn, myn)
    };
    let l2 = c * c + (b * b + a * a);
    let s = if l2 > 0.0 { 1.0 / l2.sqrt() } else { l2 };
    let mxn = -xn;
    let (a, b, c) = (a * s, b * s, c * s);
    let mb = -b;
    let f66 = zn * mb - c * myn;
    let fa8 = xn * mb - a * myn;
    let f120 = a * myn - xn * b;
    let f20 = c * mxn - zn * a;
    let f114 = a * f20 - f66 * b;
    let fb8 = f120 * b - c * f20;
    let f78 = -(a * f120) - c * f66;
    let s138 = f20 * b + a * f66;
    let s198 = c * myn + zn * mb;
    let fc8 = zn * a + c * mxn;
    let s178 = b * yn + xn * a;
    let f118 = a * mb;
    let f11c = f20 * f120;
    let f124 = c * c;
    let fd8 = (f120 * SQRT3) * c;
    let fe8 = f66 * f120;
    let fc4 = ((c * a + fe8) - (xn * -2.0) * zn) * INV_SQRT3;
    let b2 = b * b;
    let f88 = xn * f20 - f66 * myn;
    let f98 = zn * f20 + f120 * myn;
    let s188 = f66 * f20 - f118;
    let s1c8 = mb * c - f11c;
    let f1d8 = f120 * f120 - f124;
    let (p4, p5, p6, p7, p8) = (sh[4], sh[5], sh[6], sh[7], sh[8]);
    let rz = (sh[3] * xn + sh[1] * yn) + zn * sh[2];
    let r4 = (((p8 * s138 + p7 * f78) + fb8 * p5) + f114 * p4) + fd8 * p6;
    let r5 = (((p8 * s178 + p7 * fc8) + s198 * p5) + fa8 * p4) + ((c * -SQRT3) * zn) * p6;
    let w =
        ((((xn * xn + xn * xn) + (-(f66 * f66) - a * a)) + f20 * f20) + b2) - (yn * yn + yn * yn);
    let r6 = ((((w * 0.288_675_13) * p8 + p7 * fc4)
        + (((mb * c + f11c) - (yn * -2.0) * zn) * INV_SQRT3) * p5)
        + (((f118 + f66 * f20) - (xn * -2.0) * myn) * -INV_SQRT3) * p4)
        + (((zn * zn + zn * zn) + (-(f120 * f120) - f124)) * 0.5) * p6;
    let r7 = ((((f20 * myn + f66 * xn) * p8 + (zn * f66 + f120 * mxn) * p7)
        + ((f120 * -SQRT3) * zn) * p6)
        + f98 * p5)
        + f88 * p4;
    let w8 = ((f66 * f66 - a * a) - f20 * f20) + b2;
    let r8 = ((((w8 * 0.5) * p8 + (c * a - fe8) * p7) + (f1d8 * 0.866_025_4) * p6) + s1c8 * p5)
        + s188 * p4;
    (rz, r4, r5, r6, r7, r8)
}

/// The window (first band, second band) one channel's nine coefficients need for the light
/// they give no direction to fall below zero: its zonal part's, then its other part's within it.
fn channel_window(sh: &[f32]) -> (f32, f32) {
    let (rz, r4, r5, r6, r7, r8) = turned(sh);
    let m1 = (r7 * r7 + r5 * r5).sqrt();
    let m2 = (r8 * r8 + r4 * r4).sqrt();
    let bz = rz * Y1;
    let az = (r6 * Y20) * 3.0;
    let cz = r6 * -Y20;
    let dc = sh[0] * Y0;
    let (mut w1, mut w2) = (1.0f32, 1.0f32);
    if quadratic_least(az, bz, cz + dc) <= 0.0 {
        let first = (0..WINDOWS.len()).find(|&j| {
            let (w1j, w2j) = WINDOWS[j];
            quadratic_least(az * w2j, bz * w1j, cz * w2j + dc) > 0.0
        });
        match first {
            None => (w1, w2) = WINDOWS[6],
            Some(0) => (w1, w2) = WINDOWS[0],
            Some(j) => {
                let ((p1, p2), (w1j, w2j)) = (WINDOWS[j - 1], WINDOWS[j]);
                let (mut lo, mut hi) = (0.0f32, 1.0f32);
                for _ in 0..5 {
                    let m = (lo + hi) * 0.5;
                    let w2m = m * w2j + (1.0 - m) * p2;
                    let w1m = bz * (m * w1j + (1.0 - m) * p1);
                    if quadratic_least(az * w2m, w1m, w2m * cz + dc) < 0.0 {
                        lo = m;
                    } else {
                        hi = m;
                    }
                }
                let r = 1.0 - hi;
                (w1, w2) = (w1j * hi + p1 * r, w2j * hi + p2 * r);
            }
        }
    }
    if !(m1 > 0.0 || m2 > 0.0) {
        return (w1, w2);
    }
    let e = (m1 * w2) * 1.092_548_5;
    let m = (m2 * w2) * 0.546_274_24;
    let b = bz * w1;
    let a = az * w2 + m;
    let c = cz * w2 - m;
    if quartic_least(a, b, c + dc, e) > 0.0 {
        return (w1, w2);
    }
    let Some(j) = (1..WINDOWS.len()).find(|&j| {
        let (w1j, w2j) = WINDOWS[j];
        quartic_least(a * w2j, b * w1j, c * w2j + dc, e * w2j) > 0.0
    }) else {
        return (w1 * WINDOWS[6].0, w2 * WINDOWS[6].1);
    };
    let ((p1, p2), (w1j, w2j)) = (WINDOWS[j - 1], WINDOWS[j]);
    let (mut lo, mut hi) = (0.0f32, 1.0f32);
    for _ in 0..5 {
        let mid = (lo + hi) * 0.5;
        let w2m = w2j * mid + (1.0 - mid) * p2;
        let w1m = (w1j * mid + p1 * (1.0 - mid)) * b;
        if quartic_least(a * w2m, w1m, c * w2m + dc, w2m * e) < 0.0 {
            lo = mid;
        } else {
            hi = mid;
        }
    }
    let r = 1.0 - hi;
    (w1 * (w1j * hi + p1 * r), w2 * (w2j * hi + p2 * r))
}

/// The window of a probe: the least of its channels' (those with light above 0.001).
fn dering_window(sh: &[f32; 27]) -> (f32, f32) {
    let (mut w1, mut w2) = (1.0f32, 1.0f32);
    for channel in 0..3 {
        let c = &sh[channel * 9..channel * 9 + 9];
        if !(c[0] > 0.001) {
            continue;
        }
        let (a, b) = channel_window(c);
        w1 = least(w1, a);
        w2 = least(w2, b);
    }
    (w1, w2)
}

/// The hash of a probe set's points (as Unity keys the set in its lighting data): SpookyHash of
/// the points as four floats each (the fourth 0), with which as the seed the key of 42 is
/// hashed again.
pub fn set_hash(points: &[[f32; 3]]) -> [u8; 16] {
    let bytes: Vec<u8> = points
        .iter()
        .flat_map(|p| [p[0], p[1], p[2], 0.0])
        .flat_map(f32::to_le_bytes)
        .collect();
    let seed = spooky::hash128(&bytes, (0, 0));
    let mut key = [0u8; 16];
    key[..8].copy_from_slice(&42u64.to_le_bytes());
    key[8..].copy_from_slice(&0xffff_ffff_0000_0000u64.to_le_bytes());
    let (h1, h2) = spooky::hash128(&key, seed);
    let mut out = [0u8; 16];
    out[..8].copy_from_slice(&h1.to_le_bytes());
    out[8..].copy_from_slice(&h2.to_le_bytes());
    out
}

/// How close two probes may be before the bake keeps only one of them: 5 cm (squared).
const DUPLICATE: f32 = 0.002_500_000_2;

/// Whether two of the probes are within 5 cm of each other (the bake keeps only the first of
/// them; not measured).
pub fn has_duplicates(points: &[[f32; 3]]) -> bool {
    let cell = |p: [f32; 3]| p.map(|v| (v / 0.05).floor() as i64);
    let mut cells: std::collections::HashMap<[i64; 3], Vec<usize>> = Default::default();
    for (i, &p) in points.iter().enumerate() {
        let [x, y, z] = cell(p);
        for dx in -1..=1 {
            for dy in -1..=1 {
                for dz in -1..=1 {
                    for &j in cells.get(&[x + dx, y + dy, z + dz]).into_iter().flatten() {
                        let d = [
                            p[0] - points[j][0],
                            p[1] - points[j][1],
                            p[2] - points[j][2],
                        ];
                        if (d[2] * d[2] + d[1] * d[1]) + d[0] * d[0] < DUPLICATE {
                            return true;
                        }
                    }
                }
            }
        }
        cells.entry([x, y, z]).or_default().push(i);
    }
    false
}

/// A probe's occlusion entry: up to four mixed lights (their places in the bake's light list,
/// -1 for none), their shadowmask channels (255 for none) and how much of each reaches it.
#[derive(Clone, Debug, PartialEq)]
pub struct Occlusion {
    pub lights: [i32; 4],
    pub channels: [u8; 4],
    pub occlusion: [f32; 4],
}

/// The mixed lights a probe's occlusion lists: those with a shadowmask channel, the first four
/// in the bake's order (their places in it and their channels).
pub fn occluding_lights(lights: &[Light], channels: &[Option<u8>]) -> Vec<(usize, u8)> {
    lights
        .iter()
        .zip(channels)
        .enumerate()
        .filter_map(|(i, (l, c))| (l.mode == Mode::Mixed).then_some((i, (*c)?)))
        .take(4)
        .collect()
}

/// Each probe's occlusion entry: of each of [`occluding_lights`], 1 where its light reaches the
/// probe (within its range and a spot light's cone) unshadowed, else 0. (Measured with one
/// mixed spot light, its probes unshadowed: tests/projects/bakedbuffers `ProbesMore`,
/// `ProbesSky*`; a shadowed probe and several lights are not measured.)
pub fn occlusion(
    positions: &[[f32; 3]],
    lights: &[Light],
    channels: &[Option<u8>],
    casters: &Scene,
) -> Vec<Occlusion> {
    let tables = Tables::new();
    let listed = occluding_lights(lights, channels);
    positions
        .iter()
        .map(|&p| {
            let mut entry = Occlusion {
                lights: [-1; 4],
                channels: [255; 4],
                occlusion: [0.0; 4],
            };
            for (slot, &(index, channel)) in listed.iter().enumerate() {
                let light = &lights[index];
                entry.lights[slot] = index as i32;
                entry.channels[slot] = channel;
                let reaches =
                    light
                        .arriving_around(&tables, light.color, p)
                        .is_some_and(|arriving| {
                            !(light.casts_shadows
                                && casters.occluded(
                                    &Ray {
                                        origin: p,
                                        direction: arriving.direction,
                                    },
                                    arriving.distance,
                                ))
                        });
                entry.occlusion[slot] = if reaches { 1.0 } else { 0.0 };
            }
            entry
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::baked::light::Kind;

    const SCENES: [&str; 6] = [
        "Probes",
        "ProbesGrid",
        "ProbesMore",
        "ProbesReach",
        "ProbesSky",
        "ProbesSkyEven",
    ];

    fn reference(scene: &str, name: &str) -> std::path::PathBuf {
        std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../tests/projects/bakedbuffers/Reference/unity-6000.0.71f1")
            .join(scene)
            .join(name)
    }

    /// The LightProbes object of the Editor's lighting data (as `unbound dump` gives it).
    fn light_probes(scene: &str) -> serde_json::Value {
        let text = std::fs::read_to_string(reference(scene, "lightingData.json")).unwrap();
        let objects: Vec<serde_json::Value> = serde_json::from_str(&text).unwrap();
        objects.into_iter().find(|o| o["classId"] == 258).unwrap()["fields"].clone()
    }

    fn number(v: &serde_json::Value) -> f32 {
        v.as_str().unwrap().parse().unwrap()
    }

    /// A bake's probe buffer (its probes' count, then per coefficient each probe's four
    /// floats): per probe its nine coefficients.
    fn buffer(scene: &str, name: &str) -> Vec<Coefficients> {
        let data = std::fs::read(reference(scene, name)).unwrap();
        let n = u64::from_le_bytes(data[..8].try_into().unwrap()) as usize;
        let at = |i: usize| f32::from_le_bytes(data[8 + 4 * i..12 + 4 * i].try_into().unwrap());
        (0..n)
            .map(|p| std::array::from_fn(|c| std::array::from_fn(|k| at(4 * (c * n + p) + k))))
            .collect()
    }

    #[test]
    fn the_editors_probe_coefficients_are_made_of_its_buffers() {
        // (The buffers and the lighting data come from two Editor bakes, whose sums differ in
        // their last bits: within 1e-5. Every group of these scenes removes ringing.)
        for scene in SCENES {
            let (direct, indirect) = (
                buffer(scene, "radianceDirect0.shl2"),
                buffer(scene, "radianceIndirect0.shl2"),
            );
            let expected = light_probes(scene)["m_BakedCoefficients"]
                .as_array()
                .unwrap()
                .clone();
            assert_eq!(expected.len(), direct.len(), "{scene}");
            for (p, want) in expected.iter().enumerate() {
                let made = coefficients(&direct[p], &indirect[p], true);
                for (k, v) in made.iter().enumerate() {
                    let w = number(&want[format!("sh[{k:2}]")]);
                    assert!(
                        (v - w).abs() <= 1e-5,
                        "{scene} probe {p} sh[{k}]: {v}, expected {w}"
                    );
                }
            }
        }
    }

    #[test]
    fn the_editors_probe_set_hashes_are_of_their_points() {
        for scene in SCENES {
            let probes = light_probes(scene);
            let data = &probes["m_Data"];
            let positions: Vec<[f32; 3]> = data["m_Positions"]
                .as_array()
                .unwrap()
                .iter()
                .map(|p| [number(&p["x"]), number(&p["y"]), number(&p["z"])])
                .collect();
            for set in data["m_ProbeSets"].as_array().unwrap() {
                let (offset, size) = (
                    number(&set["m_Offset"]) as usize,
                    number(&set["m_Size"]) as usize,
                );
                let expected: Vec<u8> = (0..16)
                    .map(|i| number(&set["m_Hash"][format!("bytes[{i}]")]) as u8)
                    .collect();
                assert_eq!(
                    set_hash(&positions[offset..offset + size]).to_vec(),
                    expected,
                    "{scene} set at {offset}"
                );
            }
        }
    }

    #[test]
    fn without_remove_ringing_the_light_is_only_summed() {
        let mut direct = Coefficients::default();
        let mut indirect = Coefficients::default();
        direct[0] = [1.0, 2.0, 3.0];
        direct[1] = [4.0, 0.0, 0.0];
        indirect[3] = [0.0, 0.0, 2.0];
        let made = coefficients(&direct, &indirect, false);
        // L0 by its factor; raw x (index 1) is Unity's third, raw z (index 3) its second.
        assert_eq!(made[0], 1.0 * NORMALIZE[0]);
        assert_eq!(made[18], 3.0 * NORMALIZE[0]);
        assert_eq!(made[3], 4.0 * NORMALIZE[1]);
        assert_eq!(made[2 * 9 + 2], 2.0 * NORMALIZE[3]);
        // With it, a light that falls below zero somewhere has its bands scaled down.
        let ringing = coefficients(&direct, &indirect, true);
        assert_eq!(ringing[0], made[0]);
        assert!(ringing[3].abs() < made[3].abs());
    }

    #[test]
    fn probes_within_five_centimetres_are_duplicates() {
        assert!(!has_duplicates(&[
            [0.0; 3],
            [0.05, 0.0, 0.0],
            [1.0, 1.0, 1.0]
        ]));
        assert!(has_duplicates(&[
            [0.0; 3],
            [1.0, 1.0, 1.0],
            [0.03, 0.03, 0.0]
        ]));
        // Across a cell's border too.
        assert!(has_duplicates(&[[0.049, 0.0, 0.0], [0.051, 0.0, 0.0]]));
    }

    fn spot(mode: Mode) -> Light {
        // At the origin, shining along -y, 10 m range, a 60° cone.
        Light {
            kind: Kind::Spot,
            mode,
            color: [1.0; 3],
            indirect_color: [1.0; 3],
            position: [0.0; 3],
            forward: [0.0, -1.0, 0.0],
            right: [1.0, 0.0, 0.0],
            range: 10.0,
            cone_angle: 60f32.to_radians(),
            casts_shadows: true,
        }
    }

    #[test]
    fn a_probe_is_occluded_outside_a_mixed_lights_reach_or_in_its_shadow() {
        let lights = [spot(Mode::Baked), spot(Mode::Mixed)];
        let channels = [None, Some(0)];
        let probes = [
            [0.0, -2.0, 0.0],  // in the cone
            [0.0, -20.0, 0.0], // beyond the range
            [5.0, -1.0, 0.0],  // outside the cone
        ];
        let open = occlusion(&probes, &lights, &channels, &Scene::new([]));
        // Only the mixed light with a channel is listed, by its place in the bake's lights.
        assert!(
            open.iter()
                .all(|e| e.lights == [1, -1, -1, -1] && e.channels == [0, 255, 255, 255])
        );
        assert_eq!(
            open.iter().map(|e| e.occlusion[0]).collect::<Vec<_>>(),
            [1.0, 0.0, 0.0]
        );
        // A floor between the light and the first probe shadows it (Unbound's rule: a shadowed
        // probe is not measured).
        let floor = Scene::new([
            [[-5.0, -1.0, -5.0], [5.0, -1.0, 5.0], [5.0, -1.0, -5.0]],
            [[-5.0, -1.0, -5.0], [-5.0, -1.0, 5.0], [5.0, -1.0, 5.0]],
        ]);
        assert_eq!(
            occlusion(&probes[..1], &lights, &channels, &floor)[0].occlusion[0],
            0.0
        );
    }
}
