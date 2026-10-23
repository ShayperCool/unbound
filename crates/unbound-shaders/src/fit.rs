//! A compiled program's SPIR-V fitted to the renderer's interface (see [`crate::Compiled`]):
//! vertex inputs moved to the renderer's locations by HLSL semantic, resources rebound by name,
//! the vertex color multiplied by the per-draw color, and OpenGL clip space turned into the
//! renderer's (naga negates y; depth is remapped here from [-w, w] to [0, w]).
use crate::{Compiled, Scalar, Uniform};
use anyhow::{Context, Result, anyhow, bail};
use naga::{
    AddressSpace, BinaryOperator, Binding, Expression, Handle, Literal, Module, ResourceBinding,
    Span, Statement, StructMember, Type, TypeInner, VectorSize,
};
use std::collections::{BTreeSet, HashMap};

const MAIN_TEXTURE: &str = "_MainTex";

/// What glslang's SPIR-V says that naga drops: HLSL semantics of the stage's inputs, by location.
struct Scanned {
    words: Vec<u32>,
    input_semantics: HashMap<u32, String>,
}

#[derive(Clone, Copy, PartialEq)]
enum Stage {
    Vertex,
    Fragment,
}

/// Reads the semantics (SPV_GOOGLE_hlsl_functionality1) and strips them for naga. The DX9
/// position semantic (`POSITION` on a vertex output or fragment input, which Unity accepts for
/// `SV_POSITION`) becomes the position built-in, as glslang only does for `SV_POSITION`.
fn scan(words: &[u32], stage: Stage) -> Result<Scanned> {
    if words.len() < 5 {
        bail!("empty SPIR-V");
    }
    let text = |s: &[u32]| {
        let bytes: Vec<u8> = s.iter().flat_map(|w| w.to_le_bytes()).collect();
        let end = bytes.iter().position(|b| *b == 0).unwrap_or(bytes.len());
        String::from_utf8_lossy(&bytes[..end]).to_string()
    };
    let instructions = |words: &[u32]| -> Result<Vec<(usize, usize)>> {
        let mut list = Vec::new();
        let mut i = 5;
        while i < words.len() {
            let count = (words[i] >> 16) as usize;
            if count == 0 || i + count > words.len() {
                bail!("malformed SPIR-V");
            }
            list.push((i, count));
            i += count;
        }
        Ok(list)
    };
    let list = instructions(words)?;
    // First the facts: semantics, locations, interface variables.
    let mut semantics: HashMap<u32, String> = HashMap::new();
    let mut locations: HashMap<u32, u32> = HashMap::new();
    let mut inputs: Vec<u32> = Vec::new();
    let mut outputs: Vec<u32> = Vec::new();
    for &(at, count) in &list {
        let inst = &words[at..at + count];
        match inst[0] & 0xffff {
            5632 if inst[2] == 5635 => {
                semantics.insert(inst[1], text(&inst[3..]).to_ascii_uppercase());
            }
            71 if inst.len() >= 4 && inst[2] == 30 => {
                locations.insert(inst[1], inst[3]);
            }
            59 if inst.len() >= 4 && inst[3] == 1 => inputs.push(inst[2]),
            59 if inst.len() >= 4 && inst[3] == 3 => outputs.push(inst[2]),
            _ => {}
        }
    }
    let dx9_position = |id: &u32| {
        semantics
            .get(id)
            .is_some_and(|s| s == "POSITION" || s == "POSITION0")
    };
    let positions: Vec<u32> = match stage {
        Stage::Vertex => outputs
            .iter()
            .filter(|id| dx9_position(id))
            .copied()
            .collect(),
        Stage::Fragment => inputs
            .iter()
            .filter(|id| dx9_position(id))
            .copied()
            .collect(),
    };
    // BuiltIn Position (0) on a vertex output, FragCoord (15) on a fragment input.
    let builtin = if stage == Stage::Vertex { 0 } else { 15 };
    let mut kept = words[..5].to_vec();
    for &(at, count) in &list {
        let inst = &words[at..at + count];
        match inst[0] & 0xffff {
            // OpExtension: the Google HLSL extensions naga does not know.
            10 if text(&inst[1..]).starts_with("SPV_GOOGLE") => {}
            // OpDecorateString / OpMemberDecorateString (UserSemantic, UserTypeGOOGLE).
            5632 | 5633 => {}
            // OpDecorate Location of a DX9 position: OpDecorate BuiltIn instead.
            71 if inst.len() >= 4 && inst[2] == 30 && positions.contains(&inst[1]) => {
                kept.extend_from_slice(&[(4 << 16) | 71, inst[1], 11, builtin]);
            }
            _ => kept.extend_from_slice(inst),
        }
    }
    let input_semantics = inputs
        .iter()
        .filter(|id| !positions.contains(id))
        .filter_map(|id| Some((*locations.get(id)?, semantics.get(id)?.clone())))
        .collect();
    Ok(Scanned {
        words: kept,
        input_semantics,
    })
}

