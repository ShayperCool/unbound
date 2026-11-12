//! Shader targets and GPU feature requirements: the `SHADER_TARGET`, `SHADER_TARGET_AVAILABLE` and
//! `SHADER_AVAILABLE_<FEATURE>` defines a variant of a program compiles with, from its
//! `#pragma target` and `#pragma require` lines and its keywords.
//!
//! The Unity 6000.0 Manual (Targeting shader models and GPU features in HLSL) documents that a
//! line may name keywords (`#pragma target 4.0 EXAMPLE_KEYWORD`, `#pragma require mrt8 :
//! EXAMPLE_KEYWORD`) and then applies to the variants with any of them, and that a program may
//! have several lines. How the lines combine, which features each target means and what the
//! defines are is as the Unity 6000.0.71f1 Editor compiles it on its OpenGL Core and Vulkan
//! devices (tests/projects/shadertargets, measured on the NVIDIA driver only):
//!
//! - a variant requires the features of every line that applies to it (the lines without
//!   keywords, and those naming one of its keywords) and of the built-in keywords it has
//!   ([`KEYWORD_REQUIREMENTS`]); a program without a plain `#pragma target` requires 2.5's;
//! - the lines without keywords leave out the geometry and tessellation stages' features
//!   (Unbound compiles no such stages): plain 4.0 compiles as 3.5, 4.6 and 5.0 as 4.5, while a
//!   line with keywords keeps them (`#pragma target 4.0 KW` is 4.0 for those variants);
//! - `SHADER_TARGET` is the highest target whose features the requirements hold, raised by some
//!   features alone ([`shader_target`]);
//! - the `SHADER_AVAILABLE_*` defines are the requirements' features and the device's own
//!   ([`device_floor`]), without the stage features, and `SHADER_TARGET_AVAILABLE` their target;
//! - a variant requiring a feature the device does not have draws with the error shader
//!   ([`device_lacks`]).
//!
//! Where this differs from the Manual's pragma target reference: 4.6 and 5.0 bring no cube
//! arrays, and the `es3.0`, `es3.1` and `gl4.1` targets apply nothing on these devices.

use unbound_compat::GraphicsDevice;

/// The GPU features Unity's `#pragma require` names, in an order of our own: a feature's bit is
/// its index. `SHADER_AVAILABLE_<NAME>` is defined for each name (`interpolators15` and
/// `integers` are one feature with two); [`ALIASES`] are further names `#pragma require` takes.
const FEATURES: [&[&str]; 35] = [
    &["interpolators10"],
    &["interpolators32"],
    &["mrt4"],
    &["mrt8"],
    &["derivatives"],
    &["samplelod"],
    &["fragcoord"],
    &["interpolators15", "integers"],
    &["2darray"],
    &["instancing"],
    &["geometry"],
    &["cubearray"],
    &["compute"],
    &["randomwrite"],
    &["tesshw"],
    &["tessellation"],
    &["sparsetex"],
    &["framebufferfetch"],
    &["msaatex"],
    &["setrtarrayindexfromanyshader"],
    &["wavebasic"],
    &["wavevote"],
    &["waveballot"],
    &["wavemath"],
    &["wavemultiprefix"],
    &["quadshuffle"],
    &["int64"],
    &["native16bit"],
    &["barycentrics"],
    &["int64bufferatomics"],
    &["int64groupsharedatomics"],
    &["waveshuffle"],
    &["inlineraytracing"],
    // Not nameable: what every target has (2.0 and up), and a part of 3.5's set no feature name
    // gives (`#pragma require` of 3.5's named features over 3.0 does not make it 3.5).
    &[],
    &[],
];

/// Names `#pragma require` takes for a feature that define nothing of their own (no
/// `SHADER_AVAILABLE_UAV`: tests/projects/shadertargets).
const ALIASES: [(&str, &str); 2] = [("uav", "randomwrite"), ("fbfetch", "framebufferfetch")];

/// A set of GPU features.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Hash)]
pub struct Features(u64);

const fn bit(index: usize) -> u64 {
    1 << index
}

const MRT4: u64 = bit(2);
const DERIVATIVES: u64 = bit(4);
const ARRAY2D: u64 = bit(8);
const INSTANCING: u64 = bit(9);
const GEOMETRY: u64 = bit(10);
const COMPUTE: u64 = bit(12);
const RANDOMWRITE: u64 = bit(13);
const TESSHW: u64 = bit(14);
const TESSELLATION: u64 = bit(15);
const SPARSETEX: u64 = bit(16);
const MSAATEX: u64 = bit(18);
const BASELINE: u64 = bit(33);
const PART_OF_35: u64 = bit(34);

