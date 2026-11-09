//! The keywords a shader or compute shader declares (`Shader.keywordSpace`), in Unity's order,
//! read from its programs' `#pragma` lines after preprocessing: declarations in inactive `#if`
//! branches and in plain `#include`s are not the program's, `#include_with_pragmas` ones are.
//!
//! These are the rules of one release. Which release has them is the compatibility profile's
//! choice (`unbound_compat::ShaderKeywordRules`); this module does not look at versions, and a
//! caller must not apply it for a release the profile gives no rules. Measured on 6000.0.71f1
//! (tests/projects/shaderkeywords `Reference/unity-6000.0.71f1.jsonl`, probe shaders on a Linux
//! Editor with OpenGLCore) and checked offline against the keyword lists Unity serialized for its
//! built-in shaders of that release:
//!
//! A shader's space starts with the four stereo keywords. Each pass of each of its own SubShaders
//! then adds its new keywords stage by stage (vertex first, then fragment,
//! hull, domain, geometry, ray tracing; declarations for all stages count as the vertex stage's,
//! after its own). Within a stage: global declarations (`multi_compile`, `shader_feature`,
//! `dynamic_branch`) in source order, then the fog, instancing and particle sets, then local
//! declarations, then the light pass sets (`multi_compile_fwdbase`, `_shadowcaster`, ...).
//! `skip_variants` keywords are not the program's. A fixed-function pass declares the fog set; a
//! surface shader the keywords of the passes Unity generates for it. A compute shader lists its
//! local declarations before its global ones, without stereo keywords.
//!
//! A declaration form these rules do not know makes the whole space unknown (an error), never a
//! partial list. The provider also checks the Fallback chain; an extension/flag merge it cannot
//! establish remains unknown. Surface options, explicit fixed-function Fog Mode, and lightpass
//! ordering are unsupported metadata cases, not guessed complete spaces.
use crate::preprocess::Pragma;
use anyhow::{Result, bail};

/// A declared keyword: local (`_local` declarations) or global (overridable by global keyword
/// state), and whether it is a `dynamic_branch` keyword.
#[derive(Debug, Clone, PartialEq, Eq, serde::Serialize, serde::Deserialize)]
pub struct Declared {
    pub name: String,
    pub local: bool,
    pub dynamic: bool,
}

/// The keywords every shader's space starts with (the engine's stereo rendering modes).
pub const STEREO: [&str; 4] = [
    "STEREO_INSTANCING_ON",
    "UNITY_SINGLE_PASS_STEREO",
    "STEREO_MULTIVIEW_ON",
    "STEREO_CUBEMAP_RENDER_ON",
];

