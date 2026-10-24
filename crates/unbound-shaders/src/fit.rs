//! A compiled program's SPIR-V fitted to the renderer's interface (see [`crate::Compiled`]):
//! vertex inputs moved to the renderer's locations by HLSL semantic, resources rebound by name,
//! the vertex color multiplied by the per-draw color, and OpenGL clip space turned into the
//! renderer's (naga negates y; depth is remapped here from [-w, w] to [0, w]).
use crate::{BufferSlot, Compiled, InlineSampler, Scalar, TextureDim, TextureSlot, Uniform};
use anyhow::{Context, Result, anyhow, bail};
use naga::{
    AddressSpace, BinaryOperator, Binding, Expression, Handle, Literal, Module, ResourceBinding,
    Span, Statement, StructMember, Type, TypeInner, VectorSize,
};
use std::collections::{BTreeSet, HashMap};

const MAIN_TEXTURE: &str = "_MainTex";

/// What glslang's SPIR-V says that naga drops: HLSL semantics of the stage's inputs, by location.
pub(crate) struct Scanned {
    pub(crate) words: Vec<u32>,
    input_semantics: HashMap<u32, String>,
}

#[derive(Clone, Copy, PartialEq)]
pub(crate) enum Stage {
    Vertex,
    Fragment,
    Compute,
}