/// The stages' features: a program without geometry or hull stages compiles without them.
const STAGES: u64 = GEOMETRY | TESSHW | TESSELLATION;

const T20: u64 = BASELINE;
const T25: u64 = T20 | DERIVATIVES;
const T30: u64 = T25 | bit(0) | bit(5) | bit(6);
const T35: u64 = T30 | MRT4 | bit(7) | ARRAY2D | INSTANCING | PART_OF_35;
const T40: u64 = T35 | GEOMETRY;
const T45: u64 = T35 | COMPUTE | RANDOMWRITE | MSAATEX;
const T46: u64 = T40 | TESSHW | TESSELLATION | MSAATEX;
const T50: u64 = T45 | T46;

/// Each `#pragma target` value's features, by `SHADER_TARGET`'s number, with the name that
/// spells it. A 4.6 or 5.0 target has no cube arrays (`#pragma require cubearray` adds them).
/// Other values (the platform ones, `es3.0`, `es3.1`, `gl4.1`) apply nothing on the measured
/// devices: the program is as without the line.
const TARGETS: [(u32, &[&str], u64); 8] = [
    (20, &["2.0"], T20),
    (25, &["2.5"], T25),
    (30, &["3.0"], T30),
    (35, &["3.5"], T35),
    (40, &["4.0"], T40),
    (45, &["4.5"], T45),
    (46, &["4.6"], T46),
    (50, &["5.0"], T50),
];

/// `#pragma target` values of particular platforms, which apply nothing on the measured devices.
const PLATFORM_TARGETS: [&str; 3] = ["es3.0", "es3.1", "gl4.1"];

/// Features that raise `SHADER_TARGET` on their own to a target, whatever else the
/// requirements hold (`#pragma require compute` over 2.5 is 4.5; `integers` or `cubearray`
/// raise nothing).
const RAISES: [(u64, u32); 6] = [
    (MRT4, 30),
    (ARRAY2D | INSTANCING, 35),
    (GEOMETRY, 40),
    (COMPUTE | MSAATEX, 45),
    (TESSELLATION, 46),
    (SPARSETEX, 50),
];

/// Built-in keywords that bring requirements of their own to the variants that have them
/// (measured but `INSTANCING_ON`, whose variant the probe's draws do not select: it is taken as
/// the instancing feature, which `#pragma require instancing` measures). The stereo and device
/// capability keywords (`STEREO_INSTANCING_ON`, `UNITY_DEVICE_SUPPORTS_NATIVE_16BIT`, ...) are not
/// listed: Unbound enables none of them. `MESH_INSTANCE_REPLAY` is Unbound's (a draw replaying
/// mesh instances), drawn as instancing.
const KEYWORD_REQUIREMENTS: [(&str, u64); 7] = [
    ("SHADOWS_SCREEN", T30),
    ("SHADOWS_SOFT", T30),
    ("DIRLIGHTMAP_COMBINED", T30),
    ("DYNAMICLIGHTMAP_ON", T30),
    ("INSTANCING_ON", INSTANCING),
    ("PROCEDURAL_INSTANCING_ON", COMPUTE),
    (crate::MESH_INSTANCE_REPLAY, INSTANCING),
];

/// The features a device does not have: a variant requiring one draws with the error shader
/// (measured: sparse textures on OpenGL Core and Vulkan; the wave, 64-bit, 16-bit, barycentric
/// and ray tracing features are unmeasured and taken as present).
fn device_lacks(_device: GraphicsDevice) -> u64 {
    SPARSETEX
}

impl Features {
    /// A `#pragma target` value's features (none for a value Unity does not name).
    pub fn of_target(value: &str) -> Option<Features> {
        TARGETS
            .iter()
            .find(|(_, names, _)| names.contains(&value))
            .map(|&(_, _, set)| Features(set))
    }

    /// A `#pragma require` feature name's (in any case).
    pub fn of_name(name: &str) -> Option<Features> {
        let name = name.to_ascii_lowercase();
        let name = ALIASES
            .iter()
            .find(|(alias, _)| *alias == name)
            .map_or(name.as_str(), |(_, feature)| feature);
        FEATURES
            .iter()
            .position(|names| names.contains(&name))
            .map(|k| Features(bit(k)))
    }

    fn has(self, set: u64) -> bool {
        self.0 & set == set
    }

