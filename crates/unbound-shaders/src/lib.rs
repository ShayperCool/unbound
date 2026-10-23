//! Unity shader programs (a ShaderLab pass's `CGPROGRAM`/`HLSLPROGRAM`) compiled to WGSL for
//! Unbound's renderer, so that shaders without a hand-written port draw with their own code.
//!
//! The pipeline: Unity's preprocessing ([`preprocess`]) with the defines of an OpenGL-convention
//! platform (`SHADER_API_GLCORE`: the renderer's matrices and texture rows are OpenGL's), glslang
//! (through shaderc) from HLSL to SPIR-V, naga from SPIR-V to its IR, where the program is fitted
//! to the renderer (see [`fit`]), and WGSL out. Results are cached on disk by a hash of the
//! preprocessed sources and [`FORMAT_VERSION`].
pub mod preprocess;

mod fit;

use anyhow::{Context, Result, anyhow, bail};
use serde::{Deserialize, Serialize};
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
    sync::Arc,
};

/// Bumped whenever the output for a given source changes (cache entries of other versions are
/// never used).
pub const FORMAT_VERSION: u32 = 1;

/// One pass's program.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct Program {
    /// The `.shader` file: relative includes resolve from its directory.
    pub file: PathBuf,
    /// The pass's `CGINCLUDE`/`HLSLINCLUDE` blocks, then its program.
    pub source: String,
    /// `CGPROGRAM` (implicitly includes `HLSLSupport.cginc` and `UnityShaderVariables.cginc`),
    /// or `HLSLPROGRAM`.
    pub cg: bool,
    /// The pass's `LightMode` tag.
    pub light_mode: Option<String>,
}

/// Where `#include`s are found.
#[derive(Debug, Clone, Default)]
pub struct Includes {
    /// The project root (`Assets/...` includes).
    pub project: PathBuf,
    /// Packages by name (`Packages/<name>/...` includes).
    pub packages: Vec<(String, PathBuf)>,
    /// The Unity release's `Editor/Data/CGIncludes` (`UnityCG.cginc`, ...).
    pub cg_includes: Option<PathBuf>,
}

#[derive(Debug, Clone, Default)]
pub struct Settings {
    pub includes: Includes,
    /// `UNITY_VERSION` (e.g. 6000071 for 6000.0.71).
    pub unity_version: u32,
    /// Where compiled programs are cached (none: not cached).
    pub cache_dir: Option<PathBuf>,
}

/// The scalar type of a uniform member.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum Scalar {
    Float,
    Int,
    Uint,
}

/// A member of the program's uniform block (`$Globals`): built-in variables and material
/// properties, filled by name.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Uniform {
    pub name: String,
    pub offset: u32,
    pub scalar: Scalar,
    /// Vector components (1 for scalars).
    pub rows: u32,
    /// Matrix columns (1 for vectors and scalars).
    pub columns: u32,
    /// Array elements (1 when not an array) and their stride.
    pub count: u32,
    pub stride: u32,
}

/// A compiled program, fitted to the renderer's interface:
/// - vertex inputs at the renderer's vertex locations (0 position, 1 normal, 2 tangent, 3 color,
///   4..7 uv0..uv3);
/// - group 0: binding 0 the uniform block (dynamic offset), binding 1 the renderer's per-draw
///   uniform (its color multiplies the vertex color, as a CanvasRenderer's color does);
/// - group 1: binding 2k / 2k+1 the texture and sampler of `textures[k]`;
/// - group 2: binding 0 / 1 `_MainTex` and its sampler, when the program samples it;
/// - clip space as OpenGL's, turned into the renderer's (bottom-up targets, depth 0..w).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Compiled {
    pub vertex: String,
    pub fragment: String,
    pub vertex_entry: String,
    pub fragment_entry: String,
    pub uniforms: Vec<Uniform>,
    /// The uniform block's size (0 without one).
    pub uniform_size: u32,
    pub textures: Vec<String>,
    pub main_texture: bool,
    /// The keywords this variant was compiled with.
    pub keywords: Vec<String>,
}

