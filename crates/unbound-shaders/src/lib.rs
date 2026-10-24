//! Unity shader programs (a ShaderLab pass's `CGPROGRAM`/`HLSLPROGRAM`) compiled to WGSL for
//! Unbound's renderer: every shader draws with its own code.
//!
//! The pipeline: Unity's preprocessing ([`preprocess`]) with the defines of an OpenGL-convention
//! platform (`SHADER_API_GLCORE`: the renderer's matrices and texture rows are OpenGL's), glslang
//! (through shaderc) from HLSL to SPIR-V, naga from SPIR-V to its IR, where the program is fitted
//! to the renderer (see [`fit`]), and WGSL out. Results are cached on disk by a hash of the
//! preprocessed sources and [`FORMAT_VERSION`]. Surface shaders compile as the forward base pass
//! Unity generates for them (see [`surface`]); fixed-function passes as the program their
//! commands stand for (see [`fixed`]).
pub mod compute;
pub mod fixed;
pub mod preprocess;

mod fit;
mod surface;

use anyhow::{Context, Result, anyhow, bail};
use serde::{Deserialize, Serialize};
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
    sync::Arc,
};

/// Bumped whenever the output for a given source changes (cache entries of other versions are
/// never used).
pub const FORMAT_VERSION: u32 = 8;

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
///   4..7 uv0..uv3; 8..11 uv4..uv7 from a second vertex buffer, see `extra_vertex_inputs`);
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
    /// Each texture's dimension.
    pub texture_dims: Vec<TextureDim>,
    pub main_texture: bool,
    /// The keywords this variant was compiled with.
    pub keywords: Vec<String>,
    /// How each texture slot samples (by `textures` index).
    #[serde(default)]
    pub texture_slots: Vec<TextureSlot>,
    /// The cbuffers in the uniform block: where each one lies (a constant buffer bound by its
    /// name, `SetGlobalConstantBuffer`, fills its range).
    #[serde(default)]
    pub blocks: Vec<BlockRange>,
    /// Structured and byte address buffers, bound in group 3 at their index.
    #[serde(default)]
    pub buffers: Vec<BufferSlot>,
    /// Whether the vertex program reads `TEXCOORD4..7` (the second vertex buffer).
    #[serde(default)]
    pub extra_vertex_inputs: bool,
}

/// A buffer a program reads (or, in the fragment program, writes) by name.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct BufferSlot {
    pub name: String,
    pub read_only: bool,
}

/// A cbuffer's byte range in the program's uniform block.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct BlockRange {
    pub name: String,
    pub offset: u32,
    pub size: u32,
}

/// How a texture slot is sampled: a depth texture (`Texture2D` read with comparisons), a
/// comparison sampler (`SamplerComparisonState`), a sampler whose name spells its state (Unity's
/// inline samplers, such as `sampler_LinearClampCompare`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
pub struct TextureSlot {
    pub depth: bool,
    pub comparison: bool,
    pub inline: Option<InlineSampler>,
}

/// A sampler state spelt by a sampler's name: filter 0 point, 1 linear, 2 trilinear; wrap 0
/// repeat, 1 clamp, 2 mirror, 3 mirror once.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct InlineSampler {
    pub filter: i32,
    pub wrap: i32,
}

impl InlineSampler {
    /// The state a sampler's name spells, as Unity reads it: a filter (`Point`, `Linear`,
    /// `Trilinear`) and a wrap mode (`Clamp`, `Repeat`, `Mirror`, `MirrorOnce`), any case.
    pub fn from_name(name: &str) -> Option<InlineSampler> {
        let n = name.to_ascii_lowercase();
        let filter = if n.contains("trilinear") {
            2
        } else if n.contains("linear") {
            1
        } else if n.contains("point") {
            0
        } else {
            return None;
        };
        let wrap = if n.contains("mirroronce") {
            3
        } else if n.contains("mirror") {
            2
        } else if n.contains("repeat") {
            0
        } else if n.contains("clamp") {
            1
        } else {
            return None;
        };
        Some(InlineSampler { filter, wrap })
    }
}