/// Where a declaration comes in a stage's list.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord)]
enum Group {
    Global,
    /// The fog, instancing and particle sets.
    Builtin,
    Local,
    /// A light pass set (`multi_compile_fwdbase`, ...).
    LightPass,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Stage {
    All,
    Vertex,
    Fragment,
    Hull,
    Domain,
    Geometry,
    RayTracing,
}

const STAGES: [Stage; 6] = [
    Stage::Vertex,
    Stage::Fragment,
    Stage::Hull,
    Stage::Domain,
    Stage::Geometry,
    Stage::RayTracing,
];

#[derive(Debug, Clone)]
struct Declaration {
    keywords: Vec<String>,
    group: Group,
    stage: Stage,
    dynamic: bool,
}

fn set(words: &[&str], group: Group) -> Declaration {
    Declaration {
        keywords: words.iter().map(|w| w.to_string()).collect(),
        group,
        stage: Stage::All,
        dynamic: false,
    }
}

const FOG: [&str; 3] = ["FOG_LINEAR", "FOG_EXP", "FOG_EXP2"];
const FWDBASE: [&str; 9] = [
    "DIRECTIONAL",
    "LIGHTPROBE_SH",
    "SHADOWS_SHADOWMASK",
    "DYNAMICLIGHTMAP_ON",
    "LIGHTMAP_ON",
    "LIGHTMAP_SHADOW_MIXING",
    "DIRLIGHTMAP_COMBINED",
    "SHADOWS_SCREEN",
    "VERTEXLIGHT_ON",
];
const FWDADD: [&str; 5] = [
    "POINT",
    "DIRECTIONAL",
    "SPOT",
    "POINT_COOKIE",
    "DIRECTIONAL_COOKIE",
];
const FULLSHADOWS: [&str; 4] = [
    "SHADOWS_DEPTH",
    "SHADOWS_SCREEN",
    "SHADOWS_SOFT",
    "SHADOWS_CUBE",
];
const SHADOWCASTER: [&str; 2] = ["SHADOWS_DEPTH", "SHADOWS_CUBE"];
const PREPASSFINAL: [&str; 7] = [
    "LIGHTPROBE_SH",
    "SHADOWS_SHADOWMASK",
    "DYNAMICLIGHTMAP_ON",
    "LIGHTMAP_ON",
    "LIGHTMAP_SHADOW_MIXING",
    "DIRLIGHTMAP_COMBINED",
    "UNITY_HDR_ON",
];

const SHADOWCOLLECTOR: [&str; 2] = ["SHADOWS_SPLIT_SPHERES", "SHADOWS_SINGLE_CASCADE"];

pub(crate) fn skipped_keywords(pragmas: &[Pragma]) -> impl Iterator<Item = &String> {
    pragmas
        .iter()
        .filter(|p| p.words.first().is_some_and(|w| w == "skip_variants"))
        .flat_map(|p| p.words.iter().skip(1))
}

/// A program's keyword declarations, in source order, without the keywords its `skip_variants`
/// lines name.
fn declarations(pragmas: &[Pragma]) -> Result<Vec<Declaration>> {
    let skipped: Vec<&String> = skipped_keywords(pragmas).collect();
    let mut out = declared(pragmas, false)?;
    for d in &mut out {
        d.keywords.retain(|k| !skipped.contains(&k));
    }
    out.retain(|d| !d.keywords.is_empty());
    Ok(out)
}

fn declared(pragmas: &[Pragma], placeholders: bool) -> Result<Vec<Declaration>> {
    let mut out = Vec::new();
    let procedural = pragmas.iter().any(|p| {
        p.words.first().is_some_and(|w| w == "instancing_options")
            && p.words.iter().skip(1).any(|w| w.starts_with("procedural:"))
    });
    for pragma in pragmas {
        let Some((first, rest)) = pragma.words.split_first() else {
            continue;
        };
        let light = |words: &[&str]| set(words, Group::LightPass);
        match first.as_str() {
            "multi_compile_fog" => {
                let mut d = set(&FOG, Group::Builtin);
                if placeholders {
                    d.keywords.insert(0, "__".into());
                }
                out.push(d);
            }
            "multi_compile_instancing" => {
                let mut d = set(&["INSTANCING_ON"], Group::Builtin);
                if procedural {
                    d.keywords.push("PROCEDURAL_INSTANCING_ON".into());
                }
                out.push(d);
            }
            "multi_compile_particles" => out.push(set(&["SOFTPARTICLES_ON"], Group::Builtin)),
            "multi_compile_fwdbase" | "multi_compile_fwdbasealpha" => {
                if let Some(option) = rest.first() {
                    bail!("multi_compile_fwdbase option `{option}` is not measured");
                }
                out.push(light(&FWDBASE));
            }
            "multi_compile_fwdadd" => out.push(light(&FWDADD)),
            "multi_compile_fwdadd_fullshadows" => {
                let mut d = light(&FWDADD);
                d.keywords.extend(FULLSHADOWS.iter().map(|k| k.to_string()));
                out.push(d);
            }
            "multi_compile_lightpass" => {
                bail!("multi_compile_lightpass keyword order is not measured")
            }
            "multi_compile_shadowcaster" => out.push(light(&SHADOWCASTER)),
            "multi_compile_shadowcollector" => out.push(light(&SHADOWCOLLECTOR)),
            "multi_compile_prepassfinal" => out.push(light(&PREPASSFINAL)),
            other => {
                let Some((dynamic, suffix)) = other
                    .strip_prefix("multi_compile")
                    .map(|s| (false, s))
                    .or_else(|| other.strip_prefix("shader_feature").map(|s| (false, s)))
                    .or_else(|| other.strip_prefix("dynamic_branch").map(|s| (true, s)))
                else {
                    continue;
                };
                let (local, suffix) = match suffix.strip_prefix("_local") {
                    Some(s) => (true, s),
                    None => (false, suffix),
                };
                let stage = match suffix {
                    "" => Stage::All,
                    "_vertex" => Stage::Vertex,
                    "_fragment" => Stage::Fragment,
                    "_hull" => Stage::Hull,
                    "_domain" => Stage::Domain,
                    "_geometry" => Stage::Geometry,
                    "_raytracing" => Stage::RayTracing,
                    // A built-in set or form these rules do not know: the space is unknown.
                    _ => bail!("`#pragma {other}` is a keyword declaration Unbound cannot list"),
                };
                let keywords: Vec<String> = rest
                    .iter()
                    .filter(|k| placeholders || !k.chars().all(|c| c == '_'))
                    .cloned()
                    .collect();
                if keywords.is_empty() {
                    continue;
                }
                out.push(Declaration {
                    keywords,
                    group: if local { Group::Local } else { Group::Global },
                    stage,
                    dynamic,
                });
            }
        }
    }
    Ok(out)
}

/// A pass's keyword choices for variant validation. Explicit declarations retain their
/// placeholders, which the variant constructor treats as choices even though keywordSpace
/// omits them. Light-pass shorthand lists describe keyword membership, not exclusive choices:
/// e.g. DIRECTIONAL and LIGHTMAP_ON can coexist in a forward-base variant.
pub fn variant_sets(pragmas: &[Pragma]) -> Result<Vec<Vec<String>>> {
    let skipped: Vec<&String> = skipped_keywords(pragmas).collect();
    let mut sets = Vec::new();
    for mut d in declared(pragmas, true)? {
        d.keywords.retain(|k| !skipped.contains(&k));
        match d.group {
            Group::Global | Group::Local | Group::Builtin => sets.push(d.keywords),
            Group::LightPass => {
                sets.extend(d.keywords.into_iter().map(|k| vec![k]));
            }
        }
    }
    Ok(sets)
}

/// A keyword space being built: keywords in order, each once (its first declaration's kind).
#[derive(Debug, Clone, Default)]
pub struct Space {
    pub keywords: Vec<Declared>,
}

impl Space {
    /// A shader's space before its passes: the stereo keywords (global, not dynamic).
    pub fn shader() -> Space {
        let mut space = Space::default();
        for k in STEREO {
            space.add(k, false, false);
        }
        space
    }