/// A program's `#pragma`s that matter here.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct Pragmas {
    pub vertex: Option<String>,
    pub fragment: Option<String>,
    /// `multi_compile` / `shader_feature` sets: (keywords, whether "none" is an option).
    pub keyword_sets: Vec<(Vec<String>, bool)>,
    /// `#pragma target`, as `SHADER_TARGET` (25 by default).
    pub target: u32,
    /// A stage or kind of program Unbound cannot compile (`surface`, `geometry`, ...).
    pub unsupported: Option<String>,
}

impl Pragmas {
    fn read(pragmas: &[preprocess::Pragma]) -> Pragmas {
        let mut p = Pragmas {
            target: 25,
            ..Default::default()
        };
        for pragma in pragmas {
            let Some((first, rest)) = pragma.words.split_first() else {
                continue;
            };
            match first.as_str() {
                "vertex" => p.vertex = rest.first().cloned(),
                "fragment" => p.fragment = rest.first().cloned(),
                "target" => {
                    if let Some(t) = rest.first().and_then(|t| t.parse::<f32>().ok()) {
                        p.target = (t * 10.0).round() as u32;
                    }
                }
                "surface" | "geometry" | "hull" | "domain" | "kernel" | "raytracing" => {
                    p.unsupported = Some(format!("#pragma {first}"));
                }
                "multi_compile_fog" => p
                    .keyword_sets
                    .push((words(&["FOG_LINEAR", "FOG_EXP", "FOG_EXP2"]), true)),
                "multi_compile_instancing" => {
                    p.keyword_sets.push((words(&["INSTANCING_ON"]), true))
                }
                "multi_compile_particles" => {
                    p.keyword_sets.push((words(&["SOFTPARTICLES_ON"]), true))
                }
                "multi_compile_fwdbase" | "multi_compile_fwdbasealpha" => {
                    p.keyword_sets.push((words(&["DIRECTIONAL"]), false));
                }
                other => {
                    let shader_feature = other.starts_with("shader_feature");
                    if !(other.starts_with("multi_compile") || shader_feature)
                        || other.contains("multi_compile_fwd")
                    {
                        continue;
                    }
                    let none = shader_feature || rest.iter().any(|k| k.chars().all(|c| c == '_'));
                    let keywords: Vec<String> = rest
                        .iter()
                        .filter(|k| !k.chars().all(|c| c == '_'))
                        .cloned()
                        .collect();
                    if !keywords.is_empty() {
                        // A single keyword in a shader_feature line is optional too.
                        p.keyword_sets.push((keywords, none));
                    }
                }
            }
        }
        p
    }

    /// The keywords a material with `enabled` keywords draws with: per set, the first enabled
    /// one, else none when that is an option, else the set's first.
    pub fn select(&self, enabled: &[String]) -> Vec<String> {
        let mut selected: Vec<String> = Vec::new();
        for (set, none) in &self.keyword_sets {
            if let Some(k) = set.iter().find(|k| enabled.contains(k)) {
                selected.push(k.clone());
            } else if !none && let Some(first) = set.first() {
                selected.push(first.clone());
            }
        }
        selected.sort();
        selected.dedup();
        selected
    }
}

fn words(w: &[&str]) -> Vec<String> {
    w.iter().map(|s| s.to_string()).collect()
}

