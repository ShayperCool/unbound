//! Inline sampler states in graphics programs on the OpenGL Core device, the one Unbound reports
//! (`SystemInfo.graphicsDeviceType`): the Unity 6000.0 Manual (Using sampler states) says "Inline
//! samplers don't work if you target the OpenGL ES or OpenGL Core graphics APIs", and the Editor
//! on OpenGL Core samples a texture through its own sampler state whatever inline sampler the
//! program names (tests/projects/urp: `upscale_point` equals `upscale_linear`; `inline_sampler`).
//! A texture sampled through another texture's sampler (`_Detail.Sample(sampler_MainTex, uv)`)
//! keeps that sampler: unmeasured.
//!
//! Compute kernels retain predefined sampler states (tests/projects/renderapi computeSamplers).
//!
//! [`own_samplers`] rewrites a graphics program's SPIR-V so that every sample of a texture through an
//! inline sampler (`sampler_PointClamp`, `s_linear_clamp_sampler`: a sampler named like no
//! texture's, whose name spells a state) uses the texture's own sampler (`sampler<texture>`,
//! declared when the program does not). glslang passes textures and samplers to functions
//! resolved, so every sample names its texture and sampler variables. Comparison samples keep
//! their sampler (a shadow map's comparison is the sampler's), and so does a sample whose
//! texture is not a variable (an inline state is then what the sampler binds) or whose texture's
//! own sampler compares (a sampler is one kind or the other).

use crate::InlineSampler;
use anyhow::{Result, bail};
use std::collections::{BTreeMap, HashMap, HashSet};

const OP_NAME: u32 = 5;
const OP_TYPE_INT: u32 = 21;
const OP_TYPE_IMAGE: u32 = 25;
const OP_TYPE_SAMPLER: u32 = 26;
const OP_TYPE_POINTER: u32 = 32;
const OP_FUNCTION: u32 = 54;
const OP_FUNCTION_END: u32 = 56;
const OP_VARIABLE: u32 = 59;
const OP_LOAD: u32 = 61;
const OP_STORE: u32 = 62;
const OP_COPY_OBJECT: u32 = 83;
const OP_SAMPLED_IMAGE: u32 = 86;
const OP_DECORATE: u32 = 71;
const DESCRIPTOR_SET: u32 = 34;
const BINDING: u32 = 33;
/// The type declarations (`OpTypeVoid` .. `OpTypeForwardPointer`): decorations come before.
const FIRST_TYPE: u32 = 19;
const LAST_TYPE: u32 = 39;
/// Sampling with a depth reference (`OpImageSampleDref*`, `OpImageSampleProjDref*`,
/// `OpImageDrefGather`).
const DEPTH_REFERENCE_OPS: [u32; 5] = [89, 90, 93, 94, 97];
const UNIFORM_CONSTANT: u32 = 0;

fn string(words: &[u32]) -> String {
    let bytes: Vec<u8> = words.iter().flat_map(|w| w.to_le_bytes()).collect();
    let end = bytes.iter().position(|b| *b == 0).unwrap_or(bytes.len());
    String::from_utf8_lossy(&bytes[..end]).into_owned()
}

fn name_words(id: u32, name: &str) -> Vec<u32> {
    let mut bytes = name.as_bytes().to_vec();
    bytes.push(0);
    while bytes.len() % 4 != 0 {
        bytes.push(0);
    }
    let mut out = vec![0, id];
    out.extend(
        bytes
            .chunks_exact(4)
            .map(|c| u32::from_le_bytes([c[0], c[1], c[2], c[3]])),
    );
    out[0] = ((out.len() as u32) << 16) | OP_NAME;
    out
}

/// The texture whose own sampler a sampler named `name` is (`sampler_MainTex` is `_MainTex`'s),
/// as the renderer pairs them.
fn sampler_texture(name: &str) -> Option<&str> {
    name.strip_prefix("sampler").filter(|t| !t.is_empty())
}

/// Whether a sampler named `name` is an inline state: named like no texture of `textures`, its
/// name spelling a filter and a wrap mode. Comparison states (`..._Compare`) are left alone.
fn inline_state(name: &str, textures: &HashSet<&str>) -> bool {
    sampler_texture(name).is_none_or(|t| !textures.contains(t))
        && InlineSampler::from_name(name).is_some()
        && !name.to_ascii_lowercase().contains("compare")
}