fn parse(words: &[u32]) -> Result<Module> {
    let bytes: Vec<u8> = words.iter().flat_map(|w| w.to_le_bytes()).collect();
    let options = naga::front::spv::Options {
        adjust_coordinate_space: true,
        strict_capabilities: false,
        block_ctx_dump_prefix: None,
    };
    naga::front::spv::parse_u8_slice(&bytes, &options).map_err(|e| anyhow!("reading SPIR-V: {e:?}"))
}

/// The renderer's vertex location for an HLSL input semantic.
fn vertex_slot(semantic: &str) -> Option<u32> {
    let s = semantic.trim_end_matches('0');
    Some(match s {
        "POSITION" | "SV_POSITION" => 0,
        "NORMAL" => 1,
        "TANGENT" => 2,
        "COLOR" => 3,
        "TEXCOORD" => 4,
        "TEXCOORD1" => 5,
        "TEXCOORD2" => 6,
        "TEXCOORD3" => 7,
        _ => return None,
    })
}

/// A texture or sampler variable's texture property: `_MainTex.t` / `_MainTex.s` (a
/// `sampler2D`), `_MainTex` / `sampler_MainTex` (separate objects).
fn texture_property(name: &str, sampler: bool) -> Option<String> {
    if let Some(p) = name.strip_suffix(".t").or_else(|| name.strip_suffix(".s")) {
        return Some(p.to_string());
    }
    if sampler {
        return name
            .strip_prefix("sampler")
            .filter(|p| !p.is_empty())
            .map(str::to_string);
    }
    Some(name.to_string())
}

pub fn fit(vertex: &[u32], fragment: &[u32]) -> Result<Compiled> {
    let vertex_scan = scan(vertex, Stage::Vertex)?;
    let fragment_scan = scan(fragment, Stage::Fragment)?;
    let mut vertex = parse(&vertex_scan.words).context("vertex program")?;
    let mut fragment = parse(&fragment_scan.words).context("fragment program")?;

    // Textures of both stages, bound alike.
    let mut names = BTreeSet::new();
    for module in [&vertex, &fragment] {
        for (_, g) in module.global_variables.iter() {
            if g.space != AddressSpace::Handle {
                continue;
            }
            let name = g.name.clone().unwrap_or_default();
            let sampler = matches!(module.types[g.ty].inner, TypeInner::Sampler { .. });
            let property = texture_property(&name, sampler).ok_or_else(|| {
                anyhow!(
                    "sampler `{name}` is not a texture's (inline sampler states are not supported)"
                )
            })?;
            names.insert(property);
        }
    }
    let main_texture = names.remove(MAIN_TEXTURE);
    let textures: Vec<String> = names.into_iter().collect();

    let vertex_uniforms = rebind(&mut vertex, &textures)?;
    let fragment_uniforms = rebind(&mut fragment, &textures)?;
    fit_vertex(&mut vertex, &vertex_scan.input_semantics)?;

    let mut uniforms = vertex_uniforms.clone();
    for u in &fragment_uniforms {
        match uniforms.iter().find(|v| v.name == u.name) {
            Some(v) if v.offset != u.offset => {
                bail!(
                    "uniform `{}` is at offset {} in the vertex program and {} in the fragment program",
                    u.name,
                    v.offset,
                    u.offset
                )
            }
            Some(_) => {}
            None => uniforms.push(u.clone()),
        }
    }
    let uniform_size = [&vertex, &fragment]
        .iter()
        .filter_map(|m| uniform_block(m).map(|(_, span)| span))
        .max()
        .unwrap_or(0);

    let vertex_wgsl = to_wgsl(&vertex).context("vertex program")?;
    let fragment_wgsl = to_wgsl(&fragment).context("fragment program")?;
    Ok(Compiled {
        vertex: vertex_wgsl,
        fragment: fragment_wgsl,
        vertex_entry: String::new(),
        fragment_entry: String::new(),
        uniforms,
        uniform_size,
        textures,
        main_texture,
        keywords: Vec::new(),
    })
}