/// A texture binding's dimension.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub enum TextureDim {
    D2,
    D2Array,
    Cube,
    D3,
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
    /// A surface shader's `#pragma surface` words after `surface` (its function, lighting model
    /// and options); `vertex`, `fragment` and the keyword sets are then the generated pass's.
    pub surface: Option<Vec<String>>,
    /// A stage or kind of program Unbound cannot compile (`geometry`, `hull`, ...).
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
                "surface" => p.surface = Some(rest.to_vec()),
                "geometry" | "hull" | "domain" | "kernel" | "raytracing" => {
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
                    // The main directional light, ambient from light probes or a lightmap
                    // (directional or not), and point lights lit per vertex when the renderer
                    // has them.
                    p.keyword_sets.push((words(&["DIRECTIONAL"]), false));
                    p.keyword_sets.push((words(&["LIGHTPROBE_SH", "LIGHTMAP_ON"]), false));
                    p.keyword_sets.push((words(&["DIRLIGHTMAP_COMBINED"]), true));
                    if !rest.iter().any(|w| w == "novertexlight") {
                        p.keyword_sets.push((words(&["VERTEXLIGHT_ON"]), true));
                    }
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

    /// The program's pragmas (keyword sets, entry points), read once. A surface shader's are
    /// those of its generated pass.
    pub fn pragmas(&mut self, program: &Program, linear: bool) -> Result<Pragmas> {
        let key = (program.clone(), linear);
        if let Some(p) = self.pragmas.get(&key) {
            return Ok(p.clone());
        }
        let defines = self.defines(program, &[], 25, linear, "SHADER_STAGE_VERTEX");
        let out = self.preprocess(program, &defines)?;
        let mut pragmas = Pragmas::read(&out.pragmas);
        if let Some(words) = pragmas.surface.take() {
            let pass = surface::Surface::parse(&words)?.program(program, None);
            let defines = self.defines(&pass, &[], 25, linear, "SHADER_STAGE_VERTEX");
            let out = self.preprocess(&pass, &defines)?;
            pragmas = Pragmas::read(&out.pragmas);
            pragmas.surface = Some(words);
        }
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
        let generated;
        let program = match &pragmas.surface {
            Some(words) => {
                // The pass is generated for the variant's code, as Unity analyses each variant.
                let surface = surface::Surface::parse(words)?;
                let probe = surface.program(program, None);
                let defines = self.defines(
                    &probe,
                    &selected,
                    pragmas.target,
                    linear,
                    "SHADER_STAGE_FRAGMENT",
                );
                let text = self.preprocess(&probe, &defines)?.text;
                let analysis = surface
                    .analyse(&text)
                    .with_context(|| format!("surface shader {}", program.file.display()))?;
                generated = surface.program(program, Some(&analysis));
                &generated
            }
            None => program,
        };
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
        let root = program.file.parent();
        let resolve = |dir: Option<&Path>, name: &str| resolve_include(includes, root, dir, name);
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
        let artifact = match self
            .compiler
            .compile_into_spirv(source, kind, &name, entry, Some(&o))
        {
            Ok(artifact) => artifact,
            // Code FXC (Unity's compiler) accepts and glslang does not: compile again with the
            // macros expanded and those constructs rewritten.
            Err(_) => {
                let expanded = self
                    .compiler
                    .preprocess(source, &name, entry, Some(&o))
                    .map_err(|e| anyhow!("{}: {e}", stage_name(kind)))?;
                let fixed = fxc_compatible(&expanded.as_text());
                self.compiler
                    .compile_into_spirv(&fixed, kind, &name, entry, Some(&o))
                    .map_err(|e| {
                        dump_failed(&fixed, &name, kind);
                        anyhow!("{}: {e}", stage_name(kind))
                    })?
            }
        };
        Ok(artifact.as_binary().to_vec())
    }
}

