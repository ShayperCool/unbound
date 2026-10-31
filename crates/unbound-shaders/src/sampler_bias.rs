//! Per-texture LOD bias, which WebGPU's sampler descriptor cannot express. Add dedicated
//! uniforms and apply them to the generated WGSL sampling operations, including explicit LOD
//! and gradient operations. Integer texel loads never use sampler state.
use anyhow::{Result, anyhow, bail};
use naga::{AddressSpace, Module, Span, StructMember, Type, TypeInner};
use std::collections::HashMap;

pub(crate) fn append_uniforms(module: &mut Module, textures: &[String], base: u32) {
    if textures.is_empty() {
        return;
    }
    let span = Span::UNDEFINED;
    let scalar = module.types.insert(
        Type {
            name: None,
            inner: TypeInner::Scalar(naga::Scalar::F32),
        },
        span,
    );
    let block = module
        .global_variables
        .iter()
        .find(|(_, g)| g.space == AddressSpace::Uniform)
        .map(|(h, _)| h);
    let mut members = block
        .and_then(
            |h| match &module.types[module.global_variables[h].ty].inner {
                TypeInner::Struct { members, .. } => Some(members.clone()),
                _ => None,
            },
        )
        .unwrap_or_default();
    for i in 0..textures.len() {
        members.push(StructMember {
            name: Some(format!("unbound_mip_bias_{i}")),
            ty: scalar,
            binding: None,
            offset: base + i as u32 * 4,
        });
    }
    let ty = module.types.insert(
        Type {
            name: Some("UnboundBiasedUniforms".into()),
            inner: TypeInner::Struct {
                members,
                span: (base + textures.len() as u32 * 4).div_ceil(16) * 16,
            },
        },
        span,
    );
    match block {
        Some(h) => module.global_variables[h].ty = ty,
        None => {
            module.global_variables.append(
                naga::GlobalVariable {
                    name: Some("unbound_uniforms".into()),
                    space: AddressSpace::Uniform,
                    binding: None,
                    ty,
                    init: None,
                    memory_decorations: Default::default(),
                },
                span,
            );
        }
    }
}

pub(crate) fn rewrite(source: &str, textures: &[String], main: bool) -> Result<String> {
    if textures.is_empty() {
        return Ok(source.into());
    }
    let module = naga::front::wgsl::parse_str(source)
        .map_err(|e| anyhow!("reading sampler WGSL: {}", e.emit_to_string(source)))?;
    let mut images = HashMap::new();
    let mut biases = Vec::new();
    for (_, g) in module.global_variables.iter() {
        let Some(binding) = &g.binding else { continue };
        if binding.group == 0 && binding.binding == 0 {
            if let TypeInner::Struct { members, .. } = &module.types[g.ty].inner {
                for i in 0..textures.len() {
                    let field = format!("unbound_mip_bias_{i}");
                    let member = members
                        .iter()
                        .find(|m| {
                            m.name.as_deref().map(|n| n.trim_end_matches('_'))
                                == Some(field.as_str())
                        })
                        .ok_or_else(|| {
                            anyhow!(
                                "missing sampler bias uniform {field}; emitted fields {:?}",
                                members.iter().map(|m| &m.name).collect::<Vec<_>>()
                            )
                        })?;
                    biases.push(format!(
                        "{}.{}",
                        g.name.as_deref().unwrap(),
                        member.name.as_deref().unwrap()
                    ));
                }
            }
        }
        let TypeInner::Image { arrayed, .. } = module.types[g.ty].inner else {
            continue;
        };
        let index = match (binding.group, binding.binding) {
            (2, 0) if main => Some(0),
            (1, b) if b % 2 == 0 => Some(b as usize / 2 + usize::from(main)),
            _ => None,
        };
        if let Some(index) = index {
            images.insert(g.name.as_deref().unwrap().to_string(), (index, arrayed));
        }
    }
    if biases.len() != textures.len() {
        bail!("missing sampler bias uniform block");
    }
    rewrite_calls(source, &images, &biases)
}

fn rewrite_calls(
    source: &str,
    images: &HashMap<String, (usize, bool)>,
    biases: &[String],
) -> Result<String> {
    let bytes = source.as_bytes();
    let mut out = String::new();
    let mut i = 0;
    while i < bytes.len() {
        let start = i;
        if !(bytes[i].is_ascii_alphabetic() || bytes[i] == b'_') {
            out.push(bytes[i] as char);
            i += 1;
            continue;
        }
        i += 1;
        while i < bytes.len() && (bytes[i].is_ascii_alphanumeric() || bytes[i] == b'_') {
            i += 1;
        }
        let name = &source[start..i];
        if !matches!(
            name,
            "textureSample" | "textureSampleBias" | "textureSampleLevel" | "textureSampleGrad"
        ) {
            out.push_str(name);
            continue;
        }
        let mut open = i;
        while open < bytes.len() && bytes[open].is_ascii_whitespace() {
            open += 1;
        }
        if bytes.get(open) != Some(&b'(') {
            out.push_str(name);
            continue;
        }
        let mut depth = 1;
        let mut end = open + 1;
        let mut arg = end;
        let mut args = Vec::new();
        while end < bytes.len() {
            match bytes[end] {
                b'(' | b'[' => depth += 1,
                b')' | b']' => depth -= 1,
                b',' if depth == 1 => {
                    args.push(source[arg..end].trim().to_string());
                    arg = end + 1;
                }
                _ => {}
            }
            if depth == 0 {
                args.push(source[arg..end].trim().to_string());
                break;
            }
            end += 1;
        }
        if depth != 0 {
            bail!("unterminated generated sampling operation");
        }
        let (index, arrayed) = images.get(&args[0]).ok_or_else(|| {
            anyhow!(
                "sampling operation uses an unassociated texture: {}",
                args[0]
            )
        })?;
        for a in &mut args {
            *a = rewrite_calls(a, images, biases)?;
        }
        let bias = &biases[*index];
        let level = 3 + usize::from(*arrayed);
        let name = match name {
            "textureSample" => {
                args.insert(level, bias.clone());
                "textureSampleBias"
            }
            "textureSampleBias" | "textureSampleLevel" => {
                args[level] = format!("({} + {bias})", args[level]);
                name
            }
            "textureSampleGrad" => {
                args[level] = format!("({} * exp2({bias}))", args[level]);
                args[level + 1] = format!("({} * exp2({bias}))", args[level + 1]);
                name
            }
            _ => unreachable!(),
        };
        out.push_str(name);
        out.push('(');
        out.push_str(&args.join(", "));
        out.push(')');
        i = end + 1;
    }
    Ok(out)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn sampling_bias_preserves_offsets_and_array_indices() {
        let images = HashMap::from([
            ("image".into(), (0, false)),
            ("array_image".into(), (1, true)),
        ]);
        let biases = vec!["u.bias0".into(), "u.bias1".into()];
        let source = "textureSampleLevel(image, s, vec2<f32>(0.5, 0.5), lod, vec2<i32>(1, 0)) + textureSample(array_image, s, uv, layer, offset)";
        let result = rewrite_calls(source, &images, &biases).unwrap();
        assert!(result.contains("(lod + u.bias0), vec2<i32>(1, 0)"));
        assert!(result.contains("textureSampleBias(array_image, s, uv, layer, u.bias1, offset)"));
        assert_eq!(
            rewrite_calls("textureLoad(image, xy, 1)", &images, &biases).unwrap(),
            "textureLoad(image, xy, 1)"
        );
    }
}