    fn add(&mut self, name: &str, local: bool, dynamic: bool) {
        if !self.keywords.iter().any(|k| k.name == name) {
            self.keywords.push(Declared {
                name: name.to_string(),
                local,
                dynamic,
            });
        }
    }

    fn add_declarations(&mut self, declarations: &[Declaration]) {
        for (i, stage) in STAGES.iter().enumerate() {
            let mut list: Vec<&Declaration> =
                declarations.iter().filter(|d| d.stage == *stage).collect();
            if i == 0 {
                list.extend(declarations.iter().filter(|d| d.stage == Stage::All));
            }
            // Stable: source order within each group.
            list.sort_by_key(|d| d.group);
            for d in list {
                for k in &d.keywords {
                    self.add(k, d.group == Group::Local, d.dynamic);
                }
            }
        }
    }

    /// A pass's program, by its `#pragma` lines.
    pub fn add_program(&mut self, pragmas: &[Pragma]) -> Result<()> {
        let declarations = declarations(pragmas)?;
        self.add_declarations(&declarations);
        Ok(())
    }

    /// An unlit fixed-function pass without an explicit Fog Mode. The provider rejects
    /// unmeasured lit/Fog variants before adding their keywords.
    pub fn add_fixed_function(&mut self) {
        self.add_declarations(&[set(&FOG, Group::Builtin)]);
    }