/// With `UNBOUND_SHADER_DUMP` set to a directory, writes a program glslang rejected there (the
/// error lines number it).
fn dump_failed(source: &str, name: &str, kind: shaderc::ShaderKind) {
    let Some(dir) = std::env::var_os("UNBOUND_SHADER_DUMP") else {
        return;
    };
    let dir = PathBuf::from(dir);
    let _ = std::fs::create_dir_all(&dir);
    let path = dir.join(format!("{name}.{}.hlsl", stage_name(kind).replace(' ', "-")));
    if std::fs::write(&path, source).is_ok() {
        tracing::info!("wrote the failed program to {}", path.display());
    }
}

/// Expanded HLSL rewritten where FXC is more lenient than glslang: overloads that differ only in
/// `half`/minimum precision types (one stays), and empty member declarations (`;;` in a struct or
/// cbuffer, left by macros).
fn fxc_compatible(source: &str) -> String {
    drop_empty_declarations(&drop_duplicate_overloads(source))
}

/// Removes a `;` that only repeats the previous one inside braces (not in a `for (;;)`).
fn drop_empty_declarations(source: &str) -> String {
    let mut out = String::with_capacity(source.len());
    let (mut braces, mut parens) = (0i32, 0i32);
    let mut last_significant = ' ';
    for c in source.chars() {
        match c {
            '{' => braces += 1,
            '}' => braces -= 1,
            '(' => parens += 1,
            ')' => parens -= 1,
            _ => {}
        }
        if c == ';' && last_significant == ';' && braces > 0 && parens == 0 {
            continue;
        }
        if !c.is_whitespace() {
            last_significant = c;
        }
        out.push(c);
    }
    out
}

/// Drops top-level function definitions whose signature repeats an earlier one once `half` and
/// minimum precision types are read as the full types glslang gives them (the first definition
/// stays; the render pipelines define the float overload first, and on desktop half is float).
fn drop_duplicate_overloads(source: &str) -> String {
    fn normalize(header: &str) -> Option<String> {
        let header: String = header
            .lines()
            .filter(|l| !l.trim_start().starts_with('#'))
            .collect::<Vec<_>>()
            .join(" ");
        let header = header.trim();
        let close = header.rfind(')')?;
        let open = header.find('(')?;
        if open > close {
            return None;
        }
        let first = header.split_whitespace().next()?;
        if matches!(first, "struct" | "cbuffer" | "tbuffer" | "class" | "interface" | "namespace") {
            return None;
        }
        let mut normalized = String::new();
        for word in header[..=close]
            .replace('(', " ( ")
            .replace(')', " ) ")
            .replace(',', " , ")
            .split_whitespace()
        {
            // `half` is float to glslang too (FXC keeps it apart for overloads).
            let word = match word.strip_prefix("half") {
                Some(rest) if rest.chars().all(|c| c.is_ascii_digit() || c == 'x') => format!("float{rest}"),
                _ => word.to_string(),
            };
            let word = word
                .replace("min16float", "float")
                .replace("min10float", "float")
                .replace("min16uint", "uint")
                .replace("min16int", "int")
                .replace("min12int", "int");
            normalized.push_str(&word);
            normalized.push(' ');
        }
        Some(normalized)
    }

    let bytes = source.as_bytes();
    let mut seen = std::collections::HashSet::new();
    let mut out = String::with_capacity(source.len());
    let mut depth = 0usize;
    // The start of the current top-level declaration (after the last `;` or `}` at depth 0).
    let mut start = 0usize;
    let mut copied = 0usize;
    let mut i = 0usize;
    while i < bytes.len() {
        match bytes[i] {
            b'{' => {
                if depth == 0 {
                    let header = &source[start..i];
                    if let Some(signature) = normalize(header) {
                        if !seen.insert(signature) {
                            // Skip this definition: from its header to its closing brace.
                            let mut d = 0usize;
                            let mut j = i;
                            while j < bytes.len() {
                                match bytes[j] {
                                    b'{' => d += 1,
                                    b'}' => {
                                        d -= 1;
                                        if d == 0 {
                                            break;
                                        }
                                    }
                                    _ => {}
                                }
                                j += 1;
                            }
                            out.push_str(&source[copied..start]);
                            copied = (j + 1).min(bytes.len());
                            start = copied;
                            i = copied;
                            continue;
                        }
                    }
                }
                depth += 1;
            }
            b'}' => {
                depth = depth.saturating_sub(1);
                if depth == 0 {
                    start = i + 1;
                }
            }
            b';' if depth == 0 => start = i + 1,
            b'\n' if depth == 0 && source[start..i].trim_start().starts_with('#') => start = i + 1,
            _ => {}
        }
        i += 1;
    }
    out.push_str(&source[copied..]);
    out
}