/// DX9-style samplers as texture-and-sampler pairs (glslang has no `sampler2D` for HLSL), the
/// way Unity's own DXC path emulates them; Unity's emulation is skipped (it names the struct
/// `sampler2D` on OpenGL, a glslang keyword).
const SAMPLERS: &str = r#"
#define DXC_SAMPLER_COMPATIBILITY 1
struct sampler1D_f { Texture1D<float4> t; SamplerState s; };
struct sampler2D_f { Texture2D<float4> t; SamplerState s; };
struct sampler3D_f { Texture3D<float4> t; SamplerState s; };
struct samplerCUBE_f { TextureCube<float4> t; SamplerState s; };
#define sampler1D sampler1D_f
#define sampler2D sampler2D_f
#define sampler3D sampler3D_f
#define samplerCUBE samplerCUBE_f
#define sampler2D_half sampler2D_f
#define sampler2D_float sampler2D_f
#define samplerCUBE_half samplerCUBE_f
#define samplerCUBE_float samplerCUBE_f
float4 tex1D(sampler1D_f x, float v) { return x.t.Sample(x.s, v); }
float4 tex2D(sampler2D_f x, float2 v) { return x.t.Sample(x.s, v); }
float4 tex2D(sampler2D_f x, float2 v, float2 dx, float2 dy) { return x.t.SampleGrad(x.s, v, dx, dy); }
float4 tex2Dlod(sampler2D_f x, float4 v) { return x.t.SampleLevel(x.s, v.xy, v.w); }
float4 tex2Dbias(sampler2D_f x, float4 v) { return x.t.SampleBias(x.s, v.xy, v.w); }
float4 tex2Dgrad(sampler2D_f x, float2 v, float2 dx, float2 dy) { return x.t.SampleGrad(x.s, v, dx, dy); }
float4 tex2Dproj(sampler2D_f x, float3 v) { return x.t.Sample(x.s, v.xy / v.z); }
float4 tex2Dproj(sampler2D_f x, float4 v) { return x.t.Sample(x.s, v.xy / v.w); }
float4 tex3D(sampler3D_f x, float3 v) { return x.t.Sample(x.s, v); }
float4 tex3Dlod(sampler3D_f x, float4 v) { return x.t.SampleLevel(x.s, v.xyz, v.w); }
float4 texCUBE(samplerCUBE_f x, float3 v) { return x.t.Sample(x.s, v); }
float4 texCUBElod(samplerCUBE_f x, float4 v) { return x.t.SampleLevel(x.s, v.xyz, v.w); }
float4 texCUBEbias(samplerCUBE_f x, float4 v) { return x.t.SampleBias(x.s, v.xyz, v.w); }
"#;

pub struct ShaderCompiler {
    settings: Settings,
    compiler: shaderc::Compiler,
    /// Compiled variants by program and keywords.
    memory: HashMap<(Program, Vec<String>, bool), Arc<Compiled>>,
    pragmas: HashMap<(Program, bool), Pragmas>,
}

impl ShaderCompiler {
    pub fn new(settings: Settings) -> Result<ShaderCompiler> {
        Ok(ShaderCompiler {
            settings,
            compiler: shaderc::Compiler::new().context("starting glslang (shaderc)")?,
            memory: HashMap::new(),
            pragmas: HashMap::new(),
        })
    }

    /// The program's pragmas (keyword sets, entry points), read once.
    pub fn pragmas(&mut self, program: &Program, linear: bool) -> Result<Pragmas> {
        let key = (program.clone(), linear);
        if let Some(p) = self.pragmas.get(&key) {
            return Ok(p.clone());
        }
        let defines = self.defines(program, &[], 25, linear, "SHADER_STAGE_VERTEX");
        let out = self.preprocess(program, &defines)?;
        let pragmas = Pragmas::read(&out.pragmas);
        self.pragmas.insert(key, pragmas.clone());
        Ok(pragmas)
    }

