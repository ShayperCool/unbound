//! Compute shaders (`.compute` files): kernels compiled from Unity's sources to WGSL compute
//! programs. The same pipeline as draw programs (Unity preprocessing, glslang, naga); resources
//! keep their names and are bound in group 0 one after another, so the renderer binds a
//! dispatch's buffers, textures and uniforms by name, as `ComputeShader.SetBuffer` names them.
use crate::targets::Variant;
use crate::{
    FORMAT_VERSION, Program, ShaderCompiler, TextureDim, Uniform,
    fit::{
        Stage, align_uniform_members, pad_array_elements, parse, scan, strip_relaxed_precision,
        texture_dim, uniform, unify_int_signedness,
    },
};
use anyhow::{Context, Result, anyhow, bail};
use naga::{AddressSpace, Expression, ImageClass, Module, ResourceBinding, TypeInner};
use serde::{Deserialize, Serialize};
use std::{collections::HashSet, path::PathBuf, sync::Arc};

/// A `.compute` file.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct ComputeProgram {
    pub file: PathBuf,
    pub source: String,
}

impl ComputeProgram {
    fn as_program(&self) -> Program {
        Program {
            file: self.file.clone(),
            source: self.source.clone(),
            cg: false,
            light_mode: None,
        }
    }
}

/// A kernel a `#pragma kernel` declares, with the defines it adds.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Kernel {
    pub name: String,
    pub defines: Vec<(String, String)>,
}

/// What a compute file declares: its kernels in order and its keyword sets.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct ComputePragmas {
    pub kernels: Vec<Kernel>,
    pub keyword_sets: Vec<(Vec<String>, bool)>,
}

/// A uniform block (a `cbuffer`, or `$Globals` for loose uniforms), filled by member name.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct UniformBlock {
    pub name: String,
    pub binding: u32,
    pub size: u32,
    pub uniforms: Vec<Uniform>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub enum ResourceKind {
    /// A structured or byte-address buffer.
    Buffer { read_only: bool },
    /// A sampled texture: `sample` is `float`, `sint`, `uint` or `depth`.
    Texture { dim: TextureDim, sample: String },
    /// A RW texture: its format (naga's name, e.g. `Rgba8Unorm`) and whether it is only written.
    StorageTexture {
        dim: TextureDim,
        format: String,
        write_only: bool,
    },
    /// A sampler: of the texture `sampler_<name>` pairs with, or an inline state its name spells
    /// (`linear`/`point`/`trilinear`, `clamp`/`repeat`/`mirror`/`mirroronce`). `unfiltered`: it
    /// samples (gathers) an integer texture, which no filtering sampler may.
    Sampler {
        texture: Option<String>,
        comparison: bool,
        unfiltered: bool,
    },
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct KernelResource {
    pub name: String,
    pub binding: u32,
    pub kind: ResourceKind,
}

/// A kernel compiled for the renderer: group 0 holds its uniform blocks and resources at the
/// bindings listed.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CompiledKernel {
    pub wgsl: String,
    pub entry: String,
    pub workgroup_size: [u32; 3],
    pub blocks: Vec<UniformBlock>,
    pub resources: Vec<KernelResource>,
    pub keywords: Vec<String>,
}

impl ComputePragmas {
    fn read(pragmas: &[crate::preprocess::Pragma]) -> ComputePragmas {
        let mut out = ComputePragmas::default();
        for pragma in pragmas {
            let Some((first, rest)) = pragma.words.split_first() else {
                continue;
            };
            match first.as_str() {
                "kernel" => {
                    let Some((name, defines)) = rest.split_first() else {
                        continue;
                    };
                    let defines = defines
                        .iter()
                        .map(|d| match d.split_once('=') {
                            Some((k, v)) => (k.to_string(), v.to_string()),
                            None => (d.to_string(), "1".to_string()),
                        })
                        .collect();
                    out.kernels.push(Kernel {
                        name: name.clone(),
                        defines,
                    });
                }
                other
                    if other.starts_with("multi_compile")
                        || other.starts_with("shader_feature") =>
                {
                    let none = other.starts_with("shader_feature")
                        || rest.iter().any(|k| k.chars().all(|c| c == '_'));
                    let keywords: Vec<String> = rest
                        .iter()
                        .filter(|k| !k.chars().all(|c| c == '_'))
                        .cloned()
                        .collect();
                    if !keywords.is_empty() {
                        out.keyword_sets.push((keywords, none));
                    }
                }
                _ => {}
            }
        }
        out
    }