    /// The passes Unity generates for a surface shader ([`surface_passes`]): each has the
    /// program's own declarations and those of its kind of pass.
    pub fn add_surface(&mut self, pragmas: &[Pragma], options: &[String]) -> Result<()> {
        if let Some(option) = options.first() {
            bail!("surface shader option `{option}` is not measured");
        }
        let own = declarations(pragmas)?;
        let has = |w: &str| options.iter().any(|o| o == w);
        let builtins = |instancing: bool, fog: bool| {
            let mut d = Vec::new();
            if instancing && !has("noinstancing") {
                d.push(set(&["INSTANCING_ON"], Group::Builtin));
            }
            if fog && !has("nofog") {
                d.push(set(&FOG, Group::Builtin));
            }
            d
        };
        for pass in surface_passes(options) {
            let mut list = own.clone();
            match pass.light_mode {
                "ForwardBase" => {
                    list.extend(builtins(true, true));
                    let mut d = set(&FWDBASE, Group::LightPass);
                    if has("novertexlights") {
                        d.keywords.retain(|k| k != "VERTEXLIGHT_ON");
                    }
                    list.push(d);
                }
                "ForwardAdd" => {
                    list.extend(builtins(false, true));
                    let mut d = set(&FWDADD, Group::LightPass);
                    if has("fullforwardshadows") {
                        d.keywords.extend(FULLSHADOWS.iter().map(|k| k.to_string()));
                    }
                    list.push(d);
                }
                "Deferred" => {
                    list.extend(builtins(true, false));
                    list.push(set(&PREPASSFINAL, Group::LightPass));
                }
                "Meta" => list.push(set(&["EDITOR_VISUALIZATION"], Group::Global)),
                "ShadowCaster" => {
                    list.extend(builtins(true, false));
                    list.push(set(&SHADOWCASTER, Group::LightPass));
                }
                _ => {}
            }
            self.add_declarations(&list);
        }
        Ok(())
    }
}

/// A pass Unity generates for a surface shader.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SurfacePass {
    pub name: &'static str,
    pub light_mode: &'static str,
}

/// The passes Unity generates for a surface shader with these `#pragma surface` options (the
/// words after the surface function and lighting model): forward base, forward add (not with
/// `noforwardadd`), deferred (not for blended or decal surfaces, nor with
/// `exclude_path:deferred`), meta (not with `nometa`), and a shadow caster with `addshadow`.
pub fn surface_passes(options: &[String]) -> Vec<SurfacePass> {
    let has = |w: &str| options.iter().any(|o| o == w);
    let blended = options.iter().any(|o| {
        o == "alpha" || o.starts_with("alpha:") || o.starts_with("decal:") || o == "keepalpha"
    });
    let mut passes = Vec::new();
    if !has("exclude_path:forward") {
        passes.push(SurfacePass {
            name: "FORWARD",
            light_mode: "ForwardBase",
        });
        if !has("noforwardadd") {
            passes.push(SurfacePass {
                name: "FORWARD",
                light_mode: "ForwardAdd",
            });
        }
    }
    if !blended && !has("exclude_path:deferred") {
        passes.push(SurfacePass {
            name: "DEFERRED",
            light_mode: "Deferred",
        });
    }
    if !has("nometa") {
        passes.push(SurfacePass {
            name: "META",
            light_mode: "Meta",
        });
    }
    if has("addshadow") {
        passes.push(SurfacePass {
            name: "ShadowCaster",
            light_mode: "ShadowCaster",
        });
    }
    passes
}