    /// The variant of `program` a material with `keywords` enabled draws with.
    pub fn compile(
        &mut self,
        program: &Program,
        keywords: &[String],
        linear: bool,
    ) -> Result<Arc<Compiled>> {
        let pragmas = self.pragmas(program, linear)?;
        if let Some(what) = &pragmas.unsupported {
            bail!("{what} programs are not supported (Unbound compiles vertex/fragment programs)");
        }
        let vertex_entry = pragmas
            .vertex
            .clone()
            .ok_or_else(|| anyhow!("no #pragma vertex"))?;
        let fragment_entry = pragmas
            .fragment
            .clone()
            .ok_or_else(|| anyhow!("no #pragma fragment"))?;
        let selected = pragmas.select(keywords);
        let key = (program.clone(), selected.clone(), linear);
        if let Some(c) = self.memory.get(&key) {
            return Ok(c.clone());
        }
        let vertex_defines = self.defines(
            program,
            &selected,
            pragmas.target,
            linear,
            "SHADER_STAGE_VERTEX",
        );
        let fragment_defines = self.defines(
            program,
            &selected,
            pragmas.target,
            linear,
            "SHADER_STAGE_FRAGMENT",
        );
        let vertex_source = self.preprocess(program, &vertex_defines)?.text;
        let fragment_source = self.preprocess(program, &fragment_defines)?.text;

        let hash = {
            let mut h = blake3::Hasher::new();
            h.update(&FORMAT_VERSION.to_le_bytes());
            for part in [
                &vertex_source,
                &fragment_source,
                &vertex_entry,
                &fragment_entry,
            ] {
                h.update(&(part.len() as u64).to_le_bytes());
                h.update(part.as_bytes());
            }
            h.update(selected.join(" ").as_bytes());
            h.finalize().to_hex().to_string()
        };
        let cached = self
            .settings
            .cache_dir
            .as_ref()
            .map(|d| d.join(format!("{hash}.json")));
        if let Some(path) = &cached
            && let Ok(text) = std::fs::read_to_string(path)
            && let Ok(compiled) = serde_json::from_str::<Compiled>(&text)
        {
            let compiled = Arc::new(compiled);
            self.memory.insert(key, compiled.clone());
            return Ok(compiled);
        }

        let vertex_spv = self.glslang(
            &vertex_source,
            shaderc::ShaderKind::Vertex,
            &vertex_entry,
            &program.file,
        )?;
        let fragment_spv = self.glslang(
            &fragment_source,
            shaderc::ShaderKind::Fragment,
            &fragment_entry,
            &program.file,
        )?;
        let mut compiled = fit::fit(&vertex_spv, &fragment_spv)?;
        compiled.vertex_entry = vertex_entry;
        compiled.fragment_entry = fragment_entry;
        compiled.keywords = selected;
        if let Some(path) = &cached {
            if let Some(dir) = path.parent() {
                let _ = std::fs::create_dir_all(dir);
            }
            let temp = path.with_extension(format!("{}.tmp", std::process::id()));
            if std::fs::write(&temp, serde_json::to_vec(&compiled)?).is_ok() {
                let _ = std::fs::rename(&temp, path);
            }
        }
        let compiled = Arc::new(compiled);
        self.memory.insert(key, compiled.clone());
        Ok(compiled)
    }

    fn defines(
        &self,
        program: &Program,
        keywords: &[String],
        target: u32,
        linear: bool,
        stage: &str,
    ) -> Vec<(String, String)> {
        let mut d: Vec<(String, String)> = [
            ("SHADER_API_GLCORE", "1"),
            ("SHADER_API_DESKTOP", "1"),
            ("UNITY_COMPILER_HLSL", "1"),
            ("UNITY_COMPILER_HLSLCC", "1"),
            ("UNITY_ENABLE_REFLECTION_BUFFERS", "1"),
            ("UNITY_PBS_USE_BRDF1", "1"),
            ("UNITY_SPECCUBE_BOX_PROJECTION", "1"),
            ("UNITY_SPECCUBE_BLENDING", "1"),
            ("UNITY_ENABLE_DETAIL_NORMALMAP", "1"),
            ("UNITY_HARDWARE_TIER3", "1"),
            ("UNITY_LIGHTMAP_FULL_HDR", "1"),
        ]
        .iter()
        .map(|(k, v)| (k.to_string(), v.to_string()))
        .collect();
        d.push(("SHADER_TARGET".into(), target.to_string()));
        d.push((
            "UNITY_VERSION".into(),
            self.settings.unity_version.to_string(),
        ));
        d.push((stage.into(), "1".into()));
        if !linear {
            d.push(("UNITY_COLORSPACE_GAMMA".into(), "1".into()));
        }
        if let Some(mode) = &program.light_mode {
            let pass = match mode.to_ascii_lowercase().as_str() {
                "forwardbase" => Some("UNITY_PASS_FORWARDBASE"),
                "forwardadd" => Some("UNITY_PASS_FORWARDADD"),
                "shadowcaster" => Some("UNITY_PASS_SHADOWCASTER"),
                _ => None,
            };
            if let Some(pass) = pass {
                d.push((pass.into(), "1".into()));
            }
        }
        for k in keywords {
            d.push((k.clone(), "1".into()));
        }
        d
    }