/// The uniform block's type handle and size.
fn uniform_block(module: &Module) -> Option<(Handle<Type>, u32)> {
    module
        .global_variables
        .iter()
        .find(|(_, g)| {
            g.space == AddressSpace::Uniform
                && g.binding
                    == Some(ResourceBinding {
                        group: 0,
                        binding: 0,
                    })
        })
        .and_then(|(_, g)| match &module.types[g.ty].inner {
            TypeInner::Struct { span, .. } => Some((g.ty, *span)),
            _ => None,
        })
}

/// Binds the uniform block and textures to the renderer's slots; returns the block's members.
fn rebind(module: &mut Module, textures: &[String]) -> Result<Vec<Uniform>> {
    let mut block = None;
    let handles: Vec<_> = module.global_variables.iter().map(|(h, _)| h).collect();
    for h in handles {
        let g = &module.global_variables[h];
        match g.space {
            AddressSpace::Uniform => {
                if block.is_some() {
                    bail!("more than one uniform block (cbuffer) is not supported");
                }
                block = Some(h);
                module.global_variables[h].binding = Some(ResourceBinding {
                    group: 0,
                    binding: 0,
                });
            }
            AddressSpace::Handle => {
                let name = g.name.clone().unwrap_or_default();
                let sampler = match &module.types[g.ty].inner {
                    TypeInner::Sampler { comparison: false } => true,
                    TypeInner::Sampler { comparison: true } => {
                        bail!("comparison sampler `{name}` is not supported")
                    }
                    TypeInner::Image {
                        dim: naga::ImageDimension::D2,
                        arrayed: false,
                        class: naga::ImageClass::Sampled { multi: false, .. },
                    } => false,
                    other => {
                        bail!("texture `{name}` ({other:?}) is not supported: only 2D textures are")
                    }
                };
                let property = texture_property(&name, sampler).unwrap_or_default();
                let binding = if property == MAIN_TEXTURE {
                    ResourceBinding {
                        group: 2,
                        binding: sampler as u32,
                    }
                } else {
                    let k = textures
                        .iter()
                        .position(|t| *t == property)
                        .ok_or_else(|| anyhow!("texture `{property}`"))?
                        as u32;
                    ResourceBinding {
                        group: 1,
                        binding: 2 * k + sampler as u32,
                    }
                };
                module.global_variables[h].binding = Some(binding);
            }
            AddressSpace::Storage { .. } => bail!("storage buffers are not supported"),
            _ => {}
        }
    }
    let Some(block) = block else {
        return Ok(Vec::new());
    };
    let ty = module.global_variables[block].ty;
    let TypeInner::Struct { members, .. } = &module.types[ty].inner else {
        bail!("the uniform block is not a struct");
    };
    let mut uniforms = Vec::new();
    for m in members {
        let Some(name) = m.name.clone() else { continue };
        if let Some(u) = uniform(module, &name, m.ty, m.offset) {
            uniforms.push(u);
        }
    }
    Ok(uniforms)
}

fn uniform(module: &Module, name: &str, ty: Handle<Type>, offset: u32) -> Option<Uniform> {
    let scalar = |s: naga::Scalar| match s.kind {
        naga::ScalarKind::Float => Some(Scalar::Float),
        naga::ScalarKind::Sint => Some(Scalar::Int),
        naga::ScalarKind::Uint | naga::ScalarKind::Bool => Some(Scalar::Uint),
        _ => None,
    };
    let base = |inner: &TypeInner| -> Option<(Scalar, u32, u32)> {
        match *inner {
            TypeInner::Scalar(s) => Some((scalar(s)?, 1, 1)),
            TypeInner::Vector { size, scalar: s } => Some((scalar(s)?, size as u32, 1)),
            TypeInner::Matrix {
                columns,
                rows,
                scalar: s,
            } => Some((scalar(s)?, rows as u32, columns as u32)),
            _ => None,
        }
    };
    let name = name.to_string();
    match &module.types[ty].inner {
        TypeInner::Array {
            base: element,
            size: naga::ArraySize::Constant(count),
            stride,
        } => {
            let (scalar, rows, columns) = base(&module.types[*element].inner)?;
            Some(Uniform {
                name,
                offset,
                scalar,
                rows,
                columns,
                count: count.get(),
                stride: *stride,
            })
        }
        inner => {
            let (scalar, rows, columns) = base(inner)?;
            Some(Uniform {
                name,
                offset,
                scalar,
                rows,
                columns,
                count: 1,
                stride: 0,
            })
        }
    }
}