    /// The `SHADER_AVAILABLE_<NAME>` defines of the features, in [`FEATURES`]' order.
    pub fn names(self) -> impl Iterator<Item = &'static str> {
        FEATURES
            .iter()
            .enumerate()
            .filter(move |(k, _)| self.0 & bit(*k) != 0)
            .flat_map(|(_, names)| names.iter().copied())
    }
}

impl std::ops::BitOr for Features {
    type Output = Features;
    fn bitor(self, other: Features) -> Features {
        Features(self.0 | other.0)
    }
}

impl std::ops::BitOrAssign for Features {
    fn bitor_assign(&mut self, other: Features) {
        self.0 |= other.0;
    }
}

/// `SHADER_TARGET` of requirements: the highest target whose features they hold, raised by the
/// features that raise it on their own ([`RAISES`]); compute with tessellation is 5.0. Without
/// any (not even 2.0's), 3.0.
pub fn shader_target(features: Features) -> u32 {
    let held = TARGETS
        .iter()
        .filter(|(_, _, set)| features.has(*set))
        .map(|&(target, _, _)| target);
    let raised = RAISES
        .iter()
        .filter(|(set, _)| features.0 & set != 0)
        .map(|&(_, target)| target);
    let both = features.has(COMPUTE | TESSELLATION).then_some(50);
    held.chain(raised).chain(both).max().unwrap_or(30)
}

/// The features a device compiles every program with, beside the program's requirements
/// (measured: OpenGL Core 3.5's, Vulkan 4.5's; Metal, which Unbound does not draw, unmeasured:
/// taken as OpenGL Core's).
fn device_floor(device: GraphicsDevice) -> Features {
    Features(match device {
        GraphicsDevice::Vulkan => T45,
        GraphicsDevice::OpenGLCore | GraphicsDevice::Metal => T35,
    })
}

/// One `#pragma target` or `#pragma require` line: its features and the keywords it is limited
/// to (none: every variant).
#[derive(Debug, Clone, PartialEq)]
struct Line {
    features: Features,
    keywords: Vec<String>,
}

/// A program's requirement lines (`#pragma target`, `#pragma require`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct Requirements {
    lines: Vec<Line>,
    /// Whether a line without keywords names a target (else the program requires 2.5's).
    targeted: bool,
}

/// What a variant compiles with: `SHADER_TARGET` and the features of its `SHADER_AVAILABLE_*`
/// defines (and `SHADER_TARGET_AVAILABLE`).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Variant {
    pub target: u32,
    pub available: Features,
    /// A required feature the device does not have ([`device_lacks`]).
    pub missing: Option<&'static str>,
}

impl Variant {
    /// What compute kernels compile with: 5.0, its features without the stages' (unmeasured).
    pub const COMPUTE: Variant = Variant {
        target: 50,
        available: Features(T50 & !STAGES),
        missing: None,
    };

    /// The defines, `(name, value)`.
    pub fn defines(&self) -> impl Iterator<Item = (String, String)> + '_ {
        [
            ("SHADER_TARGET".to_string(), self.target.to_string()),
            (
                "SHADER_TARGET_AVAILABLE".to_string(),
                shader_target(self.available).to_string(),
            ),
        ]
        .into_iter()
        .chain(self.available.names().map(|n| {
            (
                format!("SHADER_AVAILABLE_{}", n.to_ascii_uppercase()),
                "1".to_string(),
            )
        }))
    }
}

impl Requirements {
    /// Reads `#pragma target <value> [keywords]` (its words after `target`); another value is
    /// left out, with a warning unless it is a platform's ([`PLATFORM_TARGETS`]).
    pub fn target(&mut self, words: &[String]) {
        let Some((value, keywords)) = words.split_first() else {
            return;
        };
        let Some(features) = Features::of_target(value) else {
            if !PLATFORM_TARGETS.contains(&value.as_str()) {
                tracing::warn!("`#pragma target {value}` names no shader target: left out");
            }
            return;
        };
        self.targeted |= keywords.is_empty();
        self.lines.push(Line {
            features,
            keywords: keywords.to_vec(),
        });
    }

    /// Reads `#pragma require <features> [: keywords]` (its words after `require`); a name Unity
    /// does not know is left out.
    pub fn require(&mut self, words: &[String]) {
        let mut features = Features::default();
        let mut keywords = Vec::new();
        let mut after_colon = false;
        for word in words {
            // The colon may stand alone or end or start a word (`mrt8:` `:KW`).
            for (k, part) in word.split(':').enumerate() {
                if k > 0 {
                    after_colon = true;
                }
                if part.is_empty() {
                    continue;
                }
                if after_colon {
                    keywords.push(part.to_string());
                } else if let Some(f) = Features::of_name(part) {
                    features |= f;
                } else {
                    tracing::warn!("`#pragma require {part}` names no GPU feature: left out");
                }
            }
        }
        self.lines.push(Line { features, keywords });
    }