/// The SPIR-V with samples through inline samplers turned to their texture's own sampler (see
/// the module).
pub(crate) fn own_samplers(words: &[u32]) -> Result<Vec<u32>> {
    if words.len() < 5 {
        bail!("empty SPIR-V");
    }
    let list = crate::fit::instructions(words)?;
    let inst = |k: usize| &words[list[k].0..list[k].0 + list[k].1];
    let op = |k: usize| inst(k)[0] & 0xffff;

    // Globals and types.
    let mut names: HashMap<u32, String> = HashMap::new();
    let mut int_types = HashSet::new();
    let mut image_types = HashSet::new();
    let mut sampler_types = HashSet::new();
    let mut pointers: HashMap<u32, u32> = HashMap::new();
    let mut textures: HashMap<u32, String> = HashMap::new();
    // Sampler variables: id → (pointer type, instruction index).
    let mut samplers: BTreeMap<u32, (u32, usize)> = BTreeMap::new();
    let mut in_function = false;
    for k in 0..list.len() {
        let w = inst(k);
        match op(k) {
            OP_NAME if w.len() >= 3 => {
                names.insert(w[1], string(&w[2..]));
            }
            OP_TYPE_INT => {
                int_types.insert(w[1]);
            }
            // Integer textures are read unfiltered (the renderer binds them a point sampler):
            // their samples keep the sampler they name.
            OP_TYPE_IMAGE if w.len() >= 3 && !int_types.contains(&w[2]) => {
                image_types.insert(w[1]);
            }
            OP_TYPE_SAMPLER => {
                sampler_types.insert(w[1]);
            }
            OP_TYPE_POINTER if w.len() >= 4 && w[2] == UNIFORM_CONSTANT => {
                pointers.insert(w[1], w[3]);
            }
            OP_FUNCTION => in_function = true,
            OP_FUNCTION_END => in_function = false,
            OP_VARIABLE if !in_function && w.len() >= 4 && w[3] == UNIFORM_CONSTANT => {
                let Some(pointee) = pointers.get(&w[1]) else {
                    continue;
                };
                if image_types.contains(pointee) {
                    textures.insert(w[2], names.get(&w[2]).cloned().unwrap_or_default());
                } else if sampler_types.contains(pointee) {
                    samplers.insert(w[2], (w[1], k));
                }
            }
            _ => {}
        }
    }
    let texture_names: HashSet<&str> = textures.values().map(String::as_str).collect();
    let inline: HashSet<u32> = samplers
        .keys()
        .copied()
        .filter(|id| {
            names
                .get(id)
                .is_some_and(|n| inline_state(n, &texture_names))
        })
        .collect();
    if inline.is_empty() {
        return Ok(words.to_vec());
    }

    // The values loaded or copied (and what function-local variables hold), the samples.
    let mut loads: HashMap<u32, u32> = HashMap::new();
    // The type of each loaded or copied value.
    let mut value_types: HashMap<u32, u32> = HashMap::new();
    let mut stores: HashMap<u32, u32> = HashMap::new();
    let mut depth_samples: HashSet<u32> = HashSet::new();
    let mut samples: Vec<usize> = Vec::new();
    for k in 0..list.len() {
        let w = inst(k);
        match op(k) {
            OP_LOAD | OP_COPY_OBJECT if w.len() >= 4 => {
                loads.insert(w[2], w[3]);
                value_types.insert(w[2], w[1]);
            }
            OP_STORE if w.len() >= 3 => {
                stores.insert(w[1], w[2]);
            }
            OP_SAMPLED_IMAGE if w.len() >= 5 => samples.push(k),
            o if DEPTH_REFERENCE_OPS.contains(&o) && w.len() >= 4 => {
                depth_samples.insert(w[3]);
            }
            _ => {}
        }
    }
    // The global variable a value comes from (glslang passes textures and samplers to functions
    // resolved: every sample names its globals).
    let origin = |mut id: u32| -> Option<u32> {
        for _ in 0..64 {
            if textures.contains_key(&id) || samplers.contains_key(&id) {
                return Some(id);
            }
            id = *loads.get(&id).or_else(|| stores.get(&id))?;
        }
        None
    };
    // The samplers that compare (their samples have a depth reference).
    let comparing: HashSet<u32> = samples
        .iter()
        .filter(|&&k| depth_samples.contains(&inst(k)[2]))
        .filter_map(|&k| origin(inst(k)[4]))
        .collect();
    let compares = |t: u32| {
        let own = format!("sampler{}", textures[&t]);
        samplers
            .keys()
            .any(|id| comparing.contains(id) && names.get(id) == Some(&own))
    };
    // Each sample of a texture through an inline sampler, but comparisons and textures whose
    // own sampler compares.
    let rewrite_samples: Vec<(usize, u32)> = samples
        .into_iter()
        .filter_map(|k| {
            let w = inst(k);
            let (t, s) = (origin(w[3])?, origin(w[4])?);
            (!depth_samples.contains(&w[2])
                && textures.contains_key(&t)
                && inline.contains(&s)
                && !compares(t))
            .then_some((k, t))
        })
        .collect();
    if rewrite_samples.is_empty() {
        return Ok(words.to_vec());
    }

    // The textures' own samplers, declared after an inline one where the program has none.
    let mut bound = words[3];
    let by_name: HashMap<&str, u32> = samplers
        .keys()
        .filter_map(|id| Some((names.get(id)?.as_str(), *id)))
        .collect();
    let &(pointer_type, declared_at) = samplers
        .iter()
        .find(|(id, _)| inline.contains(id))
        .map(|(_, v)| v)
        .expect("an inline sampler");
    // New variables are bound after the program's (the renderer rebinds them by name).
    let mut next_binding = list
        .iter()
        .map(|&(at, n)| &words[at..at + n])
        .filter(|i| (i[0] & 0xffff) == OP_DECORATE && i.len() >= 4 && i[2] == BINDING)
        .map(|i| i[3] + 1)
        .max()
        .unwrap_or(0);
    let mut own: HashMap<u32, u32> = HashMap::new();
    let mut declarations: Vec<(u32, String)> = Vec::new();
    for &(_, t) in &rewrite_samples {
        if own.contains_key(&t) {
            continue;
        }
        let name = format!("sampler{}", textures[&t]);
        let id = match by_name.get(name.as_str()) {
            Some(id) => *id,
            None => {
                let id = bound;
                bound += 1;
                declarations.push((id, name));
                id
            }
        };
        own.insert(t, id);
    }

    let mut out =
        Vec::with_capacity(words.len() + 8 * (declarations.len() + rewrite_samples.len()));
    out.extend_from_slice(&words[..5]);
    out[3] = 0;
    let samples_at: HashMap<usize, u32> = rewrite_samples.into_iter().collect();
    let (mut named, mut decorated) = (false, false);
    for k in 0..list.len() {
        let w = inst(k);
        if op(k) == OP_NAME && !named {
            named = true;
            for (id, name) in &declarations {
                out.extend(name_words(*id, name));
            }
        }
        if !decorated && (op(k) == OP_DECORATE || (FIRST_TYPE..=LAST_TYPE).contains(&op(k))) {
            decorated = true;
            for (id, _) in &declarations {
                out.extend_from_slice(&[(4 << 16) | OP_DECORATE, *id, DESCRIPTOR_SET, 0]);
                out.extend_from_slice(&[(4 << 16) | OP_DECORATE, *id, BINDING, next_binding]);
                next_binding += 1;
            }
        }
        if let Some(t) = samples_at.get(&k)
            && let Some(&sampler_type) = value_types.get(&w[4])
        {
            // A load of the texture's own sampler, sampled with instead.
            let loaded = bound;
            bound += 1;
            out.extend_from_slice(&[(4 << 16) | OP_LOAD, sampler_type, loaded, own[t]]);
            out.extend_from_slice(&[w[0], w[1], w[2], w[3], loaded]);
            out.extend_from_slice(&w[5..]);
            continue;
        }
        out.extend_from_slice(w);
        if k == declared_at {
            for (id, _) in &declarations {
                out.extend_from_slice(&[
                    (4 << 16) | OP_VARIABLE,
                    pointer_type,
                    *id,
                    UNIFORM_CONSTANT,
                ]);
            }
        }
    }
    out[3] = bound;
    Ok(out)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn spirv(source: &str, entry: &str, kind: shaderc::ShaderKind) -> Vec<u32> {
        let compiler = shaderc::Compiler::new().unwrap();
        let mut o = shaderc::CompileOptions::new().unwrap();
        o.set_source_language(shaderc::SourceLanguage::HLSL);
        o.set_auto_bind_uniforms(true);
        o.set_optimization_level(shaderc::OptimizationLevel::Zero);
        o.set_target_env(
            shaderc::TargetEnv::Vulkan,
            shaderc::EnvVersion::Vulkan1_1 as u32,
        );
        compiler
            .compile_into_spirv(source, kind, "t", entry, Some(&o))
            .unwrap()
            .as_binary()
            .to_vec()
    }

    /// For every sample of the module: the texture and the sampler (their names). The module
    /// validates but for glslang's bindings (the renderer rebinds by name).
    fn sampled(words: &[u32]) -> Vec<(String, String)> {
        let module = crate::fit::parse(words).unwrap();
        naga::valid::Validator::new(
            naga::valid::ValidationFlags::all() - naga::valid::ValidationFlags::BINDINGS,
            naga::valid::Capabilities::all(),
        )
        .validate(&module)
        .unwrap();
        let mut pairs = Vec::new();
        let functions = module
            .functions
            .iter()
            .map(|(_, f)| f)
            .chain(module.entry_points.iter().map(|e| &e.function));
        for f in functions {
            let name = |h: naga::Handle<naga::Expression>| match &f.expressions[h] {
                naga::Expression::GlobalVariable(g) => {
                    module.global_variables[*g].name.clone().unwrap_or_default()
                }
                naga::Expression::FunctionArgument(i) => format!("arg{i}"),
                other => format!("{other:?}"),
            };
            for (_, e) in f.expressions.iter() {
                if let naga::Expression::ImageSample { image, sampler, .. } = e {
                    pairs.push((name(*image), name(*sampler)));
                }
            }
        }
        pairs.sort();
        pairs
    }

    #[test]
    fn inline_samplers_become_the_textures_own() {
        let source = r#"
Texture2D _BlitTexture;
Texture2D _Other;
SamplerState sampler_Other;
SamplerState sampler_PointClamp;
SamplerState sampler_LinearRepeat;
Texture2D _ShadowMap;
SamplerComparisonState sampler_LinearClampCompare;
Texture2D<uint> _Ids;
float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_Target
{
    return _BlitTexture.Sample(sampler_PointClamp, uv)
        + _Other.Sample(sampler_LinearRepeat, uv)
        + _Other.Sample(sampler_Other, uv)
        + _BlitTexture.SampleLevel(sampler_LinearRepeat, uv, 0)
        + _ShadowMap.SampleCmpLevelZero(sampler_LinearClampCompare, uv, 0.5)
        + _Ids.GatherRed(sampler_PointClamp, uv).x;
}
"#;
        let words = spirv(source, "main", shaderc::ShaderKind::Fragment);
        let fixed = own_samplers(&words).unwrap();
        let pairs = sampled(&fixed);
        assert_eq!(
            pairs,
            [
                ("_BlitTexture", "sampler_BlitTexture"),
                ("_BlitTexture", "sampler_BlitTexture"),
                ("_Ids", "sampler_PointClamp"),
                ("_Other", "sampler_Other"),
                ("_Other", "sampler_Other"),
                ("_ShadowMap", "sampler_LinearClampCompare"),
            ]
            .map(|(a, b)| (a.to_string(), b.to_string()))
        );
    }

    /// Through function parameters (the render pipelines' `TEXTURE2D_PARAM`), one inline
    /// sampler for two textures: each sample takes its own texture's sampler.
    #[test]
    fn parameters_sample_with_the_textures_own_sampler() {
        let source = r#"
Texture2D _A;
Texture2D _B;
SamplerState s_linear_clamp_sampler;
float4 Fetch(Texture2D t, SamplerState s, float2 uv) { return t.SampleLevel(s, uv, 0); }
float4 Twice(Texture2D t, SamplerState s, float2 uv) { return Fetch(t, s, uv) * 2; }
float4 Both(SamplerState s, float2 uv) { return _A.SampleLevel(s, uv, 0) + _B.SampleLevel(s, uv, 0); }
float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_Target
{
    return Twice(_A, s_linear_clamp_sampler, uv) + Fetch(_B, s_linear_clamp_sampler, uv)
        + Both(s_linear_clamp_sampler, uv);
}
"#;
        let words = spirv(source, "main", shaderc::ShaderKind::Fragment);
        let pairs = sampled(&own_samplers(&words).unwrap());
        assert!(!pairs.is_empty());
        assert!(
            pairs.iter().all(|(t, s)| *s == format!("sampler{t}")),
            "{pairs:?}"
        );
    }

    /// A texture whose own sampler compares keeps the inline sampler for its other samples.
    #[test]
    fn a_comparing_own_sampler_is_not_shared() {
        let source = r#"
Texture2D _Shadow;
SamplerComparisonState sampler_Shadow;
SamplerState sampler_PointClamp;
float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_Target
{
    return _Shadow.SampleCmpLevelZero(sampler_Shadow, uv, 0.5) + _Shadow.Sample(sampler_PointClamp, uv);
}
"#;
        let words = spirv(source, "main", shaderc::ShaderKind::Fragment);
        let pairs = sampled(&own_samplers(&words).unwrap());
        assert_eq!(
            pairs,
            [
                ("_Shadow", "sampler_PointClamp"),
                ("_Shadow", "sampler_Shadow")
            ]
            .map(|(a, b)| (a.to_string(), b.to_string()))
        );
    }

    /// A program without inline samplers is returned as it was.
    #[test]
    fn programs_without_inline_samplers_are_unchanged() {
        let source = r#"
Texture2D _MainTex;
SamplerState sampler_MainTex;
float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_Target { return _MainTex.Sample(sampler_MainTex, uv); }
"#;
        let words = spirv(source, "main", shaderc::ShaderKind::Fragment);
        assert_eq!(own_samplers(&words).unwrap(), words);
    }
}