/// Vertex inputs at the renderer's locations, the per-draw color applied, depth remapped.
fn fit_vertex(module: &mut Module, semantics: &HashMap<u32, String>) -> Result<()> {
    let span = Span::UNDEFINED;
    let vec4 = module.types.insert(
        Type {
            name: None,
            inner: TypeInner::Vector {
                size: VectorSize::Quad,
                scalar: naga::Scalar::F32,
            },
        },
        span,
    );
    let mat4 = module.types.insert(
        Type {
            name: None,
            inner: TypeInner::Matrix {
                columns: VectorSize::Quad,
                rows: VectorSize::Quad,
                scalar: naga::Scalar::F32,
            },
        },
        span,
    );
    // The renderer's per-draw uniform, up to its color (see `Draw` in unity.wgsl).
    let member = |name: &str, ty, offset| StructMember {
        name: Some(name.into()),
        ty,
        binding: None,
        offset,
    };
    let draw_ty = module.types.insert(
        Type {
            name: Some("UnboundDraw".into()),
            inner: TypeInner::Struct {
                members: vec![
                    member("model", mat4, 0),
                    member("view", mat4, 64),
                    member("proj", mat4, 128),
                    member("color", vec4, 192),
                ],
                span: 208,
            },
        },
        span,
    );
    let draw = module.global_variables.append(
        naga::GlobalVariable {
            name: Some("unbound_draw".into()),
            space: AddressSpace::Uniform,
            binding: Some(ResourceBinding {
                group: 0,
                binding: 1,
            }),
            ty: draw_ty,
            init: None,
            memory_decorations: Default::default(),
        },
        span,
    );

    let [entry] = module.entry_points.as_mut_slice() else {
        bail!(
            "the vertex program has {} entry points",
            module.entry_points.len()
        );
    };
    let function = &mut entry.function;
    let mut color_argument = None;
    let mut used = HashMap::new();
    for (index, argument) in function.arguments.iter_mut().enumerate() {
        let Some(Binding::Location { location, .. }) = &mut argument.binding else {
            continue;
        };
        let semantic = semantics
            .get(location)
            .ok_or_else(|| anyhow!("vertex input at location {location} has no semantic"))?;
        let slot = vertex_slot(semantic)
            .ok_or_else(|| anyhow!("vertex input semantic {semantic} is not supported"))?;
        if let Some(other) = used.insert(slot, semantic.clone()) {
            bail!("vertex inputs {other} and {semantic} read the same data");
        }
        *location = slot;
        if slot == 3 {
            color_argument = Some((index as u32, argument.ty));
        }
    }

    // The color input times the draw's color, where the entry point stores it for the program.
    if let Some((index, ty)) = color_argument {
        let size = match module.types[ty].inner {
            TypeInner::Vector { size, .. } => size,
            _ => bail!("the COLOR input is not a vector"),
        };
        let argument = function
            .expressions
            .iter()
            .find(|(_, e)| matches!(e, Expression::FunctionArgument(i) if *i == index))
            .map(|(h, _)| h);
        if let Some(argument) = argument {
            let position = function
                .body
                .iter()
                .position(|s| matches!(s, Statement::Store { value, .. } if *value == argument));
            if let Some(position) = position {
                let e = &mut function.expressions;
                let global = e.append(Expression::GlobalVariable(draw), span);
                let access = e.append(
                    Expression::AccessIndex {
                        base: global,
                        index: 3,
                    },
                    span,
                );
                let load = e.append(Expression::Load { pointer: access }, span);
                let color = if size == VectorSize::Quad {
                    load
                } else {
                    use naga::SwizzleComponent::*;
                    e.append(
                        Expression::Swizzle {
                            size,
                            vector: load,
                            pattern: [X, Y, Z, W],
                        },
                        span,
                    )
                };
                let product = e.append(
                    Expression::Binary {
                        op: BinaryOperator::Multiply,
                        left: argument,
                        right: color,
                    },
                    span,
                );
                let emit = Statement::Emit(naga::Range::new_from_bounds(access, product));
                let Statement::Store { pointer, .. } = function.body[position].clone() else {
                    unreachable!()
                };
                function.body.splice(
                    position..position + 1,
                    naga::Block::from_vec(vec![
                        emit,
                        Statement::Store {
                            pointer,
                            value: product,
                        },
                    ]),
                );
            }
        }
    }

    // Depth from OpenGL's [-w, w] to [0, w], right after naga's y flip (`p.y = -p.y`).
    let flip = function.body.iter().position(|s| {
        let Statement::Store { value, .. } = s else {
            return false;
        };
        matches!(
            function.expressions[*value],
            Expression::Unary {
                op: naga::UnaryOperator::Negate,
                ..
            }
        )
    });
    let Some(flip) = flip else {
        bail!("the vertex program writes no SV_POSITION");
    };
    let Statement::Store { pointer, .. } = function.body[flip].clone() else {
        unreachable!()
    };
    let Expression::AccessIndex {
        base: position,
        index: 1,
    } = function.expressions[pointer]
    else {
        bail!("unexpected SV_POSITION store");
    };
    let e = &mut function.expressions;
    let half = e.append(Expression::Literal(Literal::F32(0.5)), span);
    let z = e.append(
        Expression::AccessIndex {
            base: position,
            index: 2,
        },
        span,
    );
    let w = e.append(
        Expression::AccessIndex {
            base: position,
            index: 3,
        },
        span,
    );
    let z_value = e.append(Expression::Load { pointer: z }, span);
    let w_value = e.append(Expression::Load { pointer: w }, span);
    let z_half = e.append(
        Expression::Binary {
            op: BinaryOperator::Multiply,
            left: z_value,
            right: half,
        },
        span,
    );
    let w_half = e.append(
        Expression::Binary {
            op: BinaryOperator::Multiply,
            left: w_value,
            right: half,
        },
        span,
    );
    let sum = e.append(
        Expression::Binary {
            op: BinaryOperator::Add,
            left: z_half,
            right: w_half,
        },
        span,
    );
    function.body.splice(
        flip + 1..flip + 1,
        naga::Block::from_vec(vec![
            Statement::Emit(naga::Range::new_from_bounds(z, sum)),
            Statement::Store {
                pointer: z,
                value: sum,
            },
        ]),
    );
    Ok(())
}