    fn preprocess(
        &self,
        program: &Program,
        defines: &[(String, String)],
    ) -> Result<preprocess::Output> {
        let includes = &self.settings.includes;
        let resolve = |dir: Option<&Path>, name: &str| resolve_include(includes, dir, name);
        let mut pp = preprocess::Preprocessor::new(&resolve);
        for (k, v) in defines {
            pp.define(k, v);
        }
        let mut source = String::from(SAMPLERS);
        if program.cg {
            source.push_str(
                "#include \"HLSLSupport.cginc\"\n#include \"UnityShaderVariables.cginc\"\n",
            );
        }
        // The program's own lines start after a marker, so errors can name them.
        source.push_str(&program.source);
        pp.run(&source, Some(&program.file))
            .with_context(|| format!("preprocessing {}", program.file.display()))
    }

    fn glslang(
        &self,
        source: &str,
        kind: shaderc::ShaderKind,
        entry: &str,
        file: &Path,
    ) -> Result<Vec<u32>> {
        let mut o = shaderc::CompileOptions::new().context("glslang options")?;
        o.set_source_language(shaderc::SourceLanguage::HLSL);
        o.set_auto_bind_uniforms(true);
        o.set_auto_map_locations(true);
        o.set_hlsl_functionality1(true);
        o.set_hlsl_io_mapping(true);
        // Names survive (textures and uniforms are matched by name); drivers optimize anyway.
        o.set_optimization_level(shaderc::OptimizationLevel::Zero);
        o.set_target_env(
            shaderc::TargetEnv::Vulkan,
            shaderc::EnvVersion::Vulkan1_1 as u32,
        );
        o.set_suppress_warnings();
        let name = file
            .file_name()
            .map_or("shader".into(), |n| n.to_string_lossy().to_string());
        let artifact = self
            .compiler
            .compile_into_spirv(source, kind, &name, entry, Some(&o))
            .map_err(|e| anyhow!("{}: {e}", stage_name(kind)))?;
        Ok(artifact.as_binary().to_vec())
    }
}

fn stage_name(kind: shaderc::ShaderKind) -> &'static str {
    match kind {
        shaderc::ShaderKind::Vertex => "vertex program",
        _ => "fragment program",
    }
}

/// Unity's include lookup: `Assets/...` and `Packages/<name>/...` from the project, then the
/// including file's directory, then the release's CGIncludes.
fn resolve_include(
    includes: &Includes,
    dir: Option<&Path>,
    name: &str,
) -> Option<(PathBuf, String)> {
    let name = name.replace('\\', "/");
    let mut candidates: Vec<PathBuf> = Vec::new();
    if name.starts_with("Assets/") {
        candidates.push(includes.project.join(&name));
    }
    if let Some(rest) = name.strip_prefix("Packages/") {
        let (package, path) = rest.split_once('/').unwrap_or((rest, ""));
        if let Some((_, root)) = includes.packages.iter().find(|(n, _)| n == package) {
            candidates.push(root.join(path));
        }
    }
    if let Some(dir) = dir {
        candidates.push(dir.join(&name));
    }
    if let Some(cg) = &includes.cg_includes {
        candidates.push(cg.join(&name));
    }
    candidates
        .into_iter()
        .find_map(|p| std::fs::read_to_string(&p).ok().map(|t| (p, t)))
}