    /// The variant with `keywords` enabled on `device`.
    pub fn variant(&self, keywords: &[String], device: GraphicsDevice) -> Variant {
        let mut base = if self.targeted {
            Features::default()
        } else {
            Features(T25)
        };
        let mut keyed = Features::default();
        for line in &self.lines {
            if line.keywords.is_empty() {
                base |= line.features;
            } else if line.keywords.iter().any(|k| keywords.contains(k)) {
                keyed |= line.features;
            }
        }
        for (keyword, set) in KEYWORD_REQUIREMENTS {
            if keywords.iter().any(|k| k == keyword) {
                keyed |= Features(set);
            }
        }
        let required = Features(base.0 & !STAGES) | keyed;
        Variant {
            target: shader_target(required),
            available: Features((required | device_floor(device)).0 & !STAGES),
            missing: Features(required.0 & device_lacks(device)).names().next(),
        }
    }

    /// `SHADER_TARGET` of the variant without keywords (the one a program's pragmas are read
    /// under).
    pub fn plain_target(&self) -> u32 {
        self.variant(&[], GraphicsDevice::OpenGLCore).target
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn lines(text: &[&str]) -> Requirements {
        let mut r = Requirements::default();
        for line in text {
            let words: Vec<String> = line.split_whitespace().map(String::from).collect();
            match words.split_first() {
                Some((first, rest)) if first == "target" => r.target(rest),
                Some((first, rest)) if first == "require" => r.require(rest),
                _ => panic!("{line}"),
            }
        }
        r
    }

    fn target(r: &Requirements, keywords: &[&str]) -> u32 {
        let keywords: Vec<String> = keywords.iter().map(|k| k.to_string()).collect();
        r.variant(&keywords, GraphicsDevice::OpenGLCore).target
    }

    fn available(r: &Requirements, keywords: &[&str], device: GraphicsDevice) -> Vec<&'static str> {
        let keywords: Vec<String> = keywords.iter().map(|k| k.to_string()).collect();
        r.variant(&keywords, device).available.names().collect()
    }

    /// Lines with keywords apply to the variants with any of them; the highest target wins
    /// whatever the lines' order; without a plain target, 2.5.
    #[test]
    fn keyword_targets_apply_to_their_variants() {
        let r = lines(&["target 2.0", "target 4.5 KW_A", "target 3.5 KW_B"]);
        assert_eq!(target(&r, &[]), 20);
        assert_eq!(target(&r, &["KW_A"]), 45);
        assert_eq!(target(&r, &["KW_B"]), 35);
        assert_eq!(target(&r, &["KW_A", "KW_B"]), 45);
        let first = lines(&["target 4.5 KW_A", "target 3.0"]);
        assert_eq!((target(&first, &[]), target(&first, &["KW_A"])), (30, 45));
        let lower = lines(&["target 4.5", "target 3.0 KW_A"]);
        assert_eq!(target(&lower, &["KW_A"]), 45);
        let only = lines(&["target 4.0 KW_A"]);
        assert_eq!((target(&only, &[]), target(&only, &["KW_B"])), (25, 25));
        let two = lines(&["target 4.6 KW_A KW_B"]);
        assert_eq!((target(&two, &["KW_B"]), target(&two, &[])), (46, 25));
        assert_eq!(target(&lines(&["target 4.5", "target 3.0"]), &[]), 45);
    }

    /// Plain targets leave out the stages' features; keyed ones keep them.
    #[test]
    fn plain_targets_leave_out_the_stages() {
        assert_eq!(target(&lines(&["target 4.0"]), &[]), 35);
        assert_eq!(target(&lines(&["target 4.6"]), &[]), 45);
        assert_eq!(target(&lines(&["target 5.0"]), &[]), 45);
        assert_eq!(target(&lines(&["target 4.0 KW_A"]), &["KW_A"]), 40);
        assert_eq!(target(&lines(&["target 4.6 KW_A"]), &["KW_A"]), 46);
        // Platform values apply nothing: 2.5, as without the lines.
        let aliases = lines(&["target es3.0", "target gl4.1 KW_A", "target es3.1 KW_B"]);
        assert_eq!(target(&aliases, &[]), 25);
        assert_eq!(target(&aliases, &["KW_A", "KW_B"]), 25);
    }