/// A compute shader's space: its local declarations, then its global ones.
pub fn compute_space(pragmas: &[Pragma]) -> Result<Vec<Declared>> {
    let declared = declarations(pragmas)?;
    if let Some(set) = declared.iter().find(|d| d.group == Group::LightPass) {
        bail!(
            "a light pass keyword set ({}) in a compute shader is a declaration Unbound cannot list",
            set.keywords.join(" ")
        );
    }
    let mut space = Space::default();
    for local in [true, false] {
        for d in declared
            .iter()
            .filter(|d| (d.group == Group::Local) == local)
        {
            for k in &d.keywords {
                space.add(k, local, d.dynamic);
            }
        }
    }
    Ok(space.keywords)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn pragmas(text: &str) -> Vec<Pragma> {
        text.lines()
            .filter_map(|l| l.trim().strip_prefix("#pragma "))
            .map(|l| Pragma {
                words: l.split_whitespace().map(String::from).collect(),
            })
            .collect()
    }

    fn names(space: &[Declared]) -> Vec<&str> {
        space.iter().map(|k| k.name.as_str()).collect()
    }

    /// The first SubShader's two passes and the second SubShader's pass of the probe's
    /// `Probe/KwForms` (after preprocessing on OpenGLCore: the `SHADER_API_D3D11` declaration
    /// and the plain `#include`'s are gone, the `#include_with_pragmas` file's are in), against
    /// the canonical `probe.Probe/KwForms.space.keywordNames` and its local/dynamic flags.
    #[test]
    fn variant_choices_keep_placeholders_and_separate_independent_sets() {
        let sets = variant_sets(&pragmas("#pragma multi_compile _ A B\n#pragma shader_feature_local __ C D\n#pragma dynamic_branch E F\n#pragma multi_compile_fog\n#pragma multi_compile_fwdbase\n#pragma skip_variants B")).unwrap();
        assert_eq!(sets[0], ["_", "A"]);
        assert_eq!(sets[1], ["__", "C", "D"]);
        assert_eq!(sets[2], ["E", "F"]);
        assert_eq!(sets[3], ["__", "FOG_LINEAR", "FOG_EXP", "FOG_EXP2"]);
        assert!(sets.iter().any(|s| s == &["DIRECTIONAL"]));
        assert!(sets.iter().any(|s| s == &["LIGHTMAP_ON"]));
        assert!(variant_sets(&pragmas("#pragma multi_compile_new_unmeasured A B")).is_err());
    }

    #[test]
    fn pass_keywords_are_grouped_by_stage_and_kind() {
        let mut space = Space::shader();
        space
            .add_program(&pragmas(
                "#pragma vertex vert
                 #pragma fragment frag
                 #pragma multi_compile _ GLOBAL_A GLOBAL_B
                 #pragma multi_compile_local _ LOCAL_A
                 #pragma multi_compile_local __ LOCAL_B LOCAL_C
                 #pragma shader_feature FEATURE_A
                 #pragma shader_feature_local _ FEATURE_LOCAL_A FEATURE_LOCAL_B
                 #pragma shader_feature_local_fragment FRAG_LOCAL
                 #pragma multi_compile_vertex _ VTX_GLOBAL
                 #pragma dynamic_branch DYN_GLOBAL
                 #pragma dynamic_branch_local DYN_LOCAL_A DYN_LOCAL_B
                 #pragma dynamic_branch_local_fragment DYN_FRAG
                 #pragma multi_compile _ SHARED_KW
                 #pragma multi_compile _ LIGHTMAP_ON
                 #pragma multi_compile_fwdbase
                 #pragma multi_compile_fog
                 #pragma shader_feature TOGGLE_KW
                 #pragma skip_variants GLOBAL_B
                 #pragma multi_compile_local _ INCLUDED_LOCAL
                 #pragma multi_compile _ INCLUDED_GLOBAL",
            ))
            .unwrap();
        space
            .add_program(&pragmas(
                "#pragma multi_compile_local _ SHARED_KW
                 #pragma multi_compile_local _ PASS2_ONLY
                 #pragma multi_compile_shadowcaster
                 #pragma multi_compile_instancing",
            ))
            .unwrap();
        space
            .add_program(&pragmas(
                "#pragma multi_compile _ SUB2_GLOBAL
                 #pragma multi_compile_local _ LOCAL_A",
            ))
            .unwrap();
        // The second SubShader's untagged fixed-function pass adds nothing new.
        space.add_fixed_function();
        assert_eq!(
            names(&space.keywords),
            [
                "STEREO_INSTANCING_ON",
                "UNITY_SINGLE_PASS_STEREO",
                "STEREO_MULTIVIEW_ON",
                "STEREO_CUBEMAP_RENDER_ON",
                "VTX_GLOBAL",
                "GLOBAL_A",
                "FEATURE_A",
                "DYN_GLOBAL",
                "SHARED_KW",
                "LIGHTMAP_ON",
                "TOGGLE_KW",
                "INCLUDED_GLOBAL",
                "FOG_LINEAR",
                "FOG_EXP",
                "FOG_EXP2",
                "LOCAL_A",
                "LOCAL_B",
                "LOCAL_C",
                "FEATURE_LOCAL_A",
                "FEATURE_LOCAL_B",
                "DYN_LOCAL_A",
                "DYN_LOCAL_B",
                "INCLUDED_LOCAL",
                "DIRECTIONAL",
                "LIGHTPROBE_SH",
                "SHADOWS_SHADOWMASK",
                "DYNAMICLIGHTMAP_ON",
                "LIGHTMAP_SHADOW_MIXING",
                "DIRLIGHTMAP_COMBINED",
                "SHADOWS_SCREEN",
                "VERTEXLIGHT_ON",
                "FRAG_LOCAL",
                "DYN_FRAG",
                "INSTANCING_ON",
                "PASS2_ONLY",
                "SHADOWS_DEPTH",
                "SHADOWS_CUBE",
                "SUB2_GLOBAL",
            ]
        );
        let get = |n: &str| space.keywords.iter().find(|k| k.name == n).unwrap().clone();
        // The canonical's isOverridable is !local, isDynamic is dynamic.
        assert!(!get("SHARED_KW").local, "the first declaration decides");
        assert!(!get("STEREO_INSTANCING_ON").local && !get("STEREO_INSTANCING_ON").dynamic);
        assert!(get("DYN_GLOBAL").dynamic && !get("DYN_GLOBAL").local);
        assert!(get("DYN_FRAG").dynamic && get("DYN_FRAG").local);
        assert!(get("LOCAL_A").local && !get("LOCAL_A").dynamic);
        assert!(!get("FOG_LINEAR").local && !get("DIRECTIONAL").local);
        assert!(!space.keywords.iter().any(|k| k.name == "GLOBAL_B"));
    }

    /// `Probe/KwPlain` (a CG pass without declarations and a fixed-function pass) and
    /// `Probe/KwFixed` (fixed-function only): stereo, then the fog set.
    #[test]
    fn fixed_function_passes_declare_the_fog_set() {
        let mut plain = Space::shader();
        plain
            .add_program(&pragmas("#pragma vertex vert\n#pragma fragment frag"))
            .unwrap();
        plain.add_fixed_function();
        let mut fixed = Space::shader();
        for _ in 0..5 {
            fixed.add_fixed_function();
        }
        let want = [
            "STEREO_INSTANCING_ON",
            "UNITY_SINGLE_PASS_STEREO",
            "STEREO_MULTIVIEW_ON",
            "STEREO_CUBEMAP_RENDER_ON",
            "FOG_LINEAR",
            "FOG_EXP",
            "FOG_EXP2",
        ];
        assert_eq!(names(&plain.keywords), want);
        assert_eq!(names(&fixed.keywords), want);
    }

    /// The canonical `compute.space.keywordNames` of the probe's `KwCompute.compute`.
    #[test]
    fn compute_spaces_list_locals_first() {
        let space = compute_space(&pragmas(
            "#pragma kernel CSMain
             #pragma kernel CSOther
             #pragma multi_compile _ CS_GLOBAL_A CS_GLOBAL_B
             #pragma multi_compile_local _ CS_LOCAL
             #pragma dynamic_branch CS_DYN
             #pragma multi_compile _ LIGHTMAP_ON",
        ))
        .unwrap();
        assert_eq!(
            names(&space),
            [
                "CS_LOCAL",
                "CS_GLOBAL_A",
                "CS_GLOBAL_B",
                "CS_DYN",
                "LIGHTMAP_ON"
            ]
        );
        assert!(space[0].local && !space[3].local && space[3].dynamic);
    }

    /// A compute file that declares nothing has a known, empty space (not an unknown one).
    #[test]
    fn a_compute_file_without_declarations_has_an_empty_space() {
        let space = compute_space(&pragmas("#pragma kernel CSMain")).unwrap();
        assert!(space.is_empty());
    }

    /// The canonical `probe.Probe/KwSurface.space.keywordNames`: four generated passes.
    #[test]
    fn surface_shaders_declare_their_generated_passes_keywords() {
        let mut space = Space::shader();
        let options: Vec<String> = Vec::new();
        assert_eq!(surface_passes(&options).len(), 4);
        space
            .add_surface(
                &pragmas(
                    "#pragma surface surf Lambert
                     #pragma shader_feature_local _ SURF_LOCAL
                     #pragma multi_compile _ SURF_GLOBAL",
                ),
                &options,
            )
            .unwrap();
        assert_eq!(
            names(&space.keywords)[4..],
            [
                "SURF_GLOBAL",
                "INSTANCING_ON",
                "FOG_LINEAR",
                "FOG_EXP",
                "FOG_EXP2",
                "SURF_LOCAL",
                "DIRECTIONAL",
                "LIGHTPROBE_SH",
                "SHADOWS_SHADOWMASK",
                "DYNAMICLIGHTMAP_ON",
                "LIGHTMAP_ON",
                "LIGHTMAP_SHADOW_MIXING",
                "DIRLIGHTMAP_COMBINED",
                "SHADOWS_SCREEN",
                "VERTEXLIGHT_ON",
                "POINT",
                "SPOT",
                "POINT_COOKIE",
                "DIRECTIONAL_COOKIE",
                "UNITY_HDR_ON",
                "EDITOR_VISUALIZATION",
            ]
        );
    }

    /// A declaration form the rules do not know is an error (the caller reports the space as
    /// unknown), never a silently shorter list.
    #[test]
    fn an_unknown_declaration_form_makes_the_space_unknown() {
        let mut space = Space::shader();
        let err = space
            .add_program(&pragmas(
                "#pragma multi_compile _ KNOWN_A
                 #pragma multi_compile_somethingnew _ NEW_A",
            ))
            .unwrap_err();
        assert!(format!("{err}").contains("multi_compile_somethingnew"));
        assert!(compute_space(&pragmas("#pragma shader_feature_tessellation X")).is_err());
        assert!(compute_space(&pragmas("#pragma multi_compile_fwdbase")).is_err());
        // Pragmas that declare nothing are not declarations.
        let mut other = Space::shader();
        other
            .add_program(&pragmas(
                "#pragma target 3.0\n#pragma multi_compile __\n#pragma instancing_options assumeuniformscaling",
            ))
            .unwrap();
        assert_eq!(other.keywords.len(), STEREO.len());
    }

    #[test]
    fn unmeasured_surface_options_and_lightpass_order_are_rejected() {
        let mut space = Space::shader();
        assert!(
            space
                .add_program(&pragmas("#pragma multi_compile_lightpass"))
                .is_err()
        );
        for option in ["nolightmap", "noshadow", "nofog", "alpha:fade"] {
            assert!(space.add_surface(&[], &[option.to_string()]).is_err());
        }
        assert_eq!(space.keywords.len(), STEREO.len());
    }

    #[test]
    fn unmeasured_fwdbase_options_never_publish_a_partial_set() {
        for option in [
            "nolightmap",
            "nodynlightmap",
            "noshadow",
            "novertexlight",
            "other",
        ] {
            let mut space = Space::shader();
            assert!(
                space
                    .add_program(&pragmas(&format!("#pragma multi_compile_fwdbase {option}")))
                    .is_err()
            );
            assert_eq!(space.keywords.len(), STEREO.len());
        }
        let mut plain = Space::shader();
        assert!(
            plain
                .add_program(&pragmas("#pragma multi_compile_fwdbase"))
                .is_ok()
        );
        assert!(plain.keywords.iter().any(|k| k.name == "LIGHTMAP_ON"));
    }
}