/// Reads the semantics (SPV_GOOGLE_hlsl_functionality1) and strips them for naga. The DX9
/// position semantic (`POSITION` on a vertex output or fragment input, which Unity accepts for
/// `SV_POSITION`) becomes the position built-in, as glslang only does for `SV_POSITION`.
pub(crate) fn scan(words: &[u32], stage: Stage) -> Result<Scanned> {
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
        Stage::Compute => Vec::new(),
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

/// SPIR-V without `RelaxedPrecision` decorations (glslang's for `min16float` and `half`): naga
/// does not know them and warns for each; everything runs at full precision anyway.
pub(crate) fn strip_relaxed_precision(words: &[u32]) -> Vec<u32> {
    if words.len() < 5 {
        return words.to_vec();
    }
    let mut out = words[..5].to_vec();
    let mut i = 5;
    while i < words.len() {
        let count = ((words[i] >> 16) as usize).max(1);
        let end = (i + count).min(words.len());
        let inst = &words[i..end];
        let relaxed = match inst[0] & 0xffff {
            71 => inst.len() == 3 && inst[2] == 0,
            72 => inst.len() == 4 && inst[3] == 0,
            _ => false,
        };
        if !relaxed {
            out.extend_from_slice(inst);
        }
        i = end;
    }
    out
}

/// Integer operations whose operands differ in signedness from the result (or, comparing, from
/// each other) get a bitcast first. SPIR-V allows them (glslang shifts a `uint` into an `int`
/// for byte address buffer loads); naga types the result as the operand and rejects the mix.
pub(crate) fn unify_int_signedness(words: &[u32]) -> Result<Vec<u32>> {
    if words.len() < 5 {
        bail!("empty SPIR-V");
    }
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
    let op = |at: usize| words[at] & 0xffff;
    // Integer types: id → (signed, components, scalar id).
    let mut ints: HashMap<u32, (bool, u32, u32)> = HashMap::new();
    for &(at, _) in &list {
        let inst = &words[at..];
        match op(at) {
            21 => {
                ints.insert(inst[1], (inst[3] == 1, 1, inst[1]));
            }
            23 => {
                if let Some(&(signed, _, scalar)) = ints.get(&inst[2]) {
                    ints.insert(inst[1], (signed, inst[3], scalar));
                }
            }
            _ => {}
        }
    }
    // The integer type of every value: an instruction whose first operand is an integer type
    // has that result type (ids are unique, so no other operand can be one).
    let mut types: HashMap<u32, u32> = HashMap::new();
    for &(at, count) in &list {
        let code = op(at);
        let is_type_or_annotation = (19..=39).contains(&code) || matches!(code, 1..=8 | 71 | 72);
        if count >= 3 && !is_type_or_annotation && ints.contains_key(&words[at + 1]) {
            types.insert(words[at + 2], words[at + 1]);
        }
    }
    let arithmetic = [128, 130, 132, 134, 135, 137, 138, 139, 197, 198, 199];
    let comparison = 170..=179;
    let mut bound = words[3];
    let mut out = words[..5].to_vec();
    let mut changed = false;
    for &(at, count) in &list {
        let code = op(at);
        let mut inst = words[at..at + count].to_vec();
        let shift = (194..=196).contains(&code);
        if count == 5 && (arithmetic.contains(&code) || comparison.contains(&code) || shift) {
            // The type both operands must have: the result's, or (comparing) the first's.
            let want = if arithmetic.contains(&code) || shift {
                inst[1]
            } else {
                types.get(&inst[3]).copied().unwrap_or(0)
            };
            if let Some(&(signed, components, _)) = ints.get(&want) {
                // A shift's amount may be of either signedness; what it shifts is the result's.
                let operands: &[usize] = if shift { &[3] } else { &[3, 4] };
                for &k in operands {
                    let Some(&(s, c, _)) = types.get(&inst[k]).and_then(|t| ints.get(t)) else {
                        continue;
                    };
                    if s != signed && c == components {
                        let id = bound;
                        bound += 1;
                        out.extend_from_slice(&[(4 << 16) | 124, want, id, inst[k]]);
                        inst[k] = id;
                        changed = true;
                    }
                }
            }
        }
        out.extend_from_slice(&inst);
    }
    if !changed {
        return Ok(words.to_vec());
    }
    out[3] = bound;
    Ok(out)
}

/// Arrays whose elements are smaller than their stride (`float _W[4]` in a cbuffer: 16 bytes per
/// float), which WGSL cannot write (its array stride is the element's size), get elements of a
/// struct padded to the stride, `{ float x; float pad...; }`; every access chain through such an
/// array steps into the struct's first member, so the program reads what it did.
pub(crate) fn pad_array_elements(words: &[u32]) -> Result<Vec<u32>> {
    if words.len() < 5 {
        bail!("empty SPIR-V");
    }
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
    let op = |at: usize| words[at] & 0xffff;
    // Scalar and vector types (id → (byte size, component type, component count)).
    let mut sizes: HashMap<u32, (u32, u32, u32)> = HashMap::new();
    let mut strides: HashMap<u32, u32> = HashMap::new();
    let mut int_constants: HashMap<u32, u32> = HashMap::new();
    let mut int32: Option<u32> = None;
    let mut float32: Option<u32> = None;
    for &(at, count) in &list {
        let inst = &words[at..at + count];
        match op(at) {
            21 if inst[2] == 32 => {
                sizes.insert(inst[1], (4, inst[1], 1));
                if inst[3] == 1 || int32.is_none() {
                    int32 = Some(inst[1]);
                }
            }
            22 if inst[2] == 32 => {
                sizes.insert(inst[1], (4, inst[1], 1));
                float32 = Some(inst[1]);
            }
            23 => {
                if let Some(&(size, _, _)) = sizes.get(&inst[2]) {
                    sizes.insert(inst[1], (size * inst[3], inst[2], inst[3]));
                }
            }
            43 if count == 4 => {
                int_constants.insert(inst[2], inst[3]);
            }
            71 if count == 4 && inst[2] == 6 => {
                strides.insert(inst[1], inst[3]);
            }
            _ => {}
        }
    }
    // The arrays to pad: (array id, element id, element size, stride).
    let mut padded: HashMap<u32, (u32, u32, u32)> = HashMap::new();
    for &(at, count) in &list {
        let inst = &words[at..at + count];
        if op(at) == 28
            && let (Some(&stride), Some(&(size, _, _))) =
                (strides.get(&inst[1]), sizes.get(&inst[2]))
            && size < stride
            && stride % 4 == 0
            && stride <= 64
        {
            padded.insert(inst[1], (inst[2], size, stride));
        }
    }
    if padded.is_empty() {
        return Ok(words.to_vec());
    }
    let mut bound = words[3];
    let mut fresh = || {
        bound += 1;
        bound - 1
    };
    let int_type = match int32 {
        Some(t) => t,
        None => bail!("an array needs padding but the program has no 32-bit integer type"),
    };
    let zero = int_constants
        .iter()
        .find(|(_, v)| **v == 0)
        .map(|(id, _)| *id)
        .filter(|id| {
            // The constant must be of that integer type.
            list.iter().any(|&(at, count)| {
                op(at) == 43 && count == 4 && words[at + 2] == *id && words[at + 1] == int_type
            })
        });
    let (zero, zero_inst) = match zero {
        Some(z) => (z, None),
        None => {
            let z = fresh();
            (z, Some([(4 << 16) | 43, int_type, z, 0]))
        }
    };
    let pad_type = float32.unwrap_or(int_type);
    // Wrapper structs: the element, then 4-byte padding members up to the stride.
    let mut wrappers: HashMap<u32, (u32, Vec<u32>)> = HashMap::new();
    for (&array, &(element, size, stride)) in &padded {
        let wrapper = fresh();
        let mut inst = vec![0, wrapper, element];
        inst.extend(std::iter::repeat_n(
            pad_type,
            ((stride - size) / 4) as usize,
        ));
        inst[0] = ((inst.len() as u32) << 16) | 30;
        wrappers.insert(array, (wrapper, inst));
    }
    // Pointer and variable types, to follow access chains.
    let mut pointee: HashMap<u32, u32> = HashMap::new();
    let mut value_type: HashMap<u32, u32> = HashMap::new();
    let mut members: HashMap<u32, Vec<u32>> = HashMap::new();
    let mut elements: HashMap<u32, u32> = HashMap::new();
    for &(at, count) in &list {
        let inst = &words[at..at + count];
        match op(at) {
            32 => {
                pointee.insert(inst[1], inst[3]);
            }
            59 | 65 | 66 => {
                value_type.insert(inst[2], inst[1]);
            }
            30 => {
                members.insert(inst[1], inst[2..].to_vec());
            }
            28 | 29 => {
                elements.insert(inst[1], inst[2]);
            }
            23 => {
                elements.insert(inst[1], inst[2]);
            }
            _ => {}
        }
    }
    let mut out = words[..5].to_vec();
    let mut decorated = false;
    let mut zero_emitted = zero_inst.is_none();
    for &(at, count) in &list {
        let inst = &words[at..at + count];
        match op(at) {
            // Member offsets of the wrappers, with the other decorations.
            71 | 72 if !decorated => {
                decorated = true;
                for (&array, &(wrapper, _)) in &wrappers {
                    let (_, size, stride) = padded[&array];
                    let pads = (stride - size) / 4;
                    for m in 0..=pads {
                        let offset = if m == 0 { 0 } else { size + 4 * (m - 1) };
                        out.extend_from_slice(&[(5 << 16) | 72, wrapper, m, 35, offset]);
                    }
                }
                out.extend_from_slice(inst);
            }
            // The new zero constant right after its type.
            21 if inst[1] == int_type && !zero_emitted => {
                out.extend_from_slice(inst);
                out.extend_from_slice(&zero_inst.unwrap());
                zero_emitted = true;
            }
            28 if wrappers.contains_key(&inst[1]) => {
                let (wrapper, wrapper_inst) = &wrappers[&inst[1]];
                out.extend_from_slice(wrapper_inst);
                out.extend_from_slice(&[inst[0], inst[1], *wrapper, inst[3]]);
            }
            65 | 66 => {
                // Follow the chain from the base's type; after an index into a padded array,
                // step into the wrapper's element.
                let mut ty = value_type
                    .get(&inst[3])
                    .and_then(|p| pointee.get(p))
                    .copied();
                let mut chain = Vec::new();
                let mut changed = false;
                for &index in &inst[4..] {
                    chain.push(index);
                    let Some(t) = ty else { continue };
                    if padded.contains_key(&t) {
                        chain.push(zero);
                        changed = true;
                        ty = Some(padded[&t].0);
                    } else if let Some(m) = members.get(&t) {
                        ty = int_constants
                            .get(&index)
                            .and_then(|i| m.get(*i as usize))
                            .copied();
                    } else {
                        ty = elements.get(&t).copied();
                    }
                }
                if changed {
                    out.push((((4 + chain.len()) as u32) << 16) | op(at));
                    out.extend_from_slice(&inst[1..4]);
                    out.extend_from_slice(&chain);
                } else {
                    out.extend_from_slice(inst);
                }
            }
            _ => out.extend_from_slice(inst),
        }
    }
    out[3] = bound;
    Ok(out)
}

/// A fragment input with the `VFACE` semantic (a float: 1 on front faces, -1 on back ones), which
/// glslang leaves an ordinary input, becomes the front-facing built-in: the variable is a bool
/// `FrontFacing` and every load of it selects 1 or -1.
fn front_facing(words: &[u32]) -> Result<Vec<u32>> {
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
    let op = |at: usize| words[at] & 0xffff;
    let text = |s: &[u32]| {
        let bytes: Vec<u8> = s.iter().flat_map(|w| w.to_le_bytes()).collect();
        let end = bytes.iter().position(|b| *b == 0).unwrap_or(bytes.len());
        String::from_utf8_lossy(&bytes[..end]).to_string()
    };
    let mut vface = Vec::new();
    for &(at, count) in &list {
        let inst = &words[at..at + count];
        if op(at) == 5632 && inst[2] == 5635 && text(&inst[3..]).eq_ignore_ascii_case("VFACE") {
            vface.push(inst[1]);
        }
    }
    // Input variables only, with their pointer and float types.
    let mut pointer_of: HashMap<u32, u32> = HashMap::new();
    let mut pointee: HashMap<u32, u32> = HashMap::new();
    let mut bool_type = None;
    let mut bool_pointer = None;
    for &(at, count) in &list {
        let inst = &words[at..at + count];
        match op(at) {
            20 => bool_type = Some(inst[1]),
            32 => {
                pointee.insert(inst[1], inst[3]);
                if inst[2] == 1 && Some(inst[3]) == bool_type {
                    bool_pointer = Some(inst[1]);
                }
            }
            59 if inst[3] == 1 => {
                pointer_of.insert(inst[2], inst[1]);
            }
            _ => {}
        }
    }
    vface.retain(|v| pointer_of.contains_key(v));
    if vface.is_empty() {
        return Ok(words.to_vec());
    }
    let mut bound = words[3];
    let mut fresh = || {
        bound += 1;
        bound - 1
    };
    let new_bool = bool_type.is_none();
    let bool_type = bool_type.unwrap_or_else(&mut fresh);
    let new_pointer = bool_pointer.is_none();
    let bool_pointer = bool_pointer.unwrap_or_else(&mut fresh);
    // Per float type: the 1 and -1 constants.
    let mut constants: HashMap<u32, (u32, u32)> = HashMap::new();
    for v in &vface {
        let float = pointee[&pointer_of[v]];
        constants.entry(float).or_insert_with(|| (fresh(), fresh()));
    }
    let mut out = words[..5].to_vec();
    let mut defined = false;
    for &(at, count) in &list {
        let inst = &words[at..at + count];
        match op(at) {
            // The variable's Location becomes BuiltIn FrontFacing (17).
            71 if count >= 4 && inst[2] == 30 && vface.contains(&inst[1]) => {
                out.extend_from_slice(&[(4 << 16) | 71, inst[1], 11, 17]);
            }
            59 if vface.contains(&inst[2]) => {
                if !defined {
                    defined = true;
                    if new_bool {
                        out.extend_from_slice(&[(2 << 16) | 20, bool_type]);
                    }
                    if new_pointer {
                        out.extend_from_slice(&[(4 << 16) | 32, bool_pointer, 1, bool_type]);
                    }
                    for (float, (one, minus_one)) in &constants {
                        out.extend_from_slice(&[(4 << 16) | 43, *float, *one, 1.0f32.to_bits()]);
                        out.extend_from_slice(&[
                            (4 << 16) | 43,
                            *float,
                            *minus_one,
                            (-1.0f32).to_bits(),
                        ]);
                    }
                }
                out.extend_from_slice(&[inst[0], bool_pointer, inst[2], inst[3]]);
            }
            // A load: the bool, then 1 or -1.
            61 if vface.contains(&inst[3]) => {
                let float = inst[1];
                let (one, minus_one) = constants[&pointee[&pointer_of[&inst[3]]]];
                let flag = fresh();
                out.extend_from_slice(&[(4 << 16) | 61, bool_type, flag, inst[3]]);
                out.extend_from_slice(&[(6 << 16) | 169, float, inst[2], flag, one, minus_one]);
            }
            _ => out.extend_from_slice(inst),
        }
    }
    out[3] = bound;
    Ok(out)
}

pub(crate) fn parse(words: &[u32]) -> Result<Module> {
    let bytes: Vec<u8> = words.iter().flat_map(|w| w.to_le_bytes()).collect();
    let options = naga::front::spv::Options {
        adjust_coordinate_space: true,
        strict_capabilities: false,
        block_ctx_dump_prefix: None,
    };
    naga::front::spv::parse_u8_slice(&bytes, &options).map_err(|e| anyhow!("reading SPIR-V: {e:?}"))
}

/// A texture variable's dimension.
pub(crate) fn texture_dim(
    name: &str,
    dim: naga::ImageDimension,
    arrayed: bool,
) -> Result<TextureDim> {
    use naga::ImageDimension as D;
    Ok(match (dim, arrayed) {
        (D::D2, false) => TextureDim::D2,
        (D::D2, true) => TextureDim::D2Array,
        (D::Cube, false) => TextureDim::Cube,
        (D::D3, false) => TextureDim::D3,
        _ => bail!("texture `{name}` ({dim:?}, arrayed: {arrayed}) is not supported"),
    })
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
    let vertex = unify_int_signedness(&pad_array_elements(&strip_relaxed_precision(vertex))?)?;
    let fragment = front_facing(&unify_int_signedness(&pad_array_elements(
        &strip_relaxed_precision(fragment),
    )?)?)?;
    let vertex_scan = scan(&vertex, Stage::Vertex)?;
    let fragment_scan = scan(&fragment, Stage::Fragment)?;
    let mut vertex = parse(&vertex_scan.words).context("vertex program")?;
    let mut fragment = parse(&fragment_scan.words).context("fragment program")?;
    pad_fragment_outputs(&mut fragment);

    // Textures of both stages, bound alike.
    let mut names = BTreeSet::new();
    let mut dims: HashMap<String, TextureDim> = HashMap::new();
    let mut slots: HashMap<String, TextureSlot> = HashMap::new();
    for module in [&vertex, &fragment] {
        for (_, g) in module.global_variables.iter() {
            if g.space != AddressSpace::Handle {
                continue;
            }
            let name = g.name.clone().unwrap_or_default();
            let sampler = matches!(module.types[g.ty].inner, TypeInner::Sampler { .. });
            let property = slot_property(&name, sampler).ok_or_else(|| {
                anyhow!("sampler `{name}` is neither a texture's nor an inline sampler state")
            })?;
            let slot = slots.entry(property.clone()).or_default();
            match module.types[g.ty].inner {
                TypeInner::Image {
                    dim,
                    arrayed,
                    class,
                } => {
                    dims.insert(property.clone(), texture_dim(&name, dim, arrayed)?);
                    slot.depth |= matches!(class, naga::ImageClass::Depth { .. });
                }
                TypeInner::Sampler { comparison } => {
                    slot.comparison |= comparison;
                    if property == name {
                        slot.inline = InlineSampler::from_name(&name);
                    } else if !textures_named(module, &property) {
                        slot.inline = InlineSampler::from_name(&property);
                    }
                }
                _ => {}
            }
            names.insert(property);
        }
    }
    let main_texture = dims.get(MAIN_TEXTURE).copied().unwrap_or(TextureDim::D2) == TextureDim::D2
        && names.remove(MAIN_TEXTURE);
    let textures: Vec<String> = names.into_iter().collect();

    // Structured (and byte address) buffers of both stages, bound alike: read-only unless the
    // fragment program writes one (a vertex program cannot).
    let mut buffers: Vec<BufferSlot> = Vec::new();
    for (stage, module) in [("vertex", &vertex), ("fragment", &fragment)] {
        for (_, g) in module.global_variables.iter() {
            let AddressSpace::Storage { access } = g.space else {
                continue;
            };
            let name = block_name(module, g);
            let read_only = !access.contains(naga::StorageAccess::STORE);
            if !read_only && stage == "vertex" {
                bail!("buffer `{name}` is written by the vertex program, which wgpu does not allow");
            }
            match buffers.iter_mut().find(|b| b.name == name) {
                Some(b) => b.read_only &= read_only,
                None => buffers.push(BufferSlot { name, read_only }),
            }
        }
    }
    for b in buffers.iter().filter(|b| !b.read_only) {
        if vertex.global_variables.iter().any(|(_, g)| {
            matches!(g.space, AddressSpace::Storage { .. }) && block_name(&vertex, g) == b.name
        }) {
            bail!(
                "buffer `{}` is written by the fragment program and read by the vertex program, which wgpu does not allow",
                b.name
            );
        }
    }
    let texture_dims: Vec<TextureDim> = textures
        .iter()
        .map(|t| dims.get(t).copied().unwrap_or(TextureDim::D2))
        .collect();
    let texture_slots: Vec<TextureSlot> = textures
        .iter()
        .map(|t| slots.get(t).copied().unwrap_or_default())
        .collect();

    // Every cbuffer of the program in one uniform block, laid out alike in both stages.
    let layout = block_layout(&[&vertex, &fragment]);
    merge_blocks(&mut vertex, &layout)?;
    merge_blocks(&mut fragment, &layout)?;

    let vertex_uniforms = rebind(&mut vertex, &textures, &buffers)?;
    let fragment_uniforms = rebind(&mut fragment, &textures, &buffers)?;
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
        .unwrap_or(0)
        .max(if layout.bases.len() > 1 { layout.size } else { 0 });

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
        texture_dims,
        main_texture,
        keywords: Vec::new(),
        texture_slots,
        buffers,
        blocks: layout
            .bases
            .iter()
            .zip(&layout.spans)
            .map(|((name, offset), size)| crate::BlockRange {
                name: name.clone(),
                offset: *offset,
                size: *size,
            })
            .collect(),
    })
}