/// WGSL for a fitted module, checked by reading it back: validation, and the uniform block at
/// the offsets the uniforms are filled at.
fn to_wgsl(module: &Module) -> Result<String> {
    let info = naga::valid::Validator::new(
        naga::valid::ValidationFlags::all(),
        naga::valid::Capabilities::default(),
    )
    .validate(module)
    .map_err(|e| anyhow!("invalid after fitting: {}", e.emit_to_string("")))?;
    let wgsl =
        naga::back::wgsl::write_string(module, &info, naga::back::wgsl::WriterFlags::empty())
            .map_err(|e| anyhow!("writing WGSL: {e}"))?;
    let back = naga::front::wgsl::parse_str(&wgsl)
        .map_err(|e| anyhow!("reading the WGSL back: {}", e.emit_to_string(&wgsl)))?;
    naga::valid::Validator::new(
        naga::valid::ValidationFlags::all(),
        naga::valid::Capabilities::default(),
    )
    .validate(&back)
    .map_err(|e| anyhow!("the WGSL does not validate: {}", e.emit_to_string(&wgsl)))?;
    // Members keep their order (the WGSL writer may rename them: `x0` becomes `x0_`).
    if let (Some((ours, _)), Some((theirs, _))) = (uniform_block(module), uniform_block(&back)) {
        let (
            TypeInner::Struct { members: ours, .. },
            TypeInner::Struct {
                members: theirs, ..
            },
        ) = (&module.types[ours].inner, &back.types[theirs].inner)
        else {
            unreachable!()
        };
        for (a, b) in ours.iter().zip(theirs) {
            if a.offset != b.offset {
                bail!(
                    "WGSL cannot keep uniform `{}` at offset {} (it puts it at {})",
                    a.name.as_deref().unwrap_or("?"),
                    a.offset,
                    b.offset
                );
            }
        }
    }
    Ok(wgsl)
}