fn stage_name(kind: shaderc::ShaderKind) -> &'static str {
    match kind {
        shaderc::ShaderKind::Vertex => "vertex program",
        _ => "fragment program",
    }
}

/// Unity's include lookup: `Assets/...` and `Packages/<name>/...` from the project, then the
/// including file's directory, the `.shader` file's, then the release's CGIncludes.
fn resolve_include(
    includes: &Includes,
    root: Option<&Path>,
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
    if let Some(root) = root {
        candidates.push(root.join(&name));
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

    #[test]
    fn empty_member_declarations_are_dropped() {
        let out = drop_empty_declarations("cbuffer C { float4 a;; float b; };\nvoid f() { for (;;) { break; } }\n");
        assert!(out.contains("float4 a; float b;"), "{out}");
        assert!(out.contains("for (;;)"), "{out}");
    }

    #[test]
    fn duplicate_min_precision_overloads_are_dropped() {
        let source = "#line 1 \"a\"\nmin16float F(min16float x) { return x; } float F(float x) { return x; }\nfloat2 F(float2 x) { return x; }\nstruct S { float a; };\nfloat G(float x) { if (x > 0) { return 1; } return 0; }\nfloat G(min16float x) { return 2; }\n";
        let out = drop_duplicate_overloads(source);
        assert_eq!(out.matches(" F(").count() + out.matches("\nF(").count(), 2, "{out}");
        assert!(out.contains("float2 F(float2 x)"));
        assert!(out.contains("struct S"));
        assert!(out.contains("return 1;"));
        assert!(!out.contains("return 2;"));
    }

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
    fn scalar_arrays_keep_their_stride() {
        let source = r#"
#pragma vertex vert
#pragma fragment frag
float4x4 unity_MatrixVP;
float _Widths[4];
float4 vert(float4 v : POSITION) : SV_POSITION { return mul(unity_MatrixVP, v) * _Widths[1]; }
float4 frag() : SV_Target { return _Widths[2]; }
"#;
        let mut compiler = ShaderCompiler::new(Settings::default()).unwrap();
        let c = compiler
            .compile(&program(source, false), &[], false)
            .unwrap();
        // 16 bytes per element, as the uniform block is filled.
        let widths = c.uniforms.iter().find(|u| u.name == "_Widths").unwrap();
        assert_eq!((widths.count, widths.stride), (4, 16));
    }

    #[test]
    fn short_fragment_outputs_become_vec4() {
        let source = r#"
#pragma vertex vert
#pragma fragment frag
float4x4 unity_MatrixVP;
float _Depth;
float4 vert(float4 v : POSITION) : SV_POSITION { return mul(unity_MatrixVP, v); }
float frag() : SV_Target { return _Depth; }
"#;
        let mut compiler = ShaderCompiler::new(Settings::default()).unwrap();
        let c = compiler.compile(&program(source, false), &[], false).unwrap();
        assert!(c.fragment.contains("vec4<f32>"), "{}", c.fragment);
        let two = r#"
#pragma vertex vert
#pragma fragment frag
float4x4 unity_MatrixVP;
struct Out { float2 a : SV_Target0; float4 b : SV_Target1; };
float4 vert(float4 v : POSITION) : SV_POSITION { return mul(unity_MatrixVP, v); }
Out frag() { Out o; o.a = 1; o.b = 2; return o; }
"#;
        let c = compiler.compile(&program(two, false), &[], false).unwrap();
        assert!(c.fragment.contains("UnboundFragmentOutput"), "{}", c.fragment);
    }

    #[test]
    fn structured_buffers_bind_in_group_3() {
        let source = r#"
#pragma vertex vert
#pragma fragment frag
float4x4 unity_MatrixVP;
StructuredBuffer<float4> _Positions;
ByteAddressBuffer _Colors;
RWStructuredBuffer<uint> _Hits;
struct V { float4 pos : SV_POSITION; float4 color : COLOR; };
V vert(uint id : SV_VertexID) { V o; o.pos = mul(unity_MatrixVP, _Positions[id]); o.color = asfloat(_Colors.Load4(id * 16)); return o; }
float4 frag(V i) : SV_Target { _Hits[0] = 1; return i.color + _Positions[0]; }
"#;
        let mut compiler = ShaderCompiler::new(Settings::default()).unwrap();
        let c = compiler.compile(&program(source, false), &[], false).unwrap();
        let names: Vec<(&str, bool)> = c.buffers.iter().map(|b| (b.name.as_str(), b.read_only)).collect();
        assert_eq!(names, [("_Positions", true), ("_Colors", true), ("_Hits", false)], "{}", c.fragment);
        assert!(c.vertex.contains("@group(3) @binding(0)"), "{}", c.vertex);
        assert!(c.fragment.contains("@group(3) @binding(2)"), "{}", c.fragment);
    }

    #[test]
    fn cbuffers_merge_into_one_block() {
        let source = r#"
#pragma vertex vert
#pragma fragment frag
cbuffer UnityPerDraw { float4x4 unity_ObjectToWorld; float4 unity_LODFade; };
cbuffer UnityPerMaterial { float4 _BaseColor; float _Cutoff; };
float4x4 unity_MatrixVP;
float4 vert(float4 v : POSITION) : SV_POSITION { return mul(unity_MatrixVP, mul(unity_ObjectToWorld, v)) * unity_LODFade.x; }
float4 frag() : SV_Target { return _BaseColor * _Cutoff; }
"#;
        let mut compiler = ShaderCompiler::new(Settings::default()).unwrap();
        let c = compiler
            .compile(&program(source, false), &[], false)
            .unwrap();
        let u = |n: &str| c.uniforms.iter().find(|u| u.name == n).unwrap_or_else(|| panic!("{n}: {:?}", c.uniforms));
        let mut offsets = vec![
            u("unity_ObjectToWorld").offset,
            u("_BaseColor").offset,
            u("unity_MatrixVP").offset,
        ];
        offsets.sort();
        offsets.dedup();
        assert_eq!(offsets.len(), 3, "{:?}", c.uniforms);
        assert!(c.uniform_size >= u("_Cutoff").offset + 4);
    }

    #[test]
    fn unsupported_programs_fail_with_their_reason() {
        let mut compiler = ShaderCompiler::new(Settings::default()).unwrap();
        let geometry = program(
            "#pragma vertex vert\n#pragma geometry geom\n#pragma fragment frag",
            false,
        );
        let error = compiler
            .compile(&geometry, &[], false)
            .unwrap_err()
            .to_string();
        assert!(error.contains("#pragma geometry"), "{error}");
        let broken = program(
            "#pragma vertex vert\n#pragma fragment frag\nfloat4 vert() : SV_POSITION { return nope; }\nfloat4 frag() : SV_Target { return 1; }",
            false,
        );
        let error = format!("{:#}", compiler.compile(&broken, &[], false).unwrap_err());
        assert!(error.contains("nope"), "{error}");
    }

    /// The CGIncludes of the earliest Unity release installed.
    fn unity_includes() -> Option<PathBuf> {
        let hub = PathBuf::from(std::env::var_os("HOME")?).join("Unity/Hub/Editor");
        let mut found: Vec<PathBuf> = std::fs::read_dir(hub)
            .ok()?
            .filter_map(|e| Some(e.ok()?.path().join("Editor/Data/CGIncludes")))
            .filter(|p| p.join("UnityCG.cginc").is_file())
            .collect();
        found.sort();
        found.into_iter().next()
    }

    /// A UI program on the installed Unity release's includes (skipped without one).
    #[test]
    fn a_ui_program_compiles_with_unity_includes() {
        let Some(cg) = unity_includes() else {
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

    /// A `.shader` file's surface program as the ShaderLab parser hands it over: the
    /// `CGINCLUDE` blocks, then the first `CGPROGRAM` with `#pragma surface`.
    fn surface_program(path: &Path) -> Option<Program> {
        let text = std::fs::read_to_string(path).ok()?;
        let blocks = |start: &str| -> Vec<String> {
            text.split(start)
                .skip(1)
                .filter_map(|b| b.split_once("ENDCG").map(|(b, _)| b.to_string()))
                .collect()
        };
        let program = blocks("CGPROGRAM")
            .into_iter()
            .find(|b| b.contains("#pragma surface"))?;
        let mut source = blocks("CGINCLUDE").concat();
        source.push_str(&program);
        Some(Program {
            file: path.to_path_buf(),
            source,
            cg: true,
            light_mode: Some("ForwardBase".into()),
        })
    }

    /// Whether the program reads a uniform (the block declares every global, used or not; the
    /// WGSL writer may append `_` to a name).
    fn uses(c: &Compiled, uniform: &str) -> bool {
        let access = format!(".{uniform}");
        c.uniforms.iter().any(|u| u.name == uniform)
            && (c.vertex.contains(&access) || c.fragment.contains(&access))
    }

    /// Unity's built-in surface shaders (unpacked at /tmp/bis) and the test project's, on the
    /// installed release's includes (each skipped when absent).
    #[test]
    fn surface_shaders_compile_with_unity_includes() {
        let Some(cg) = unity_includes() else {
            eprintln!("no Unity release installed: skipped");
            return;
        };
        let builtin = PathBuf::from("/tmp/bis/src/DefaultResourcesExtra");
        let project = PathBuf::from(std::env::var_os("HOME").unwrap_or_default())
            .join("unity-projects/game/Assets");
        let tmp = project.join("TextMesh Pro/Shaders");
        // The shader, a variant's keywords, and uniforms beyond the lighting's.
        let cases: Vec<(PathBuf, &[&str], &[&str])> = vec![
            (
                builtin.join("Normal-Diffuse.shader"),
                &[],
                &["_Color", "_MainTex_ST"],
            ),
            (
                builtin.join("Normal-Bumped.shader"),
                &[],
                &["_BumpMap_ST", "unity_WorldTransformParams"],
            ),
            (
                builtin.join("Normal-Glossy.shader"),
                &[],
                &["_SpecColor", "_Shininess", "_WorldSpaceCameraPos"],
            ),
            (
                builtin.join("Normal-Parallax.shader"),
                &[],
                &["_Parallax", "_BumpMap_ST"],
            ),
            (
                builtin.join("Normal-DiffuseDetail.shader"),
                &[],
                &["_Detail_ST"],
            ),
            (builtin.join("Alpha-Diffuse.shader"), &[], &["_Color"]),
            (builtin.join("AlphaTest-Diffuse.shader"), &[], &["_Cutoff"]),
            (builtin.join("Illumin-Diffuse.shader"), &[], &["_Illum_ST"]),
            (builtin.join("Decal.shader"), &[], &["_DecalTex_ST"]),
            (builtin.join("Lightmap-Diffuse.shader"), &[], &["_Color"]),
            (
                builtin.join("Mobile/Mobile-Diffuse.shader"),
                &[],
                &["_MainTex_ST"],
            ),
            (
                builtin.join("Mobile/Mobile-BumpSpec.shader"),
                &[],
                &["_Shininess"],
            ),
            (
                builtin.join("Sprites-Diffuse.shader"),
                &["PIXELSNAP_ON"],
                &["_Color", "_RendererColor", "_Flip"],
            ),
            (
                builtin.join("Particle Standard Surface.shader"),
                &[],
                &["_Color", "unity_IndirectSpecColor"],
            ),
            (
                builtin.join("Particle Standard Surface.shader"),
                &["_NORMALMAP", "_EMISSION", "_ALPHABLEND_ON", "_FADING_ON"],
                &["_BumpScale", "_EmissionColor", "_CameraFadeParams"],
            ),
            (
                project.join("Gadsme/Resources/Shaders/GadsmePlacementShader.shader"),
                &[],
                &["_Glossiness", "_Metallic", "_Color"],
            ),
            (
                tmp.join("TMP_SDF-Surface-Mobile.shader"),
                &[],
                &["_FaceColor", "_OutlineTex_ST", "_FaceTex_ST"],
            ),
            (
                tmp.join("TMP_SDF-Surface-Mobile.shader"),
                &["GLOW_ON"],
                &["_GlowColor"],
            ),
        ];
        let mut compiler = ShaderCompiler::new(Settings {
            includes: Includes {
                cg_includes: Some(cg),
                ..Default::default()
            },
            unity_version: 6000071,
            cache_dir: None,
        })
        .unwrap();
        let mut failures = Vec::new();
        for (path, keywords, uniforms) in cases {
            let Some(program) = surface_program(&path) else {
                eprintln!("{}: absent, skipped", path.display());
                continue;
            };
            let name = path.file_name().unwrap().to_string_lossy();
            let pragmas = compiler.pragmas(&program, false).unwrap();
            assert_eq!(pragmas.vertex.as_deref(), Some("vert_surf"), "{name}");
            assert!(pragmas.surface.is_some(), "{name}");
            let keywords: Vec<String> = keywords.iter().map(|k| k.to_string()).collect();
            let c = match compiler.compile(&program, &keywords, true) {
                Ok(c) => c,
                Err(e) => {
                    failures.push(format!("{name} {keywords:?}: {e:#}"));
                    continue;
                }
            };
            for k in ["DIRECTIONAL", "LIGHTPROBE_SH"] {
                assert!(
                    c.keywords.iter().any(|w| w == k),
                    "{name}: {:?}",
                    c.keywords
                );
            }
            // Lit by the main light and the ambient probe.
            let lighting = ["_LightColor0", "_WorldSpaceLightPos0", "unity_SHAr"];
            for u in lighting.iter().chain(uniforms) {
                assert!(uses(&c, u), "{name} {keywords:?}: {u} unused");
            }
            assert!(c.main_texture, "{name}");
            assert!(
                !c.textures.iter().any(|t| t.starts_with("unity_")),
                "{name}: {:?}",
                c.textures
            );
        }
        assert!(failures.is_empty(), "{}", failures.join("\n\n"));

        // Options the built-in shaders leave out: a custom output struct and GI-style lighting,
        // world-space inputs, a vertex modifier without data, a final color, no ambient light.
        let toon = r#"
#pragma surface surf Toon finalcolor:tint vertex:grow noambient novertexlights alpha:premul
#pragma target 3.0
sampler2D _MainTex;
fixed4 _Tint;
float _Grow;
struct SurfaceOutputToon { fixed3 Albedo; fixed3 Normal; fixed3 Emission; fixed Alpha; half Rim; };
struct Input { float2 uv_MainTex; float3 viewDir; float3 worldPos; float4 screenPos; float3 worldNormal; };
void grow (inout appdata_full v) { v.vertex.xyz += v.normal * _Grow; }
void surf (Input IN, inout SurfaceOutputToon o)
{
    o.Albedo = tex2D(_MainTex, IN.uv_MainTex).rgb;
    o.Rim = 1 - saturate(dot(normalize(IN.viewDir), IN.worldNormal));
    o.Alpha = IN.screenPos.w > 0 ? 1 : frac(IN.worldPos.x);
}
half4 LightingToon (SurfaceOutputToon s, half3 viewDir, UnityGI gi)
{
    half d = step(0.5, dot(s.Normal, gi.light.dir));
    return half4(s.Albedo * (gi.light.color * d + gi.indirect.diffuse) + s.Rim, s.Alpha);
}
void LightingToon_GI (SurfaceOutputToon s, UnityGIInput data, inout UnityGI gi)
{
    gi = UnityGlobalIllumination(data, 1.0, s.Normal);
}
void tint (Input IN, SurfaceOutputToon o, inout fixed4 color) { color *= _Tint; }
"#;
        let mut toon = program(toon, true);
        toon.light_mode = Some("ForwardBase".into());
        let pragmas = compiler.pragmas(&toon, false).unwrap();
        assert!(
            pragmas
                .keyword_sets
                .iter()
                .all(|(set, _)| !set.iter().any(|k| k == "VERTEXLIGHT_ON"))
        );
        let c = compiler.compile(&toon, &[], false).unwrap();
        for u in [
            "_Tint",
            "_Grow",
            "_MainTex_ST",
            "_LightColor0",
            "_ProjectionParams",
        ] {
            assert!(uses(&c, u), "{u}");
        }
        assert!(!uses(&c, "unity_SHAr"), "no ambient");

        // Old-style lighting without a view direction; fog and vertex lights on.
        let wrap = r#"
#pragma surface surf Wrap decal:add
sampler2D _MainTex;
struct Input { float2 uv_MainTex; };
half4 LightingWrap (SurfaceOutput s, half3 lightDir, half atten)
{
    half diff = dot(s.Normal, lightDir) * 0.5 + 0.5;
    return half4(s.Albedo * _LightColor0.rgb * diff * atten, s.Alpha);
}
void surf (Input IN, inout SurfaceOutput o) { o.Albedo = tex2D(_MainTex, IN.uv_MainTex).rgb; }
"#;
        let mut wrap = program(wrap, true);
        wrap.light_mode = Some("ForwardBase".into());
        let keywords = ["VERTEXLIGHT_ON".to_string(), "FOG_LINEAR".to_string()];
        let c = compiler.compile(&wrap, &keywords, false).unwrap();
        for u in [
            "unity_4LightPosX0",
            "unity_FogParams",
            "unity_FogColor",
            "unity_SHAr",
        ] {
            assert!(uses(&c, u), "{u}");
        }

        // A VFACE input: the fragment's facing.
        let two_sided = r#"
#pragma surface surf Lambert
sampler2D _MainTex;
struct Input { float2 uv_MainTex; float facing : VFACE; };
void surf (Input IN, inout SurfaceOutput o) { o.Albedo = tex2D(_MainTex, IN.uv_MainTex).rgb * (IN.facing > 0 ? 1 : 0.5); }
"#;
        let mut two_sided = program(two_sided, true);
        two_sided.light_mode = Some("ForwardBase".into());
        let c = compiler.compile(&two_sided, &[], false).unwrap();
        assert!(c.fragment.contains("front_facing"), "{}", c.fragment);

        // Cube maps bind as cube textures.
        for path in [
            builtin.join("Reflect-Diffuse.shader"),
            tmp.join("TMP_SDF-Surface.shader"),
        ] {
            let Some(program) = surface_program(&path) else {
                continue;
            };
            let c = compiler.compile(&program, &[], false).unwrap();
            let cube = c.textures.iter().position(|t| t == "_Cube").unwrap();
            assert_eq!(c.texture_dims[cube], TextureDim::Cube);
        }
    }
}