/// Whether a module has a texture (image) variable for a property.
fn textures_named(module: &Module, property: &str) -> bool {
    module.global_variables.iter().any(|(_, g)| {
        matches!(module.types[g.ty].inner, TypeInner::Image { .. })
            && g.name
                .as_deref()
                .and_then(|n| texture_property(n, false))
                .as_deref()
                == Some(property)
    })
}

/// A texture or sampler variable's slot: its texture property, or (a sampler named like no
/// texture's, such as `my_point_clamp_sampler`) its own name when it spells an inline state.
fn slot_property(name: &str, sampler: bool) -> Option<String> {
    match texture_property(name, sampler) {
        Some(p) => Some(p),
        None if sampler && InlineSampler::from_name(name).is_some() => Some(name.to_string()),
        None => None,
    }
}

/// The program's uniform blocks (cbuffers) by name: each one's offset in the merged block, in
/// the order the vertex then the fragment program declare them (each 16-byte aligned), and the
/// merged block's size.
struct BlockLayout {
    bases: Vec<(String, u32)>,
    /// Each block's size, by `bases` index.
    spans: Vec<u32>,
    size: u32,
}

fn block_name(module: &Module, g: &naga::GlobalVariable) -> String {
    // glslang's cbuffer variables are anonymous; their struct type has the cbuffer's name.
    g.name
        .clone()
        .filter(|n| !n.is_empty())
        .or_else(|| module.types[g.ty].name.clone())
        .unwrap_or_default()
}

