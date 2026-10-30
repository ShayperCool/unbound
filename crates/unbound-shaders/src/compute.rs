//! Compute shaders (`.compute` files): kernels compiled from Unity's sources to WGSL compute
//! programs. The same pipeline as draw programs (Unity preprocessing, glslang, naga); resources
//! keep their names and are bound in group 0 one after another, so the renderer binds a
//! dispatch's buffers, textures and uniforms by name, as `ComputeShader.SetBuffer` names them.
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
    /// (`linear`/`point`/`trilinear`, `clamp`/`repeat`/`mirror`/`mirroronce`).
    Sampler {
        texture: Option<String>,
        comparison: bool,
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
        let defines = self.defines(&p, &[], 50, true, "SHADER_STAGE_COMPUTE");
        let out = self.preprocess(&p, &defines)?;
        Ok(ComputePragmas::read(&out.pragmas))
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
        let mut defines = self.defines(&p, &selected, 50, true, "SHADER_STAGE_COMPUTE");
        defines.extend(declared.defines.iter().cloned());
        let source = self.preprocess(&p, &defines)?.text;
        let hash = {
            let mut h = blake3::Hasher::new();
            h.update(b"kernel");
            h.update(&FORMAT_VERSION.to_le_bytes());
            h.update(&(source.len() as u64).to_le_bytes());
            h.update(source.as_bytes());
            h.update(kernel.as_bytes());
            h.update(selected.join(" ").as_bytes());
            for (name, format) in storage_formats {
                h.update(name.as_bytes());
                h.update(format.as_bytes());
            }
            h.finalize().to_hex().to_string()
        };
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
        let mut compiled = fit_kernel(&spv, storage_formats)
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
        "R16Float" => F::R16Float,
        "R32Float" => F::R32Float,
        "R32Uint" => F::R32Uint,
        "R32Sint" => F::R32Sint,
        "Rg16Float" => F::Rg16Float,
        "Rg32Float" => F::Rg32Float,
        "Rgba8Unorm" => F::Rgba8Unorm,
        "Rgba16Float" => F::Rgba16Float,
        "Rgba32Float" => F::Rgba32Float,
        "Rgba32Uint" => F::Rgba32Uint,
        "Rgba32Sint" => F::Rgba32Sint,
        "Rg11b10Ufloat" => F::Rg11b10Ufloat,
        "Rgb10a2Unorm" => F::Rgb10a2Unorm,
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

/// Binds a kernel's resources in group 0 by order, reads its uniform blocks, sets its RW
/// textures' formats and access, and writes WGSL.
fn fit_kernel(spv: &[u32], storage_formats: &[(String, String)]) -> Result<CompiledKernel> {
    let words = scan(
        &unify_int_signedness(&pad_array_elements(&align_uniform_members(
            &strip_relaxed_precision(spv),
        )?)?)?,
        Stage::Compute,
    )?
    .words;
    let mut module = parse(&words).context("compute program")?;
    let entry = module
        .entry_points
        .iter()
        .find(|e| e.stage == naga::ShaderStage::Compute)
        .ok_or_else(|| anyhow!("no compute entry point"))?;
    let workgroup_size = entry.workgroup_size;
    let loaded = loaded_images(&module);
    let mut blocks = Vec::new();
    let mut resources = Vec::new();
    let mut next = 0u32;
    let handles: Vec<_> = module.global_variables.iter().map(|(h, _)| h).collect();
    for h in handles {
        let g = module.global_variables[h].clone();
        let name = g.name.clone().unwrap_or_default();
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
                    name: name.clone(),
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
                    })
                }
                TypeInner::Image {
                    dim,
                    arrayed,
                    class,
                } => {
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

#[cfg(test)]
mod tests {
    use super::*;
    use crate::Settings;

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
}