#[cfg(test)]
mod tests {
    use super::*;

    const PROGRAM: &str = r#"
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile __ FANCY
#pragma shader_feature _EMISSION
float4x4 unity_ObjectToWorld;
float4x4 unity_MatrixVP;
float4 _Color;
float _Cutoff;
sampler2D _MainTex;
sampler2D _Mask;
struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
v2f vert(appdata v)
{
    v2f o;
    o.pos = mul(unity_MatrixVP, mul(unity_ObjectToWorld, v.vertex));
    o.color = v.color * _Color;
    o.uv = v.uv;
    return o;
}
float4 frag(v2f i) : SV_Target
{
    float4 c = tex2D(_MainTex, i.uv) * tex2D(_Mask, i.uv).a * i.color;
#ifdef FANCY
    c.rgb *= 2;
#endif
    clip(c.a - _Cutoff);
    return c;
}
"#;

    fn program(source: &str, cg: bool) -> Program {
        Program {
            file: PathBuf::from("Test.shader"),
            source: source.to_string(),
            cg,
            light_mode: None,
        }
    }

    #[test]
    fn a_program_is_fitted_to_the_renderer() {
        let mut compiler = ShaderCompiler::new(Settings {
            unity_version: 6000071,
            ..Default::default()
        })
        .unwrap();
        let p = program(PROGRAM, false);
        let pragmas = compiler.pragmas(&p, false).unwrap();
        assert_eq!(pragmas.vertex.as_deref(), Some("vert"));
        // multi_compile with "__" and shader_feature are optional; a material picks from them.
        assert!(pragmas.select(&[]).is_empty());
        assert_eq!(
            pragmas.select(&["FANCY".into(), "OTHER".into()]),
            vec!["FANCY".to_string()]
        );

        let c = compiler.compile(&p, &["FANCY".into()], false).unwrap();
        assert_eq!(c.keywords, vec!["FANCY".to_string()]);
        assert_eq!(c.textures, vec!["_Mask".to_string()]);
        assert!(c.main_texture);
        let u = |n: &str| {
            c.uniforms
                .iter()
                .find(|u| u.name == n)
                .unwrap_or_else(|| panic!("{n}: {:?}", c.uniforms))
        };
        assert_eq!(
            (u("unity_MatrixVP").rows, u("unity_MatrixVP").columns),
            (4, 4)
        );
        assert_eq!(u("_Color").rows, 4);
        assert_eq!((u("_Cutoff").rows, u("_Cutoff").scalar), (1, Scalar::Float));
        assert!(c.uniform_size >= u("_Cutoff").offset + 4);
        // Inputs at the renderer's locations: position 0, color 3, uv0 4.
        for location in ["@location(0)", "@location(3)", "@location(4)"] {
            assert!(c.vertex.contains(location), "{location}:\n{}", c.vertex);
        }
        // The per-draw color and the depth remap.
        assert!(c.vertex.contains("unbound_draw"), "{}", c.vertex);
        assert!(c.vertex.contains("0.5f"), "{}", c.vertex);
        assert!(
            c.fragment.contains("@group(2) @binding(0)"),
            "{}",
            c.fragment
        );
        assert!(
            c.fragment.contains("@group(1) @binding(1)"),
            "{}",
            c.fragment
        );
        assert!(
            c.fragment.contains("@group(0) @binding(0)"),
            "{}",
            c.fragment
        );
        // A variant is compiled once.
        let again = compiler.compile(&p, &["FANCY".into()], false).unwrap();
        assert!(Arc::ptr_eq(&c, &again));
    }