fn block_layout(modules: &[&Module]) -> BlockLayout {
    let mut bases: Vec<(String, u32)> = Vec::new();
    let mut spans = Vec::new();
    let mut size = 0u32;
    for module in modules {
        for (_, g) in module.global_variables.iter() {
            if g.space != AddressSpace::Uniform {
                continue;
            }
            let TypeInner::Struct { span, .. } = module.types[g.ty].inner else {
                continue;
            };
            let name = block_name(module, g);
            if bases.iter().any(|(n, _)| *n == name) {
                continue;
            }
            let base = size.div_ceil(16) * 16;
            bases.push((name, base));
            spans.push(span);
            size = base + span;
        }
    }
    BlockLayout {
        bases,
        spans,
        size: size.div_ceil(16) * 16,
    }
}

/// Puts a module's uniform blocks into one at the layout's offsets: the first block's variable
/// becomes the merged one, member accesses of the others are redirected to it, and unused
/// variables are dropped.
fn merge_blocks(module: &mut Module, layout: &BlockLayout) -> Result<()> {
    let blocks: Vec<Handle<naga::GlobalVariable>> = module
        .global_variables
        .iter()
        .filter(|(_, g)| {
            g.space == AddressSpace::Uniform
                && matches!(module.types[g.ty].inner, TypeInner::Struct { .. })
        })
        .map(|(h, _)| h)
        .collect();
    // One block in the whole program stays as it is; else every block moves to its offset (also
    // a stage's only block, so both stages agree).
    if blocks.is_empty() || layout.bases.len() < 2 {
        return Ok(());
    }
    let mut members = Vec::new();
    let mut first_member: HashMap<Handle<naga::GlobalVariable>, u32> = HashMap::new();
    let mut order: Vec<(u32, Handle<naga::GlobalVariable>)> = blocks
        .iter()
        .map(|h| {
            let name = block_name(module, &module.global_variables[*h]);
            let base = layout
                .bases
                .iter()
                .find(|(n, _)| *n == name)
                .map_or(0, |(_, b)| *b);
            (base, *h)
        })
        .collect();
    order.sort_by_key(|(base, _)| *base);
    // A gap before a block (one the other stage has) is a padding member: WGSL cannot place a
    // member after an unexplained gap.
    let vec4 = module.types.insert(
        Type {
            name: None,
            inner: TypeInner::Vector {
                size: VectorSize::Quad,
                scalar: naga::Scalar::F32,
            },
        },
        Span::UNDEFINED,
    );
    let mut end = 0u32;
    for (base, h) in &order {
        let TypeInner::Struct {
            members: block_members,
            span,
        } = module.types[module.global_variables[*h].ty].inner.clone()
        else {
            continue;
        };
        let pad_start = end.div_ceil(16) * 16;
        if *base > pad_start {
            let count = (base - pad_start) / 16;
            let array = module.types.insert(
                Type {
                    name: None,
                    inner: TypeInner::Array {
                        base: vec4,
                        size: naga::ArraySize::Constant(std::num::NonZeroU32::new(count).unwrap()),
                        stride: 16,
                    },
                },
                Span::UNDEFINED,
            );
            members.push(StructMember {
                name: Some(format!("unbound_pad{}", members.len())),
                ty: array,
                binding: None,
                offset: pad_start,
            });
        }
        end = base + span;
        first_member.insert(*h, members.len() as u32);
        for m in block_members {
            members.push(StructMember {
                name: m.name.clone(),
                ty: m.ty,
                binding: None,
                offset: base + m.offset,
            });
        }
    }
    let merged_ty = module.types.insert(
        Type {
            name: Some("UnboundUniforms".into()),
            inner: TypeInner::Struct {
                members,
                span: layout.size,
            },
        },
        Span::UNDEFINED,
    );
    let merged = order[0].1;
    module.global_variables[merged].ty = merged_ty;
    module.global_variables[merged].name = Some("UnboundUniforms".into());
    let redirect = |exprs: &mut naga::Arena<Expression>| -> Result<()> {
        // Member accesses: their index moves by the block's first member.
        let handles: Vec<Handle<Expression>> = exprs.iter().map(|(h, _)| h).collect();
        let mut bumps = Vec::new();
        for &e in &handles {
            if let Expression::AccessIndex { base, index } = exprs[e] {
                if let Expression::GlobalVariable(g) = exprs[base] {
                    if let Some(first) = first_member.get(&g) {
                        bumps.push((e, index + first));
                    }
                }
            }
        }
        for (e, index) in bumps {
            if let Expression::AccessIndex { index: i, .. } = &mut exprs[e] {
                *i = index;
            }
        }
        for &e in &handles {
            if let Expression::GlobalVariable(g) = &mut exprs[e] {
                if first_member.contains_key(g) {
                    *g = merged;
                }
            }
        }
        Ok(())
    };
    for (_, f) in module.functions.iter_mut() {
        redirect(&mut f.expressions)?;
    }
    for ep in module.entry_points.iter_mut() {
        redirect(&mut ep.function.expressions)?;
    }
    naga::compact::compact(module, naga::compact::KeepUnused::No);
    Ok(())
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
fn rebind(module: &mut Module, textures: &[String], buffers: &[BufferSlot]) -> Result<Vec<Uniform>> {
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
                    TypeInner::Sampler { .. } => true,
                    TypeInner::Image {
                        class:
                            naga::ImageClass::Sampled { multi: false, .. }
                            | naga::ImageClass::Depth { multi: false },
                        ..
                    } => false,
                    other => {
                        bail!(
                            "texture `{name}` ({other:?}) is not supported: only sampled textures are"
                        )
                    }
                };
                let property = slot_property(&name, sampler).unwrap_or_default();
                let binding = if property == MAIN_TEXTURE && !textures.contains(&property) {
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
            AddressSpace::Storage { .. } => {
                let name = block_name(module, g);
                let k = buffers
                    .iter()
                    .position(|b| b.name == name)
                    .ok_or_else(|| anyhow!("buffer `{name}`"))?;
                // A buffer both stages share is read-only in each when either only reads it.
                if buffers[k].read_only
                    && let AddressSpace::Storage { access } = &mut module.global_variables[h].space
                {
                    *access = naga::StorageAccess::LOAD;
                }
                module.global_variables[h].binding = Some(ResourceBinding {
                    group: 3,
                    binding: k as u32,
                });
            }
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

pub(crate) fn uniform(
    module: &Module,
    name: &str,
    ty: Handle<Type>,
    offset: u32,
) -> Option<Uniform> {
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
            // A padded element: its first member (see `pad_array_elements`).
            let element = match &module.types[*element].inner {
                TypeInner::Struct { members, .. } => members.first()?.ty,
                _ => *element,
            };
            let (scalar, rows, columns) = base(&module.types[element].inner)?;
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

/// Fragment outputs of fewer than four components (Unity writes them into any target, the
/// missing channels undefined; wgpu wants the target's): the entry point becomes a function the
/// new entry point calls, padding each such output to a vec4 with zeros.
fn pad_fragment_outputs(module: &mut Module) {
    let short = |module: &Module, ty: Handle<Type>| -> Option<(naga::Scalar, u32)> {
        match module.types[ty].inner {
            TypeInner::Scalar(s) => Some((s, 1)),
            TypeInner::Vector { size, scalar } if (size as u32) < 4 => Some((scalar, size as u32)),
            _ => None,
        }
    };
    for e in 0..module.entry_points.len() {
        if module.entry_points[e].stage != naga::ShaderStage::Fragment {
            continue;
        }
        let Some(result) = module.entry_points[e].function.result.clone() else {
            continue;
        };
        let members: Option<Vec<StructMember>> = match &module.types[result.ty].inner {
            TypeInner::Struct { members, .. } => Some(members.clone()),
            _ => None,
        };
        let location = |b: &Option<Binding>| matches!(b, Some(Binding::Location { .. }));
        let needs = match &members {
            Some(ms) => ms.iter().any(|m| location(&m.binding) && short(module, m.ty).is_some()),
            None => location(&result.binding) && short(module, result.ty).is_some(),
        };
        if !needs {
            continue;
        }
        let vec4 = |module: &mut Module, scalar: naga::Scalar| {
            module.types.insert(
                Type {
                    name: None,
                    inner: TypeInner::Vector {
                        size: VectorSize::Quad,
                        scalar,
                    },
                },
                Span::UNDEFINED,
            )
        };
        // The original body as an ordinary function.
        let mut inner = module.entry_points[e].function.clone();
        for a in inner.arguments.iter_mut() {
            a.binding = None;
        }
        if let Some(r) = inner.result.as_mut() {
            r.binding = None;
        }
        inner.name = Some(format!("{}_unpadded", module.entry_points[e].name));
        let arguments = module.entry_points[e].function.arguments.clone();
        let inner = module.functions.append(inner, Span::UNDEFINED);
        // The padded result type.
        let (new_ty, plan): (Handle<Type>, Vec<Option<(naga::Scalar, u32)>>) = match &members {
            Some(ms) => {
                let mut padded = Vec::new();
                let mut plan = Vec::new();
                for (i, m) in ms.iter().enumerate() {
                    let pad = if location(&m.binding) { short(module, m.ty) } else { None };
                    let ty = match pad {
                        Some((scalar, _)) => vec4(module, scalar),
                        None => m.ty,
                    };
                    padded.push(StructMember {
                        name: m.name.clone(),
                        ty,
                        binding: m.binding.clone(),
                        offset: 16 * i as u32,
                    });
                    plan.push(pad);
                }
                let span = 16 * padded.len() as u32;
                let ty = module.types.insert(
                    Type {
                        name: Some("UnboundFragmentOutput".into()),
                        inner: TypeInner::Struct {
                            members: padded,
                            span,
                        },
                    },
                    Span::UNDEFINED,
                );
                (ty, plan)
            }
            None => {
                let pad = short(module, result.ty);
                let (scalar, _) = pad.unwrap();
                (vec4(module, scalar), vec![pad])
            }
        };
        let mut f = naga::Function {
            name: Some(module.entry_points[e].name.clone()),
            arguments,
            result: Some(naga::FunctionResult {
                ty: new_ty,
                binding: if members.is_some() { None } else { result.binding.clone() },
            }),
            ..Default::default()
        };
        let args: Vec<Handle<Expression>> = (0..f.arguments.len() as u32)
            .map(|i| f.expressions.append(Expression::FunctionArgument(i), Span::UNDEFINED))
            .collect();
        let zero = |f: &mut naga::Function, scalar: naga::Scalar| {
            let literal = match scalar.kind {
                naga::ScalarKind::Sint => Literal::I32(0),
                naga::ScalarKind::Uint => Literal::U32(0),
                _ => Literal::F32(0.0),
            };
            f.expressions.append(Expression::Literal(literal), Span::UNDEFINED)
        };
        let zeros: Vec<Option<Handle<Expression>>> = plan
            .iter()
            .map(|p| p.map(|(scalar, _)| zero(&mut f, scalar)))
            .collect();
        let call = f.expressions.append(Expression::CallResult(inner), Span::UNDEFINED);
        f.body.push(
            Statement::Call {
                function: inner,
                arguments: args,
                result: Some(call),
            },
            Span::UNDEFINED,
        );
        let start = f.expressions.len();
        let mut parts = Vec::new();
        for (i, p) in plan.iter().enumerate() {
            let value = if members.is_some() {
                f.expressions.append(
                    Expression::AccessIndex {
                        base: call,
                        index: i as u32,
                    },
                    Span::UNDEFINED,
                )
            } else {
                call
            };
            let value = match (p, zeros[i]) {
                (Some((scalar, n)), Some(z)) => {
                    let mut components = vec![value];
                    components.extend(std::iter::repeat_n(z, (4 - n) as usize));
                    let ty = vec4(module, *scalar);
                    f.expressions.append(Expression::Compose { ty, components }, Span::UNDEFINED)
                }
                _ => value,
            };
            parts.push(value);
        }
        let value = if members.is_some() {
            f.expressions.append(
                Expression::Compose {
                    ty: new_ty,
                    components: parts,
                },
                Span::UNDEFINED,
            )
        } else {
            parts[0]
        };
        let end = f.expressions.len();
        if end > start {
            f.body.push(
                Statement::Emit(f.expressions.range_from(start)),
                Span::UNDEFINED,
            );
        }
        f.body.push(Statement::Return { value: Some(value) }, Span::UNDEFINED);
        module.entry_points[e].function = f;
    }
}