    /// The keywords a dispatch with `enabled` keywords compiles with (as [`crate::Pragmas::select`]).
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

impl ShaderCompiler {
    /// The kernels and keyword sets of a compute file.
    pub fn compute_pragmas(&mut self, program: &ComputeProgram) -> Result<ComputePragmas> {
        let p = program.as_program();
        let defines = self.defines(&p, &[], &Variant::COMPUTE, true, "SHADER_STAGE_COMPUTE");
        let out = self.preprocess(&p, &defines)?;
        Ok(ComputePragmas::read(&out.pragmas))
    }

    /// A compute file's declared keywords (`ComputeShader.keywordSpace`) under the given rules
    /// ([`crate::keywords`]): an error when the file cannot be preprocessed or declares a form the
    /// rules do not know (the space is then unknown, not empty). Nothing is compiled or cached.
    pub fn compute_keywords(
        &self,
        program: &ComputeProgram,
    ) -> Result<Vec<crate::keywords::Declared>> {
        let p = program.as_program();
        let defines = self.defines(&p, &[], &Variant::COMPUTE, true, "SHADER_STAGE_COMPUTE");
        let out = self.preprocess(&p, &defines)?;
        crate::keywords::compute_space(&out.pragmas)
    }

    /// A kernel's variant for the enabled keywords, its RW textures in the given formats (naga
    /// names: `Rgba8Unorm`, `Rgba16Float`, ...; others take the format glslang inferred).
    pub fn compile_kernel(
        &mut self,
        program: &ComputeProgram,
        kernel: &str,
        keywords: &[String],
        storage_formats: &[(String, String)],
    ) -> Result<Arc<CompiledKernel>> {
        let pragmas = self.compute_pragmas(program)?;
        let declared = pragmas
            .kernels
            .iter()
            .find(|k| k.name == kernel)
            .ok_or_else(|| anyhow!("no kernel `{kernel}` (#pragma kernel)"))?;
        let selected = pragmas.select(keywords);
        let p = program.as_program();
        let mut defines = self.defines(
            &p,
            &selected,
            &Variant::COMPUTE,
            true,
            "SHADER_STAGE_COMPUTE",
        );
        defines.extend(declared.defines.iter().cloned());
        let source = self.preprocess(&p, &defines)?.text;
        let opengl = self.settings.opengl_shading_language;
        let hash = kernel_cache_key(&source, kernel, &selected, storage_formats, opengl);
        let cached = self
            .settings
            .cache_dir
            .as_ref()
            .map(|d| d.join(format!("k-{hash}.json")));
        if let Some(path) = &cached
            && let Ok(text) = std::fs::read_to_string(path)
            && let Ok(compiled) = serde_json::from_str::<CompiledKernel>(&text)
        {
            return Ok(Arc::new(compiled));
        }
        let spv = self.glslang(&source, shaderc::ShaderKind::Compute, kernel, &program.file)?;
        let mut compiled = fit_kernel(&spv, storage_formats, opengl)
            .with_context(|| format!("kernel `{kernel}` of {}", program.file.display()))?;
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
        Ok(Arc::new(compiled))
    }
}

fn storage_format(name: &str) -> Option<naga::StorageFormat> {
    use naga::StorageFormat as F;
    Some(match name {
        "R8Unorm" => F::R8Unorm,
        "Rg8Unorm" => F::Rg8Unorm,
        "R16Unorm" => F::R16Unorm,
        "R16Float" => F::R16Float,
        "R32Float" => F::R32Float,
        "R32Uint" => F::R32Uint,
        "R32Sint" => F::R32Sint,
        "Rg16Unorm" => F::Rg16Unorm,
        "Rg16Float" => F::Rg16Float,
        "Rg32Float" => F::Rg32Float,
        "Rgba8Unorm" => F::Rgba8Unorm,
        "R8Snorm" => F::R8Snorm,
        "Rg8Snorm" => F::Rg8Snorm,
        "Rgba8Snorm" => F::Rgba8Snorm,
        "R16Snorm" => F::R16Snorm,
        "Rg16Snorm" => F::Rg16Snorm,
        "Rgba16Snorm" => F::Rgba16Snorm,
        "Rgb10a2Uint" => F::Rgb10a2Uint,
        "Rgba16Unorm" => F::Rgba16Unorm,
        "Rgba16Float" => F::Rgba16Float,
        "Rgba32Float" => F::Rgba32Float,
        "Rgba32Uint" => F::Rgba32Uint,
        "Rgba32Sint" => F::Rgba32Sint,
        "Rg11b10Ufloat" => F::Rg11b10Ufloat,
        "Rgb10a2Unorm" => F::Rgb10a2Unorm,
        "R8Uint" => F::R8Uint,
        "R8Sint" => F::R8Sint,
        "R16Uint" => F::R16Uint,
        "R16Sint" => F::R16Sint,
        "Rg8Uint" => F::Rg8Uint,
        "Rg8Sint" => F::Rg8Sint,
        "Rg16Uint" => F::Rg16Uint,
        "Rg16Sint" => F::Rg16Sint,
        "Rg32Uint" => F::Rg32Uint,
        "Rg32Sint" => F::Rg32Sint,
        "Rgba8Uint" => F::Rgba8Uint,
        "Rgba8Sint" => F::Rgba8Sint,
        "Rgba16Uint" => F::Rgba16Uint,
        "Rgba16Sint" => F::Rgba16Sint,
        _ => return None,
    })
}

/// The globals a module reads as images (`ImageLoad`) anywhere.
fn loaded_images(module: &Module) -> HashSet<naga::Handle<naga::GlobalVariable>> {
    let mut loaded = HashSet::new();
    let functions = module
        .functions
        .iter()
        .map(|(_, f)| f)
        .chain(module.entry_points.iter().map(|e| &e.function));
    for f in functions {
        for (_, e) in f.expressions.iter() {
            if let Expression::ImageLoad { image, .. } = e
                && let Expression::GlobalVariable(g) = f.expressions[*image]
            {
                loaded.insert(g);
            }
        }
    }
    loaded
}

/// The sampler globals a module samples or gathers an integer texture with.
fn integer_samplers(module: &Module) -> HashSet<naga::Handle<naga::GlobalVariable>> {
    let mut found = HashSet::new();
    let functions = module
        .functions
        .iter()
        .map(|(_, f)| f)
        .chain(module.entry_points.iter().map(|e| &e.function));
    for f in functions {
        for (_, e) in f.expressions.iter() {
            if let Expression::ImageSample { image, sampler, .. } = e
                && let Expression::GlobalVariable(i) = f.expressions[*image]
                && let Expression::GlobalVariable(s) = f.expressions[*sampler]
                && let TypeInner::Image {
                    class:
                        ImageClass::Sampled {
                            kind: naga::ScalarKind::Sint | naga::ScalarKind::Uint,
                            ..
                        },
                    ..
                } = module.types[module.global_variables[i].ty].inner
            {
                found.insert(s);
            }
        }
    }
    found
}

/// Binds a kernel's resources in group 0 by order, reads its uniform blocks, sets its RW
/// textures' formats and access, and writes WGSL.
/// `opengl_shading_language`: the kernel runs as OpenGL shading language
/// (`Settings::opengl_shading_language`), which cannot read a compared texture with `Load`.
fn fit_kernel(
    spv: &[u32],
    storage_formats: &[(String, String)],
    opengl_shading_language: bool,
) -> Result<CompiledKernel> {
    // A texture sampled through several samplers: a variable for each (the renderer binds a
    // resource by its name, the texture's).
    let primary = crate::samplers::primary_samplers(&crate::samplers::sampled_pairs(spv)?);
    let words = scan(
        &unify_int_signedness(&pad_array_elements(&align_uniform_members(
            &strip_relaxed_precision(&crate::samplers::one_sampler_per_texture(spv, &primary)?),
        )?)?)?,
        Stage::Compute,
    )?
    .words;
    // Compute programs have no clip space.
    let mut module = parse(&words, crate::fit::ClipSpace::Renderer).context("compute program")?;
    if opengl_shading_language {
        crate::fit::loads_no_compared_texture(&module)?;
    }
    let entry = module
        .entry_points
        .iter()
        .find(|e| e.stage == naga::ShaderStage::Compute)
        .ok_or_else(|| anyhow!("no compute entry point"))?;
    let workgroup_size = entry.workgroup_size;
    let loaded = loaded_images(&module);
    let integer_samplers = integer_samplers(&module);
    let mut blocks = Vec::new();
    let mut resources = Vec::new();
    let mut next = 0u32;
    let handles: Vec<_> = module.global_variables.iter().map(|(h, _)| h).collect();
    for h in handles {
        let g = module.global_variables[h].clone();
        let mut name = g.name.clone().unwrap_or_default();
        let kind = match g.space {
            AddressSpace::Uniform => {
                let TypeInner::Struct { members, span } = &module.types[g.ty].inner else {
                    bail!("uniform block `{name}` is not a struct");
                };
                let uniforms = members
                    .iter()
                    .filter_map(|m| uniform(&module, m.name.as_deref()?, m.ty, m.offset))
                    .collect();
                blocks.push(UniformBlock {
                    // glslang's cbuffer variables are anonymous; their struct type has the
                    // cbuffer's name (a constant buffer binds to the cbuffer by it).
                    name: Some(name.clone())
                        .filter(|n| !n.is_empty())
                        .or_else(|| module.types[g.ty].name.clone())
                        .unwrap_or_default(),
                    binding: next,
                    size: *span,
                    uniforms,
                });
                None
            }
            AddressSpace::Storage { access } => Some(ResourceKind::Buffer {
                read_only: !access.contains(naga::StorageAccess::STORE),
            }),
            AddressSpace::Handle => match module.types[g.ty].inner.clone() {
                TypeInner::Sampler { comparison } => {
                    let texture = name
                        .strip_prefix("sampler_")
                        .or_else(|| name.strip_prefix("sampler"))
                        .filter(|t| !t.is_empty())
                        .map(str::to_string);
                    Some(ResourceKind::Sampler {
                        texture,
                        comparison,
                        unfiltered: integer_samplers.contains(&h),
                    })
                }
                TypeInner::Image {
                    dim,
                    arrayed,
                    class,
                } => {
                    name = crate::samplers::variable_texture(&name).to_string();
                    let dim = texture_dim(&name, dim, arrayed)?;
                    match class {
                        ImageClass::Sampled { kind, multi } => {
                            if multi {
                                bail!("multisampled texture `{name}` is not supported");
                            }
                            Some(ResourceKind::Texture {
                                dim,
                                sample: match kind {
                                    naga::ScalarKind::Sint => "sint",
                                    naga::ScalarKind::Uint => "uint",
                                    _ => "float",
                                }
                                .into(),
                            })
                        }
                        ImageClass::Depth { .. } => Some(ResourceKind::Texture {
                            dim,
                            sample: "depth".into(),
                        }),
                        ImageClass::Storage { format, .. } => {
                            let format = storage_formats
                                .iter()
                                .find(|(n, _)| *n == name)
                                .and_then(|(_, f)| storage_format(f))
                                .unwrap_or(format);
                            let write_only = !loaded.contains(&h);
                            let access = if write_only {
                                naga::StorageAccess::STORE
                            } else {
                                naga::StorageAccess::LOAD | naga::StorageAccess::STORE
                            };
                            let ty = module.types.insert(
                                naga::Type {
                                    name: None,
                                    inner: TypeInner::Image {
                                        dim: match dim {
                                            TextureDim::D3 => naga::ImageDimension::D3,
                                            TextureDim::Cube | TextureDim::CubeArray => {
                                                naga::ImageDimension::Cube
                                            }
                                            _ => naga::ImageDimension::D2,
                                        },
                                        arrayed,
                                        class: ImageClass::Storage { format, access },
                                    },
                                },
                                naga::Span::UNDEFINED,
                            );
                            module.global_variables[h].ty = ty;
                            Some(ResourceKind::StorageTexture {
                                dim,
                                format: format!("{format:?}"),
                                write_only,
                            })
                        }
                        ImageClass::External => bail!("external texture `{name}` is not supported"),
                    }
                }
                other => bail!("resource `{name}` ({other:?}) is not supported"),
            },
            _ => continue,
        };
        module.global_variables[h].binding = Some(ResourceBinding {
            group: 0,
            binding: next,
        });
        if let Some(kind) = kind {
            resources.push(KernelResource {
                name,
                binding: next,
                kind,
            });
        }
        next += 1;
    }
    // Image stores through a parameter would need the parameter's type changed too: rare.
    for f in module.functions.iter().map(|(_, f)| f) {
        for arg in &f.arguments {
            if matches!(
                module.types[arg.ty].inner,
                TypeInner::Image {
                    class: ImageClass::Storage { .. },
                    ..
                }
            ) {
                bail!("RW textures passed to functions are not supported");
            }
        }
    }
    // A safeguard: the kernel fitting inserts no types after their users today.
    crate::fit::order_types(&mut module);
    let info = naga::valid::Validator::new(
        naga::valid::ValidationFlags::all(),
        naga::valid::Capabilities::all(),
    )
    .validate(&module)
    .map_err(|e| anyhow!("invalid after fitting: {}", e.emit_to_string("")))?;
    let wgsl =
        naga::back::wgsl::write_string(&module, &info, naga::back::wgsl::WriterFlags::empty())
            .map_err(|e| anyhow!("writing WGSL: {e}"))?;
    let entry = crate::fit::wgsl_entry(&wgsl, naga::ShaderStage::Compute)?;
    Ok(CompiledKernel {
        wgsl,
        entry,
        workgroup_size,
        blocks,
        resources,
        keywords: Vec::new(),
    })
}

/// The kernel cache key: every input is length-prefixed, so two different inputs cannot join
/// into the same bytes ("ab"+"c" vs "a"+"bc"); the `kernel-v2` tag keeps keys of the earlier
/// unprefixed layout from ever matching.
fn kernel_cache_key(
    source: &str,
    kernel: &str,
    keywords: &[String],
    storage_formats: &[(String, String)],
    opengl_shading_language: bool,
) -> String {
    fn field(h: &mut blake3::Hasher, bytes: &[u8]) {
        h.update(&(bytes.len() as u64).to_le_bytes());
        h.update(bytes);
    }
    let mut h = blake3::Hasher::new();
    h.update(b"kernel-v2");
    h.update(&FORMAT_VERSION.to_le_bytes());
    h.update(&[opengl_shading_language as u8]);
    field(&mut h, source.as_bytes());
    field(&mut h, kernel.as_bytes());
    h.update(&(keywords.len() as u64).to_le_bytes());
    for keyword in keywords {
        field(&mut h, keyword.as_bytes());
    }
    h.update(&(storage_formats.len() as u64).to_le_bytes());
    for (name, format) in storage_formats {
        field(&mut h, name.as_bytes());
        field(&mut h, format.as_bytes());
    }
    h.finalize().to_hex().to_string()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::Settings;

    #[test]
    fn kernel_cache_key_separates_its_fields() {
        let rw = |pairs: &[(&str, &str)]| -> Vec<(String, String)> {
            pairs
                .iter()
                .map(|(a, b)| (a.to_string(), b.to_string()))
                .collect()
        };
        let keys =
            |words: &[&str]| -> Vec<String> { words.iter().map(|w| w.to_string()).collect() };
        // Field boundaries move without the bytes changing: every key must differ.
        let a = kernel_cache_key(
            "s",
            "CSMain",
            &keys(&[]),
            &rw(&[("ab", "Rgba8Unorm")]),
            false,
        );
        let b = kernel_cache_key(
            "s",
            "CSMain",
            &keys(&[]),
            &rw(&[("a", "bRgba8Unorm")]),
            false,
        );
        let c = kernel_cache_key(
            "s",
            "CSMai",
            &keys(&[]),
            &rw(&[("nab", "Rgba8Unorm")]),
            false,
        );
        let d = kernel_cache_key("s", "CSMain", &keys(&["A B"]), &rw(&[]), false);
        let e = kernel_cache_key("s", "CSMain", &keys(&["A", "B"]), &rw(&[]), false);
        // A kernel compiled for OpenGL's shading language is cached apart.
        assert_ne!(
            kernel_cache_key("s", "CSMain", &keys(&[]), &rw(&[]), false),
            kernel_cache_key("s", "CSMain", &keys(&[]), &rw(&[]), true)
        );
        let all = [&a, &b, &c, &d, &e];
        for (i, x) in all.iter().enumerate() {
            for y in &all[i + 1..] {
                assert_ne!(x, y);
            }
        }
        assert_eq!(
            a,
            kernel_cache_key(
                "s",
                "CSMain",
                &keys(&[]),
                &rw(&[("ab", "Rgba8Unorm")]),
                false
            )
        );
    }

    const SOURCE: &str = r#"
#pragma kernel Fill
#pragma kernel Scale SCALE_TWICE=1
#pragma multi_compile __ OFFSET_ON
RWStructuredBuffer<float> _Values;
StructuredBuffer<float4> _Input;
RWTexture2D<float4> _Result;
Texture2D<float4> _Source;
SamplerState sampler_Source;
float _Factor;
float4 _Tint;
cbuffer Params { uint _Count; float _Offset; };
[numthreads(8, 4, 1)]
void Fill(uint3 id : SV_DispatchThreadID)
{
    float v = _Input[id.x].x * _Factor;
#ifdef OFFSET_ON
    v += _Offset;
#endif
    if (id.x < _Count) _Values[id.x] = v;
    _Result[id.xy] = _Tint * _Source.SampleLevel(sampler_Source, float2(0.5, 0.5), 0);
}
[numthreads(64, 1, 1)]
void Scale(uint3 id : SV_DispatchThreadID)
{
#ifdef SCALE_TWICE
    _Values[id.x] = _Values[id.x] * 2;
#endif
}
"#;

    fn program() -> ComputeProgram {
        ComputeProgram {
            file: PathBuf::from("Test.compute"),
            source: SOURCE.into(),
        }
    }

    #[test]
    fn packed_float3_buffers_support_whole_vector_and_component_accesses() {
        let program = ComputeProgram {
            file: PathBuf::from("Packed.compute"),
            source: "#pragma kernel Copy\nRWStructuredBuffer<float3> values;\n[numthreads(1,1,1)] void Copy(uint3 id : SV_DispatchThreadID) { float3 v = values[id.x]; values[id.x + 1] = v + 1; values[id.x].y = 9; }".into(),
        };
        let mut compiler = ShaderCompiler::new(Settings::default()).unwrap();
        let kernel = compiler.compile_kernel(&program, "Copy", &[], &[]).unwrap();
        let module = naga::front::wgsl::parse_str(&kernel.wgsl).unwrap();
        assert!(module.types.iter().any(|(_, t)| matches!(
            t.inner,
            naga::TypeInner::Array {
                size: naga::ArraySize::Dynamic,
                stride: 12,
                ..
            }
        )));
    }

    /// A kernel that compares a texture and reads it with `Load` is refused for OpenGL's shading
    /// language and compiles otherwise; one that only compares compiles for OpenGL too.
    #[test]
    fn a_kernel_loading_a_compared_texture_is_refused_in_opengl_shading_language() {
        let program = |body: &str| ComputeProgram {
            file: PathBuf::from("Depth.compute"),
            source: format!(
                "#pragma kernel K\nTexture2D<float> _Depth;\nSamplerComparisonState sampler_Depth;\nRWStructuredBuffer<float> _Out;\n[numthreads(1, 1, 1)] void K(uint3 id : SV_DispatchThreadID) {{ _Out[0] = {body}; }}"
            ),
        };
        let compile = |body: &str, opengl_shading_language| {
            let mut compiler = ShaderCompiler::new(Settings {
                opengl_shading_language,
                ..Settings::default()
            })
            .unwrap();
            compiler
                .compile_kernel(&program(body), "K", &[], &[])
                .map(|_| ())
        };
        let both = "_Depth.SampleCmpLevelZero(sampler_Depth, float2(0.5, 0.5), 0.5) + _Depth.Load(int3(0, 0, 0))";
        let refused = compile(both, true).unwrap_err();
        assert!(
            format!("{refused:#}").contains("compared and read with Load"),
            "{refused:#}"
        );
        compile(both, false).unwrap();
        compile(
            "_Depth.SampleCmpLevelZero(sampler_Depth, float2(0.5, 0.5), 0.5)",
            true,
        )
        .unwrap();
    }

    #[test]
    fn reserved_entry_names_match_emitted_wgsl() {
        let program = ComputeProgram {
            file: PathBuf::from("Reserved.compute"),
            source: "#pragma kernel array\nRWStructuredBuffer<float> _Out;\n[numthreads(1, 1, 1)] void array(uint3 id : SV_DispatchThreadID) { _Out[id.x] = 1; }".into(),
        };
        let mut compiler = ShaderCompiler::new(Settings::default()).unwrap();
        let kernel = compiler
            .compile_kernel(&program, "array", &[], &[])
            .unwrap();
        let module = naga::front::wgsl::parse_str(&kernel.wgsl).unwrap();
        assert_ne!(kernel.entry, "array");
        assert_eq!(module.entry_points[0].name, kernel.entry);
    }

    /// Append, Consume and counter methods compile with each buffer's hidden counter in a block
    /// of its own, `<name>@count`, which the renderer binds to the buffer's counter.
    #[test]
    fn counters_are_blocks_named_after_their_buffers() {
        let program = ComputeProgram {
            file: PathBuf::from("Counters.compute"),
            source: r#"
#pragma kernel Append
#pragma kernel Consume
#pragma kernel Count
AppendStructuredBuffer<uint> _Append;
ConsumeStructuredBuffer<uint> _Consume;
RWStructuredBuffer<uint> _Counter;
RWStructuredBuffer<uint> _Out;
[numthreads(64, 1, 1)] void Append(uint3 id : SV_DispatchThreadID) { if (id.x % 3 == 0) _Append.Append(id.x); }
[numthreads(8, 1, 1)] void Consume(uint3 id : SV_DispatchThreadID) { _Out[id.x] = _Consume.Consume(); }
[numthreads(16, 1, 1)] void Count(uint3 id : SV_DispatchThreadID) { uint i = _Counter.IncrementCounter(); _Counter[i] = id.x; InterlockedAdd(_Out[0], 1); }
"#
            .into(),
        };
        let mut compiler = ShaderCompiler::new(Settings::default()).unwrap();
        for (kernel, buffer) in [
            ("Append", "_Append"),
            ("Consume", "_Consume"),
            ("Count", "_Counter"),
        ] {
            let k = compiler.compile_kernel(&program, kernel, &[], &[]).unwrap();
            let names: Vec<_> = k.resources.iter().map(|r| r.name.as_str()).collect();
            let counter = format!("{buffer}@count");
            assert!(names.contains(&buffer), "{kernel}: {names:?}");
            assert!(names.contains(&counter.as_str()), "{kernel}: {names:?}");
            naga::front::wgsl::parse_str(&k.wgsl).unwrap();
        }
    }

    /// A cbuffer's block carries the cbuffer's name, which a constant buffer binds it by
    /// (`ComputeShader.SetConstantBuffer`); loose uniforms stay in `$Global`.
    #[test]
    fn cbuffer_blocks_are_named_after_their_cbuffers() {
        let program = ComputeProgram {
            file: PathBuf::from("Constants.compute"),
            source: "#pragma kernel Read\ncbuffer Params { uint4 _Values; };\nuint _Tag;\nRWStructuredBuffer<uint> _Out;\n[numthreads(4, 1, 1)] void Read(uint id : SV_DispatchThreadID) { _Out[id] = _Values[id] + _Tag; }".into(),
        };
        let mut compiler = ShaderCompiler::new(Settings::default()).unwrap();
        let k = compiler.compile_kernel(&program, "Read", &[], &[]).unwrap();
        let block = |member: &str| {
            k.blocks
                .iter()
                .find(|b| b.uniforms.iter().any(|u| u.name == member))
                .map(|b| b.name.clone())
        };
        assert_eq!(
            block("_Values").as_deref(),
            Some("Params"),
            "{:?}",
            k.blocks
        );
        assert_eq!(block("_Tag").as_deref(), Some("$Global"), "{:?}", k.blocks);
    }

    /// Render textures of the normalized 16-bit formats are written in them (where the device
    /// lets kernels write them, the renderer stores them so and names them here).
    #[test]
    fn rw_textures_compile_in_normalized_16_bit_formats() {
        use naga::StorageFormat as F;
        assert_eq!(storage_format("Rg16Unorm"), Some(F::Rg16Unorm));
        assert_eq!(storage_format("Rgba16Unorm"), Some(F::Rgba16Unorm));
        assert_eq!(storage_format("R16Unorm"), Some(F::R16Unorm));
        let mut compiler = ShaderCompiler::new(Settings::default()).unwrap();
        for (name, wgsl) in [
            ("R16Unorm", "r16unorm"),
            ("Rg16Unorm", "rg16unorm"),
            ("Rgba16Unorm", "rgba16unorm"),
            ("R16Snorm", "r16snorm"),
            ("Rg16Snorm", "rg16snorm"),
            ("Rgba16Snorm", "rgba16snorm"),
        ] {
            let k = compiler
                .compile_kernel(&program(), "Fill", &[], &[("_Result".into(), name.into())])
                .unwrap();
            let result = k.resources.iter().find(|r| r.name == "_Result").unwrap();
            assert_eq!(
                result.kind,
                ResourceKind::StorageTexture {
                    dim: TextureDim::D2,
                    format: name.into(),
                    write_only: true
                }
            );
            assert!(k.wgsl.contains(wgsl), "{}", k.wgsl);
        }
    }

    #[test]
    fn kernels_and_keywords() {
        let mut compiler = ShaderCompiler::new(Settings::default()).unwrap();
        let p = compiler.compute_pragmas(&program()).unwrap();
        let names: Vec<_> = p.kernels.iter().map(|k| k.name.as_str()).collect();
        assert_eq!(names, ["Fill", "Scale"]);
        assert_eq!(p.kernels[1].defines, [("SCALE_TWICE".into(), "1".into())]);
        assert_eq!(p.select(&["OFFSET_ON".into()]), ["OFFSET_ON"]);
    }

    #[test]
    fn compiles_a_kernel() {
        let mut compiler = ShaderCompiler::new(Settings::default()).unwrap();
        let k = compiler
            .compile_kernel(
                &program(),
                "Fill",
                &["OFFSET_ON".into()],
                &[("_Result".into(), "Rgba8Unorm".into())],
            )
            .unwrap();
        assert_eq!(k.workgroup_size, [8, 4, 1]);
        let kind = |n: &str| {
            k.resources
                .iter()
                .find(|r| r.name == n)
                .map(|r| r.kind.clone())
                .unwrap_or_else(|| panic!("{n} in {:?}", k.resources))
        };
        assert_eq!(kind("_Values"), ResourceKind::Buffer { read_only: false });
        assert_eq!(kind("_Input"), ResourceKind::Buffer { read_only: true });
        assert_eq!(
            kind("_Result"),
            ResourceKind::StorageTexture {
                dim: TextureDim::D2,
                format: "Rgba8Unorm".into(),
                write_only: true
            }
        );
        assert!(
            matches!(kind("sampler_Source"), ResourceKind::Sampler { texture: Some(t), .. } if t == "Source" || t == "_Source")
        );
        let members: Vec<_> = k
            .blocks
            .iter()
            .flat_map(|b| b.uniforms.iter().map(|u| u.name.as_str()))
            .collect();
        for name in ["_Factor", "_Tint", "_Count", "_Offset"] {
            assert!(members.contains(&name), "{name} in {members:?}");
        }
        assert!(k.wgsl.contains("@compute"), "{}", k.wgsl);
        assert!(k.wgsl.contains("rgba8unorm"), "{}", k.wgsl);
        let second = compiler
            .compile_kernel(&program(), "Scale", &[], &[])
            .unwrap();
        assert_eq!(second.workgroup_size, [64, 1, 1]);
    }

    /// A sampler that gathers an integer texture is marked unfiltered (STP gathers its packed
    /// depth and motion through a point sampler); one sampling a float texture is not.
    #[test]
    fn samplers_of_integer_textures_are_unfiltered() {
        let program = ComputeProgram {
            file: PathBuf::from("Gather.compute"),
            source: "#pragma kernel Main\nTexture2D<uint> _Ids; Texture2D _Color;\n\
                SamplerState s_point_clamp_sampler; SamplerState s_linear_clamp_sampler;\n\
                RWStructuredBuffer<float> _Out;\n\
                [numthreads(1,1,1)] void Main(uint3 id : SV_DispatchThreadID) \
                { _Out[id.x] = _Ids.GatherRed(s_point_clamp_sampler, float2(0.5, 0.5)).x \
                + _Color.SampleLevel(s_linear_clamp_sampler, float2(0.5, 0.5), 0).x; }"
                .into(),
        };
        let mut compiler = ShaderCompiler::new(Settings::default()).unwrap();
        let k = compiler.compile_kernel(&program, "Main", &[], &[]).unwrap();
        let unfiltered = |n: &str| {
            k.resources
                .iter()
                .find(|r| r.name == n)
                .map(|r| match r.kind {
                    ResourceKind::Sampler { unfiltered, .. } => unfiltered,
                    _ => panic!("{n} is not a sampler"),
                })
        };
        assert_eq!(unfiltered("s_point_clamp_sampler"), Some(true));
        assert_eq!(unfiltered("s_linear_clamp_sampler"), Some(false));
    }
    #[test]
    fn named_and_predefined_compute_samplers_keep_distinct_bindings() {
        let program = ComputeProgram {
            file: PathBuf::from("SamplerBindings.compute"),
            source: r#"#pragma kernel Main
Texture2D<float4> _Source;
SamplerState sampler_Source;
SamplerState s_linear_clamp_sampler;
SamplerState s_point_repeat_sampler;
RWStructuredBuffer<float4> _Out;
[numthreads(1,1,1)] void Main(uint3 id : SV_DispatchThreadID) {
    _Out[0] = _Source.SampleLevel(sampler_Source, float2(.5,.5), 0);
    _Out[1] = _Source.SampleLevel(s_linear_clamp_sampler, float2(.5,.5), 0);
    _Out[2] = _Source.SampleLevel(s_point_repeat_sampler, float2(.5,.5), 0);
}"#
            .into(),
        };
        let mut compiler = ShaderCompiler::new(Settings::default()).unwrap();
        let compiled = compiler.compile_kernel(&program, "Main", &[], &[]).unwrap();
        let samplers: Vec<_> = compiled
            .resources
            .iter()
            .filter_map(|resource| {
                if let ResourceKind::Sampler {
                    texture,
                    comparison,
                    unfiltered,
                } = &resource.kind
                {
                    assert!(!comparison && !unfiltered);
                    Some((resource.name.as_str(), texture.as_deref()))
                } else {
                    None
                }
            })
            .collect();
        assert_eq!(samplers.len(), 3);
        // The texture has a variable for each of them, every one bound by the texture's name.
        let textures: Vec<u32> = compiled
            .resources
            .iter()
            .filter(|r| matches!(r.kind, ResourceKind::Texture { .. }))
            .map(|r| {
                assert_eq!(r.name, "_Source");
                r.binding
            })
            .collect();
        assert_eq!(textures.len(), 3);
        assert!(textures[0] != textures[1] && textures[1] != textures[2]);
        assert!(
            samplers
                .iter()
                .any(|&(name, texture)| name == "sampler_Source"
                    && matches!(texture, Some("Source" | "_Source")))
        );
        assert!(samplers.contains(&("s_linear_clamp_sampler", None)));
        assert!(samplers.contains(&("s_point_repeat_sampler", None)));
    }
}