    /// Required features raise the target only when they raise it on their own.
    #[test]
    fn required_features_raise_some_targets() {
        assert_eq!(target(&lines(&["require compute randomwrite"]), &[]), 45);
        assert_eq!(target(&lines(&["target 2.0", "require integers"]), &[]), 20);
        assert_eq!(
            target(&lines(&["target 3.5", "require cubearray"]), &[]),
            35
        );
        let keyed = lines(&["target 2.0", "require msaatex : KW_B"]);
        assert_eq!((target(&keyed, &[]), target(&keyed, &["KW_B"])), (20, 45));
        // The colon attached to a word.
        let attached = lines(&["require msaatex: KW_B"]);
        assert_eq!(target(&attached, &["KW_B"]), 45);
    }

    /// Each feature's raise, keyed (stage features too), and the features a device lacks.
    #[test]
    fn keyed_requirements_raise_by_feature() {
        let r = lines(&[
            "target 2.0",
            "require mrt4 : A",
            "require 2darray : B",
            "require instancing : C",
            "require geometry : D",
            "require tessellation : E",
            "require compute : F",
            "require sparsetex : G",
        ]);
        assert_eq!(target(&r, &["A"]), 30);
        assert_eq!(target(&r, &["B"]), 35);
        assert_eq!(target(&r, &["C"]), 35);
        assert_eq!(target(&r, &["D"]), 40);
        assert_eq!(target(&r, &["E"]), 46);
        assert_eq!(target(&r, &["E", "F"]), 50);
        let keywords = |k: &[&str]| k.iter().map(|k| k.to_string()).collect::<Vec<_>>();
        assert_eq!(
            r.variant(&keywords(&["G"]), GraphicsDevice::Vulkan).missing,
            Some("sparsetex")
        );
        assert_eq!(
            r.variant(&keywords(&["E", "F"]), GraphicsDevice::Vulkan)
                .missing,
            None
        );
    }

    /// The built-in keywords' requirements.
    #[test]
    fn built_in_keywords_bring_requirements() {
        let r = lines(&["target 2.0"]);
        assert_eq!(target(&r, &["INSTANCING_ON"]), 35);
        assert_eq!(target(&r, &["SHADOWS_SCREEN"]), 30);
        assert_eq!(target(&r, &["SHADOWS_SOFT", "DIRLIGHTMAP_COMBINED"]), 30);
        // Not a requirement of Unbound's: Unbound enables no stereo keyword.
        assert_eq!(target(&r, &["STEREO_INSTANCING_ON"]), 20);
        assert_eq!(target(&lines(&[]), &["PROCEDURAL_INSTANCING_ON"]), 45);
        assert_eq!(target(&r, &[crate::MESH_INSTANCE_REPLAY]), 35);
    }

    /// The available features are the requirements' and the device's, without the stages'.
    #[test]
    fn available_features_add_the_device_floor() {
        use GraphicsDevice::{OpenGLCore, Vulkan};
        let plain = lines(&["target 2.0"]);
        let gl = available(&plain, &[], OpenGLCore);
        assert!(
            gl.contains(&"instancing") && gl.contains(&"integers"),
            "{gl:?}"
        );
        assert!(!gl.contains(&"compute"), "{gl:?}");
        let vk = available(&plain, &[], Vulkan);
        assert!(vk.contains(&"compute") && vk.contains(&"randomwrite") && vk.contains(&"msaatex"));
        assert!(!vk.contains(&"uav"), "{vk:?}");
        let uav = lines(&["target 2.0", "require uav"]);
        assert!(available(&uav, &[], OpenGLCore).contains(&"randomwrite"));
        let t46 = lines(&["target 4.6 KW_A"]);
        let gl = available(&t46, &["KW_A"], OpenGLCore);
        assert!(gl.contains(&"msaatex"), "{gl:?}");
        for absent in ["cubearray", "tesshw", "tessellation", "geometry", "compute"] {
            assert!(!gl.contains(&absent), "{absent}: {gl:?}");
        }
        let cube = lines(&["require cubearray : KW_A"]);
        assert!(available(&cube, &["KW_A"], OpenGLCore).contains(&"cubearray"));
        assert!(!available(&cube, &[], Vulkan).contains(&"cubearray"));
        let v = plain.variant(&[], Vulkan);
        let defines: Vec<(String, String)> = v.defines().collect();
        assert!(defines.contains(&("SHADER_TARGET".into(), "20".into())));
        assert!(defines.contains(&("SHADER_TARGET_AVAILABLE".into(), "45".into())));
        assert!(defines.contains(&("SHADER_AVAILABLE_RANDOMWRITE".into(), "1".into())));
    }
}