    #[test]
    fn the_dx9_position_semantic_is_the_position() {
        let source = r#"
#pragma vertex vert
#pragma fragment frag
float4x4 unity_MatrixVP;
struct v2f { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
v2f vert(float4 v : POSITION, float2 uv : TEXCOORD0) { v2f o; o.vertex = mul(unity_MatrixVP, v); o.uv = uv; return o; }
float4 frag(v2f i) : COLOR { return float4(i.uv, 0, 1); }
"#;
        let mut compiler = ShaderCompiler::new(Settings::default()).unwrap();
        let c = compiler
            .compile(&program(source, false), &[], false)
            .unwrap();
        assert!(c.vertex.contains("@builtin(position)"), "{}", c.vertex);
        // The position is no interpolated value: the uv keeps its location in both stages.
        assert!(c.vertex.contains("@location(1)"), "{}", c.vertex);
        assert!(
            c.fragment.contains("fn frag(@location(1)"),
            "{}",
            c.fragment
        );
    }

    #[test]
    fn unsupported_programs_fail_with_their_reason() {
        let mut compiler = ShaderCompiler::new(Settings::default()).unwrap();
        let surface = program("#pragma surface surf Lambert\nvoid surf() {}", false);
        let error = compiler
            .compile(&surface, &[], false)
            .unwrap_err()
            .to_string();
        assert!(error.contains("#pragma surface"), "{error}");
        let broken = program(
            "#pragma vertex vert\n#pragma fragment frag\nfloat4 vert() : SV_POSITION { return nope; }\nfloat4 frag() : SV_Target { return 1; }",
            false,
        );
        let error = format!("{:#}", compiler.compile(&broken, &[], false).unwrap_err());
        assert!(error.contains("nope"), "{error}");
    }

    /// A UI program on the installed Unity release's includes (skipped without one).
    #[test]
    fn a_ui_program_compiles_with_unity_includes() {
        let Some(cg) = std::env::var_os("HOME")
            .map(|h| PathBuf::from(h).join("Unity/Hub/Editor"))
            .and_then(|hub| std::fs::read_dir(hub).ok())
            .into_iter()
            .flatten()
            .filter_map(|e| Some(e.ok()?.path().join("Editor/Data/CGIncludes")))
            .find(|p| p.join("UnityCG.cginc").is_file())
        else {
            eprintln!("no Unity release installed: skipped");
            return;
        };
        let source = r#"
#pragma vertex vert
#pragma fragment frag
#pragma target 2.0
#pragma multi_compile_local _ UNITY_UI_CLIP_RECT
#include "UnityCG.cginc"
#include "UnityUI.cginc"
struct appdata_t { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 texcoord : TEXCOORD0; float4 worldPosition : TEXCOORD1; };
sampler2D _MainTex;
fixed4 _Color;
fixed4 _TextureSampleAdd;
float4 _ClipRect;
float4 _MainTex_ST;
v2f vert(appdata_t v)
{
    v2f o;
    o.worldPosition = v.vertex;
    o.vertex = UnityObjectToClipPos(o.worldPosition);
    o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
    o.color = v.color * _Color;
    return o;
}
fixed4 frag(v2f IN) : SV_Target
{
    half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;
    #ifdef UNITY_UI_CLIP_RECT
    color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
    #endif
    return color;
}
"#;
        let mut compiler = ShaderCompiler::new(Settings {
            includes: Includes {
                cg_includes: Some(cg),
                ..Default::default()
            },
            unity_version: 6000071,
            cache_dir: None,
        })
        .unwrap();
        let c = compiler
            .compile(
                &program(source, true),
                &["UNITY_UI_CLIP_RECT".into()],
                false,
            )
            .unwrap();
        for name in [
            "unity_ObjectToWorld",
            "unity_MatrixVP",
            "_Color",
            "_ClipRect",
            "_MainTex_ST",
            "_Time",
        ] {
            assert!(c.uniforms.iter().any(|u| u.name == name), "{name}");
        }
        assert!(c.main_texture && c.textures.is_empty());
    }
}
