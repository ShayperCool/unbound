//! The sampler a graphics program samples a texture through, by device.
//!
//! On the OpenGL Core device (`SystemInfo.graphicsDeviceType`) the state is the texture's
//! (`GraphicsDevice::samples_through_texture_state`): the Unity 6000.0 Manual (Using sampler
//! states) says "Inline samplers don't work if you target the OpenGL ES or OpenGL Core graphics
//! APIs", and the Editor there samples a texture through its own sampler state whatever sampler
//! the program names, an inline one (tests/projects/urp: `upscale_point` equals
//! `upscale_linear`; `inline_sampler`) or another texture's
//! (`_Detail.Sample(sampler_MainTex, uv)`), in either stage, and with comparisons too
//! (tests/projects/samplerstates). On the other devices a program samples through the sampler
//! it names, as the Vulkan Editor does (tests/projects/samplerstates, `--device vulkan`): an
//! inline state is that state, another texture's sampler that texture's state.
//!
//! Compute kernels retain predefined sampler states (tests/projects/renderapi computeSamplers).
//!
//! [`own_samplers`] is the OpenGL Core rule: it rewrites a graphics program's SPIR-V so that
//! every sample of a texture through an inline sampler (`sampler_PointClamp`,
//! `s_linear_clamp_sampler`: a sampler named like no texture's, whose name spells a state) or
//! through another texture's uses the texture's own sampler (`sampler<texture>`, declared when
//! the program does not). glslang passes textures and samplers to functions resolved, so every
//! sample names its texture and sampler variables. A sample whose texture is not a variable
//! keeps its sampler (an inline state is then what the sampler binds).
//!
//! A texture has an own sampler of each kind, both named `sampler<texture>`: the one its
//! comparisons go through and the one its other samples do (a sampler variable is one kind or
//! the other, by what samples through it; glslang likewise declares a texture sampled both
//! ways as two variables of one name). A comparison goes through the texture's own comparing
//! one whatever comparison sampler it names (samplerstates CmpTwo.map, CmpOther.mapLinear).

use crate::InlineSampler;
use anyhow::{Result, bail};
use std::collections::{BTreeMap, BTreeSet, HashMap, HashSet};

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
/// `OpTypeImage`'s Depth operand of a depth image.
const IMAGE_DEPTH: u32 = 1;

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

/// What the rewrites read of a module: its texture and sampler variables and its samples.
struct Sampling<'a> {
    words: &'a [u32],
    list: Vec<(usize, usize)>,
    names: HashMap<u32, String>,
    /// Texture variables: id → (pointer type, name, instruction index).
    textures: HashMap<u32, (u32, String, usize)>,
    /// The texture variables of depth images (sampled with comparisons): glslang declares a
    /// texture sampled both ways as two variables of one name, one of each kind.
    depth: HashSet<u32>,
    /// The texture variables of integer texels.
    integer: HashSet<u32>,
    /// Sampler variables: id → (pointer type, instruction index).
    samplers: BTreeMap<u32, (u32, usize)>,
    /// The values loaded or copied (and what function-local variables hold).
    loads: HashMap<u32, u32>,
    stores: HashMap<u32, u32>,
    /// The type of each loaded or copied value.
    value_types: HashMap<u32, u32>,
    /// The sampled images sampled with a depth reference.
    depth_samples: HashSet<u32>,
    /// The `OpSampledImage` instructions.
    samples: Vec<usize>,
}

impl<'a> Sampling<'a> {
    fn scan(words: &'a [u32]) -> Result<Self> {
        if words.len() < 5 {
            bail!("empty SPIR-V");
        }
        let list = crate::fit::instructions(words)?;
        let mut s = Sampling {
            words,
            list,
            names: HashMap::new(),
            textures: HashMap::new(),
            depth: HashSet::new(),
            integer: HashSet::new(),
            samplers: BTreeMap::new(),
            loads: HashMap::new(),
            stores: HashMap::new(),
            value_types: HashMap::new(),
            depth_samples: HashSet::new(),
            samples: Vec::new(),
        };
        let mut int_types = HashSet::new();
        // Image types: id → (of integer texels, a depth image).
        let mut image_types: HashMap<u32, (bool, bool)> = HashMap::new();
        let mut sampler_types = HashSet::new();
        let mut pointers: HashMap<u32, u32> = HashMap::new();
        let mut in_function = false;
        for k in 0..s.list.len() {
            let w = s.inst(k);
            match s.op(k) {
                OP_NAME if w.len() >= 3 => {
                    s.names.insert(w[1], string(&w[2..]));
                }
                OP_TYPE_INT => {
                    int_types.insert(w[1]);
                }
                OP_TYPE_IMAGE if w.len() >= 3 => {
                    let depth = w.get(4) == Some(&IMAGE_DEPTH);
                    image_types.insert(w[1], (int_types.contains(&w[2]), depth));
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
                    if let Some(&(integer, depth)) = image_types.get(pointee) {
                        let name = s.names.get(&w[2]).cloned().unwrap_or_default();
                        s.textures.insert(w[2], (w[1], name, k));
                        if integer {
                            s.integer.insert(w[2]);
                        }
                        if depth {
                            s.depth.insert(w[2]);
                        }
                    } else if sampler_types.contains(pointee) {
                        s.samplers.insert(w[2], (w[1], k));
                    }
                }
                OP_LOAD | OP_COPY_OBJECT if w.len() >= 4 => {
                    s.loads.insert(w[2], w[3]);
                    s.value_types.insert(w[2], w[1]);
                }
                OP_STORE if w.len() >= 3 => {
                    s.stores.insert(w[1], w[2]);
                }
                OP_SAMPLED_IMAGE if w.len() >= 5 => s.samples.push(k),
                o if DEPTH_REFERENCE_OPS.contains(&o) && w.len() >= 4 => {
                    s.depth_samples.insert(w[3]);
                }
                _ => {}
            }
        }
        Ok(s)
    }

    fn inst(&self, k: usize) -> &'a [u32] {
        &self.words[self.list[k].0..self.list[k].0 + self.list[k].1]
    }

    fn op(&self, k: usize) -> u32 {
        self.inst(k)[0] & 0xffff
    }

    /// The texture or sampler variable a value comes from (glslang passes textures and samplers
    /// to functions resolved: every sample names its globals).
    fn origin(&self, mut id: u32) -> Option<u32> {
        for _ in 0..64 {
            if self.textures.contains_key(&id) || self.samplers.contains_key(&id) {
                return Some(id);
            }
            id = *self.loads.get(&id).or_else(|| self.stores.get(&id))?;
        }
        None
    }

    /// Every sample of a named texture variable through a named sampler variable: the
    /// instruction, the texture and the sampler.
    fn pairs(&self) -> Vec<(usize, u32, u32)> {
        self.samples
            .iter()
            .filter_map(|&k| {
                let w = self.inst(k);
                let (t, s) = (self.origin(w[3])?, self.origin(w[4])?);
                (self
                    .textures
                    .get(&t)
                    .is_some_and(|(_, name, _)| !name.is_empty())
                    && self.samplers.contains_key(&s)
                    && self.names.contains_key(&s))
                .then_some((k, t, s))
            })
            .collect()
    }

    /// A texture variable as [`Texture`].
    fn texture(&self, id: u32) -> Texture {
        (self.textures[&id].1.clone(), self.depth.contains(&id))
    }

    /// The binding after the program's last.
    fn next_binding(&self) -> u32 {
        (0..self.list.len())
            .map(|k| self.inst(k))
            .filter(|i| (i[0] & 0xffff) == OP_DECORATE && i.len() >= 4 && i[2] == BINDING)
            .map(|i| i[3] + 1)
            .max()
            .unwrap_or(0)
    }
}

/// The SPIR-V with every sample of a texture through the texture's own sampler of its kind,
/// whatever sampler it names (see the module).
pub(crate) fn own_samplers(words: &[u32]) -> Result<Vec<u32>> {
    let scan = Sampling::scan(words)?;
    let list = &scan.list;
    let inst = |k: usize| scan.inst(k);
    let op = |k: usize| scan.op(k);
    let names = &scan.names;
    let samplers = &scan.samplers;
    let value_types = &scan.value_types;
    let depth_samples = &scan.depth_samples;
    // Integer textures are read unfiltered (the renderer binds them a point sampler): their
    // samples keep the sampler they name.
    let textures: HashMap<u32, String> = scan
        .textures
        .iter()
        .filter(|(id, _)| !scan.integer.contains(id))
        .map(|(id, (_, name, _))| (*id, name.clone()))
        .collect();
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
    let origin = |id: u32| scan.origin(id);
    // A sampler of another texture than the one it samples (its own is the one of the
    // texture's property, as the renderer pairs them).
    let foreign = |t: u32, s: u32| {
        let property = crate::fit::texture_property(&textures[&t], false);
        names
            .get(&s)
            .is_some_and(|sampler| crate::fit::texture_property(sampler, true) != property)
    };
    // The samplers that compare (their samples have a depth reference).
    let comparing: HashSet<u32> = scan
        .samples
        .iter()
        .filter(|&&k| depth_samples.contains(&inst(k)[2]))
        .filter_map(|&k| origin(inst(k)[4]))
        .collect();
    // A texture's own sampler of a kind, when the program declares it.
    let own_of = |t: u32, compares: bool| {
        let own = format!("sampler{}", textures[&t]);
        samplers
            .keys()
            .copied()
            .find(|id| names.get(id) == Some(&own) && comparing.contains(id) == compares)
    };
    // Each sample that goes through its texture's own sampler instead of the one it names: an
    // inline sampler, another texture's, and for a comparison any other.
    let mut replaced = HashSet::new();
    let rewrite_samples: Vec<(usize, (u32, bool))> = scan
        .samples
        .iter()
        .filter_map(|&k| {
            let w = inst(k);
            let (t, s) = (origin(w[3])?, origin(w[4])?);
            if !textures.contains_key(&t) || !samplers.contains_key(&s) {
                return None;
            }
            let compares = depth_samples.contains(&w[2]);
            let rewrite = (compares || inline.contains(&s) || foreign(t, s))
                && own_of(t, compares) != Some(s);
            if rewrite {
                replaced.insert(s);
            }
            rewrite.then_some((k, (t, compares)))
        })
        .collect();
    if rewrite_samples.is_empty() {
        return Ok(words.to_vec());
    }

    // The textures' own samplers, declared after a replaced one where the program has none.
    let mut bound = words[3];
    let &(pointer_type, declared_at) = samplers
        .iter()
        .find(|(id, _)| replaced.contains(id))
        .map(|(_, v)| v)
        .expect("a replaced sampler");
    // New variables are bound after the program's (the renderer rebinds them by name).
    let mut next_binding = scan.next_binding();
    let mut own: HashMap<(u32, bool), u32> = HashMap::new();
    let mut declarations: Vec<(u32, String)> = Vec::new();
    for &(_, (t, compares)) in &rewrite_samples {
        if own.contains_key(&(t, compares)) {
            continue;
        }
        let id = own_of(t, compares).unwrap_or_else(|| {
            let id = bound;
            bound += 1;
            declarations.push((id, format!("sampler{}", textures[&t])));
            id
        });
        own.insert((t, compares), id);
    }

    let mut out =
        Vec::with_capacity(words.len() + 8 * (declarations.len() + rewrite_samples.len()));
    out.extend_from_slice(&words[..5]);
    out[3] = 0;
    let samples_at: HashMap<usize, (u32, bool)> = rewrite_samples.into_iter().collect();
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
        if let Some(sample) = samples_at.get(&k)
            && let Some(&sampler_type) = value_types.get(&w[4])
        {
            // A load of the texture's own sampler, sampled with instead.
            let loaded = bound;
            bound += 1;
            out.extend_from_slice(&[(4 << 16) | OP_LOAD, sampler_type, loaded, own[sample]]);
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

/// A texture as a program declares it: its name, and whether the variable is the depth image
/// of the name (a texture sampled with and without comparisons is a variable of each kind).
pub(crate) type Texture = (String, bool);

/// The texture and the sampler variable (its name) of every sample of a module.
pub(crate) fn sampled_pairs(words: &[u32]) -> Result<BTreeSet<(Texture, String)>> {
    let scan = Sampling::scan(words)?;
    Ok(scan
        .pairs()
        .into_iter()
        .map(|(_, t, s)| (scan.texture(t), scan.names[&s].clone()))
        .collect())
}

/// For each texture a program samples through several samplers (`pairs`: every stage's), the
/// sampler its own variable stays with: the texture's own sampler (`sampler<texture>`) when
/// the program samples through it, else the first by name. One rule for the whole program, so
/// that its stages agree.
pub(crate) fn primary_samplers(pairs: &BTreeSet<(Texture, String)>) -> HashMap<Texture, String> {
    let mut through: BTreeMap<&Texture, Vec<&str>> = BTreeMap::new();
    for (texture, sampler) in pairs {
        through.entry(texture).or_default().push(sampler);
    }
    through
        .into_iter()
        .filter(|(_, samplers)| samplers.len() > 1)
        .map(|(texture, samplers)| {
            let own = samplers
                .iter()
                .find(|s| sampler_texture(s) == Some(texture.0.as_str()))
                .unwrap_or(&samplers[0]);
            (texture.clone(), own.to_string())
        })
        .collect()
}

/// The texture a texture variable named `name` shows: a further variable of a texture, for
/// another sampler, is named `<texture>@<sampler>` ([`one_sampler_per_texture`]).
pub(crate) fn variable_texture(name: &str) -> &str {
    name.split_once(FURTHER)
        .map_or(name, |(texture, _)| texture)
}

/// Between a texture's name and a sampler's in the name of a further variable of the texture.
const FURTHER: char = '@';

/// The SPIR-V with one texture variable for each sampler a texture is sampled through: the
/// texture's own variable for its primary sampler ([`primary_samplers`], of the whole
/// program), a further variable of the same type named `<texture>@<sampler>` for each other
/// one (bound after the program's; the renderer binds it the texture). OpenGL samples through
/// combined texture-and-sampler units, so a texture sampled through two sampler states is two
/// units of one texture: the OpenGL Core Editor's kernels do sample one texture through
/// several states (tests/projects/renderapi computeSamplers), and wgpu's OpenGL backend takes
/// one sampler for each texture variable. Programs that sample each texture through one
/// sampler are unchanged.
pub(crate) fn one_sampler_per_texture(
    words: &[u32],
    primary: &HashMap<Texture, String>,
) -> Result<Vec<u32>> {
    if primary.is_empty() {
        return Ok(words.to_vec());
    }
    let scan = Sampling::scan(words)?;
    let mut bound = words[3];
    // A variable for each further sampler of a texture: (texture, sampler name) → id.
    let mut further: BTreeMap<(u32, &str), u32> = BTreeMap::new();
    let mut samples_at: HashMap<usize, u32> = HashMap::new();
    for (k, t, s) in scan.pairs() {
        let sampler = scan.names[&s].as_str();
        if primary
            .get(&scan.texture(t))
            .is_none_or(|own| own == sampler)
        {
            continue;
        }
        let variable = *further.entry((t, sampler)).or_insert_with(|| {
            bound += 1;
            bound - 1
        });
        samples_at.insert(k, variable);
    }
    if further.is_empty() {
        return Ok(words.to_vec());
    }
    let mut next_binding = scan.next_binding();

    let mut out = Vec::with_capacity(words.len() + 16 * further.len() + 4 * samples_at.len());
    out.extend_from_slice(&words[..5]);
    let (mut named, mut decorated) = (false, false);
    for k in 0..scan.list.len() {
        let w = scan.inst(k);
        if scan.op(k) == OP_NAME && !named {
            named = true;
            for (&(t, sampler), &id) in &further {
                let name = format!("{}{FURTHER}{sampler}", scan.textures[&t].1);
                out.extend(name_words(id, &name));
            }
        }
        if !decorated
            && (scan.op(k) == OP_DECORATE || (FIRST_TYPE..=LAST_TYPE).contains(&scan.op(k)))
        {
            decorated = true;
            for &id in further.values() {
                out.extend_from_slice(&[(4 << 16) | OP_DECORATE, id, DESCRIPTOR_SET, 0]);
                out.extend_from_slice(&[(4 << 16) | OP_DECORATE, id, BINDING, next_binding]);
                next_binding += 1;
            }
        }
        if let Some(&variable) = samples_at.get(&k)
            && let Some(&image_type) = scan.value_types.get(&w[3])
        {
            // A load of the texture's variable for this sampler, sampled instead.
            let loaded = bound;
            bound += 1;
            out.extend_from_slice(&[(4 << 16) | OP_LOAD, image_type, loaded, variable]);
            out.extend_from_slice(&[w[0], w[1], w[2], loaded]);
            out.extend_from_slice(&w[4..]);
            continue;
        }
        out.extend_from_slice(w);
        // Each further variable is declared after its texture's own (of its pointer type).
        for (&(t, _), &id) in &further {
            let (pointer_type, _, declared_at) = scan.textures[&t];
            if k == declared_at {
                out.extend_from_slice(&[
                    (4 << 16) | OP_VARIABLE,
                    pointer_type,
                    id,
                    UNIFORM_CONSTANT,
                ]);
            }
        }
    }
    out[3] = bound;
    Ok(out)
}

/// Integer-coordinate fetches use a non-comparing alias of a compared texture's property.
pub(crate) fn separate_depth_fetches(words: &[u32]) -> Result<Vec<u32>> {
    const OP_IMAGE_FETCH: u32 = 95;
    let scan = Sampling::scan(words)?;
    let mut fetches = BTreeMap::new();
    let mut variables = BTreeSet::new();
    for k in 0..scan.list.len() {
        let w = scan.inst(k);
        if scan.op(k) == OP_IMAGE_FETCH
            && w.len() >= 4
            && let Some(variable) = scan.origin(w[3])
            && scan.depth.contains(&variable)
        {
            fetches.insert(k, variable);
            variables.insert(variable);
        }
    }
    if fetches.is_empty() {
        return Ok(words.to_vec());
    }
    let mut bound = words[3];
    let mut id = || {
        let next = bound;
        bound += 1;
        next
    };
    let mut raw_types = BTreeMap::new();
    let mut aliases = BTreeMap::new();
    let mut types_after: BTreeMap<usize, Vec<u32>> = BTreeMap::new();
    for &variable in &variables {
        let (pointer, name, _) = &scan.textures[&variable];
        if name.is_empty() {
            bail!("a compared texture Load needs a named resource");
        }
        let (raw_image, raw_pointer) = match raw_types.get(pointer) {
            Some(types) => *types,
            None => {
                let p = (0..scan.list.len())
                    .find(|&k| scan.op(k) == OP_TYPE_POINTER && scan.inst(k)[1] == *pointer)
                    .ok_or_else(|| {
                        anyhow::anyhow!("a compared texture Load has no pointer type")
                    })?;
                let image_type = scan.inst(p)[3];
                let t = (0..scan.list.len())
                    .find(|&k| scan.op(k) == OP_TYPE_IMAGE && scan.inst(k)[1] == image_type)
                    .ok_or_else(|| anyhow::anyhow!("a compared texture Load has no image type"))?;
                let (raw_image, raw_pointer) = (id(), id());
                let mut image = scan.inst(t).to_vec();
                image[1] = raw_image;
                image[4] = 0;
                types_after.entry(t).or_default().extend(image);
                types_after.entry(p).or_default().extend([
                    (4 << 16) | OP_TYPE_POINTER,
                    raw_pointer,
                    UNIFORM_CONSTANT,
                    raw_image,
                ]);
                raw_types.insert(*pointer, (raw_image, raw_pointer));
                (raw_image, raw_pointer)
            }
        };
        aliases.insert(variable, (raw_image, raw_pointer, id()));
    }
    drop(id);
    let mut next_binding = scan.next_binding();
    let mut out = Vec::with_capacity(words.len() + aliases.len() * 32 + fetches.len() * 4);
    out.extend_from_slice(&words[..5]);
    let (mut named, mut decorated) = (false, false);
    for k in 0..scan.list.len() {
        let w = scan.inst(k);
        if scan.op(k) == OP_NAME && !named {
            named = true;
            for (&original, &(_, _, raw)) in &aliases {
                out.extend(name_words(
                    raw,
                    &format!("{}{FURTHER}__unbound_load", scan.textures[&original].1),
                ));
            }
        }
        if !decorated
            && (scan.op(k) == OP_DECORATE || (FIRST_TYPE..=LAST_TYPE).contains(&scan.op(k)))
        {
            decorated = true;
            for &(_, _, raw) in aliases.values() {
                out.extend_from_slice(&[(4 << 16) | OP_DECORATE, raw, DESCRIPTOR_SET, 0]);
                out.extend_from_slice(&[(4 << 16) | OP_DECORATE, raw, BINDING, next_binding]);
                next_binding += 1;
            }
        }
        if let Some(variable) = fetches.get(&k) {
            let &(image_type, _, raw) = &aliases[variable];
            let loaded = bound;
            bound += 1;
            out.extend_from_slice(&[(4 << 16) | OP_LOAD, image_type, loaded, raw]);
            out.extend_from_slice(&[w[0], w[1], w[2], loaded]);
            out.extend_from_slice(&w[4..]);
        } else {
            out.extend_from_slice(w);
        }
        if let Some(types) = types_after.get(&k) {
            out.extend_from_slice(types);
        }
        for (&original, &(_, pointer, raw)) in &aliases {
            if k == scan.textures[&original].2 {
                out.extend_from_slice(&[(4 << 16) | OP_VARIABLE, pointer, raw, UNIFORM_CONSTANT]);
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
        let module = crate::fit::parse(words, crate::fit::ClipSpace::OpenGl).unwrap();
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
                ("_ShadowMap", "sampler_ShadowMap"),
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

    /// For every sample of the module: the texture, the sampler, and whether the sampler
    /// variable compares.
    fn sampled_kinds(words: &[u32]) -> Vec<(String, String, bool)> {
        let module = crate::fit::parse(words, crate::fit::ClipSpace::OpenGl).unwrap();
        naga::valid::Validator::new(
            naga::valid::ValidationFlags::all() - naga::valid::ValidationFlags::BINDINGS,
            naga::valid::Capabilities::all(),
        )
        .validate(&module)
        .unwrap();
        let mut found = Vec::new();
        let functions = module
            .functions
            .iter()
            .map(|(_, f)| f)
            .chain(module.entry_points.iter().map(|e| &e.function));
        for f in functions {
            let global = |h: naga::Handle<naga::Expression>| match &f.expressions[h] {
                naga::Expression::GlobalVariable(g) => &module.global_variables[*g],
                other => panic!("not a variable: {other:?}"),
            };
            for (_, e) in f.expressions.iter() {
                if let naga::Expression::ImageSample { image, sampler, .. } = e {
                    let sampler = global(*sampler);
                    let compares = matches!(
                        module.types[sampler.ty].inner,
                        naga::TypeInner::Sampler { comparison: true }
                    );
                    found.push((
                        global(*image).name.clone().unwrap(),
                        sampler.name.clone().unwrap(),
                        compares,
                    ));
                }
            }
        }
        found.sort();
        found
    }

    fn kinds(list: &[(&str, &str, bool)]) -> Vec<(String, String, bool)> {
        list.iter()
            .map(|(t, s, c)| (t.to_string(), s.to_string(), *c))
            .collect()
    }

    /// A texture whose own sampler compares has an own sampler for its other samples too, of
    /// the same name: a sample of it through an inline sampler goes through that one.
    #[test]
    fn a_compared_texture_has_an_own_sampler_of_each_kind() {
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
        assert_eq!(
            sampled_kinds(&own_samplers(&words).unwrap()),
            kinds(&[
                ("_Shadow", "sampler_Shadow", false),
                ("_Shadow", "sampler_Shadow", true),
            ])
        );
    }

    /// A comparison goes through the texture's own comparing sampler whatever sampler it
    /// names, declared when the program has none (its `sampler_Shadow` is the one the other
    /// samples go through).
    #[test]
    fn comparisons_go_through_the_textures_own_comparing_sampler() {
        let source = r#"
Texture2D _Shadow; SamplerState sampler_Shadow;
Texture2D _Map; SamplerComparisonState sampler_Map;
SamplerComparisonState sampler_LinearClampCompare;
float4 frag(float2 uv : TEXCOORD0) : SV_Target {
    return _Shadow.SampleCmpLevelZero(sampler_LinearClampCompare, uv, .5)
        + _Shadow.Sample(sampler_Shadow, uv)
        + _Map.SampleCmpLevelZero(sampler_Map, uv, .5)
        + _Map.SampleCmpLevelZero(sampler_LinearClampCompare, uv, .25);
}"#;
        let words = spirv(source, "frag", shaderc::ShaderKind::Fragment);
        assert_eq!(
            sampled_kinds(&words),
            kinds(&[
                ("_Map", "sampler_LinearClampCompare", true),
                ("_Map", "sampler_Map", true),
                ("_Shadow", "sampler_LinearClampCompare", true),
                ("_Shadow", "sampler_Shadow", false),
            ])
        );
        assert_eq!(
            sampled_kinds(&own_samplers(&words).unwrap()),
            kinds(&[
                ("_Map", "sampler_Map", true),
                ("_Map", "sampler_Map", true),
                ("_Shadow", "sampler_Shadow", false),
                ("_Shadow", "sampler_Shadow", true),
            ])
        );
    }

    const TWO_STATES: &str = r#"
Texture2D<float4> _Source;
Texture3D<float4> _Volume;
SamplerState sampler_Source;
SamplerState s_point_clamp_sampler;
SamplerState s_linear_repeat_sampler;
SamplerState sampler_Volume;
RWStructuredBuffer<float4> _Out;
float4 Twice(Texture2D<float4> t, SamplerState s, float2 uv) {
    return t.SampleLevel(s, uv, 0) + t.SampleLevel(s, uv, 1);
}
[numthreads(1,1,1)] void Main(uint3 id : SV_DispatchThreadID) {
    _Out[0] = _Source.SampleLevel(sampler_Source, float2(.5,.5), 0);
    _Out[1] = Twice(_Source, s_point_clamp_sampler, float2(.5,.5));
    _Out[2] = _Source.SampleLevel(s_linear_repeat_sampler, float2(.5,.5), 0)
        + _Source.SampleLevel(sampler_Source, float2(.25,.5), 0);
    _Out[3] = _Volume.SampleLevel(sampler_Volume, float3(.5,.5,.5), 0)
        + _Volume.SampleLevel(s_point_clamp_sampler, float3(.5,.5,.5), 0);
    _Out[4] = _Source.Load(int3(0, 0, 0));
}"#;

    /// For every sample: the texture variable (its handle's index and name) and the sampler.
    fn sampled_variables(words: &[u32]) -> Vec<(usize, String, String)> {
        let module = crate::fit::parse(words, crate::fit::ClipSpace::OpenGl).unwrap();
        naga::valid::Validator::new(
            naga::valid::ValidationFlags::all() - naga::valid::ValidationFlags::BINDINGS,
            naga::valid::Capabilities::all(),
        )
        .validate(&module)
        .unwrap();
        let mut out = Vec::new();
        let functions = module
            .functions
            .iter()
            .map(|(_, f)| f)
            .chain(module.entry_points.iter().map(|e| &e.function));
        for f in functions {
            let global = |h: naga::Handle<naga::Expression>| match &f.expressions[h] {
                naga::Expression::GlobalVariable(g) => *g,
                other => panic!("not a variable: {other:?}"),
            };
            for (_, e) in f.expressions.iter() {
                if let naga::Expression::ImageSample { image, sampler, .. } = e {
                    let (image, sampler) = (global(*image), global(*sampler));
                    out.push((
                        image.index(),
                        module.global_variables[image].name.clone().unwrap(),
                        module.global_variables[sampler].name.clone().unwrap(),
                    ));
                }
            }
        }
        out.sort();
        out.dedup();
        out
    }

    /// The rewrite of one stage alone (a kernel): its own pairs decide.
    fn split(words: &[u32]) -> Vec<u32> {
        let primary = primary_samplers(&sampled_pairs(words).unwrap());
        one_sampler_per_texture(words, &primary).unwrap()
    }

    #[test]
    fn a_texture_sampled_through_several_samplers_has_a_variable_for_each() {
        let words = spirv(TWO_STATES, "Main", shaderc::ShaderKind::Compute);
        // As compiled: each texture is one variable, whatever samples it.
        let before = sampled_variables(&words);
        let variables = |pairs: &[(usize, String, String)], texture: &str| {
            pairs
                .iter()
                .filter(|(_, t, _)| variable_texture(t) == texture)
                .map(|(v, ..)| *v)
                .collect::<HashSet<_>>()
                .len()
        };
        assert_eq!(variables(&before, "_Source"), 1);
        let after = sampled_variables(&split(&words));
        // The same samples, by texture and sampler...
        let named = |pairs: &[(usize, String, String)]| {
            let mut named: Vec<_> = pairs
                .iter()
                .map(|(_, t, s)| (variable_texture(t).to_string(), s.clone()))
                .collect();
            named.sort();
            named
        };
        assert_eq!(named(&after), named(&before));
        assert_eq!(after.len(), 5);
        // ...each texture variable sampled through one sampler only, through a function too.
        let mut samplers_of: HashMap<usize, HashSet<&str>> = HashMap::new();
        for (variable, _, sampler) in &after {
            samplers_of.entry(*variable).or_default().insert(sampler);
        }
        assert!(samplers_of.values().all(|s| s.len() == 1), "{after:?}");
        assert_eq!(variables(&after, "_Source"), 3);
        assert_eq!(variables(&after, "_Volume"), 2);
        // A texture's own variable stays with its own sampler; the others name theirs.
        let names: HashSet<(&str, &str)> = after
            .iter()
            .map(|(_, t, s)| (t.as_str(), s.as_str()))
            .collect();
        assert!(names.contains(&("_Source", "sampler_Source")));
        assert!(names.contains(&("_Source@s_point_clamp_sampler", "s_point_clamp_sampler")));
        assert!(names.contains(&("_Volume@s_point_clamp_sampler", "s_point_clamp_sampler")));
    }

    /// The stages of one program agree on the variable each sampler samples through: the
    /// rule is the whole program's, whatever one stage alone samples.
    #[test]
    fn the_primary_sampler_is_the_whole_programs() {
        let pairs = |list: &[(&str, &str)]| -> BTreeSet<(Texture, String)> {
            list.iter()
                .map(|(t, s)| ((t.to_string(), false), s.to_string()))
                .collect()
        };
        // One stage samples `_A` through another texture's sampler only, the other through
        // its own: the own sampler keeps `_A`'s variable in both.
        let program = pairs(&[
            ("_A", "sampler_B"),
            ("_A", "sampler_A"),
            ("_B", "sampler_B"),
        ]);
        let primary = primary_samplers(&program);
        assert_eq!(primary.len(), 1);
        let a = ("_A".to_string(), false);
        assert_eq!(primary[&a], "sampler_A");
        // No own sampler among them: the first by name.
        let foreign = pairs(&[("_A", "sampler_C"), ("_A", "sampler_B")]);
        assert_eq!(primary_samplers(&foreign)[&a], "sampler_B");
        // The stage that samples `_A` through `sampler_B` alone still takes the further
        // variable.
        let source = r#"
Texture2D<float4> _A;
SamplerState sampler_B;
RWStructuredBuffer<float4> _Out;
[numthreads(1,1,1)] void Main(uint3 id : SV_DispatchThreadID) {
    _Out[0] = _A.SampleLevel(sampler_B, float2(.5,.5), 0);
}"#;
        let words = spirv(source, "Main", shaderc::ShaderKind::Compute);
        let after = sampled_variables(&one_sampler_per_texture(&words, &primary).unwrap());
        assert_eq!(after.len(), 1);
        assert_eq!(
            (after[0].1.as_str(), after[0].2.as_str()),
            ("_A@sampler_B", "sampler_B")
        );
        // Alone, the stage is unchanged.
        assert_eq!(split(&words), words);
    }

    /// A texture sampled with and without comparisons is two variables of one name as
    /// compiled, a depth image and a plain one, each sampled through its own sampler: neither
    /// gets a further variable (the comparison is not another sampler of the plain one).
    #[test]
    fn a_texture_compared_and_sampled_keeps_its_two_variables() {
        let source = r#"
Texture2D _Shadow;
SamplerComparisonState sampler_Shadow;
SamplerState sampler_B;
RWStructuredBuffer<float4> _Out;
[numthreads(1,1,1)] void Main(uint3 id : SV_DispatchThreadID) {
    _Out[0] = _Shadow.SampleCmpLevelZero(sampler_Shadow, float2(.5,.5), .5)
        + _Shadow.SampleLevel(sampler_B, float2(.5,.5), 0);
}"#;
        let words = spirv(source, "Main", shaderc::ShaderKind::Compute);
        let pairs = sampled_pairs(&words).unwrap();
        let kinds: Vec<_> = pairs
            .iter()
            .map(|((name, depth), sampler)| (name.as_str(), *depth, sampler.as_str()))
            .collect();
        assert_eq!(
            kinds,
            [
                ("_Shadow", false, "sampler_B"),
                ("_Shadow", true, "sampler_Shadow")
            ]
        );
        assert!(primary_samplers(&pairs).is_empty());
        assert_eq!(split(&words), words);
    }

    #[test]
    fn programs_sampling_each_texture_through_one_sampler_are_unchanged() {
        let source = r#"
Texture2D<float4> _A;
Texture2D<float4> _B;
SamplerState sampler_A;
RWStructuredBuffer<float4> _Out;
[numthreads(1,1,1)] void Main(uint3 id : SV_DispatchThreadID) {
    _Out[0] = _A.SampleLevel(sampler_A, float2(.5,.5), 0) + _B.SampleLevel(sampler_A, float2(.5,.5), 0);
    _Out[1] = _B.Load(int3(0, 0, 0));
}"#;
        let words = spirv(source, "Main", shaderc::ShaderKind::Compute);
        assert_eq!(split(&words), words);
    }

    /// A sample through another texture's sampler uses the texture's own too, declared when
    /// the program has none.
    #[test]
    fn another_textures_sampler_becomes_the_textures_own() {
        let source = r#"
Texture2D _A; SamplerState sampler_A;
Texture2D _B; SamplerState sampler_B;
Texture2D _C;
Texture2D _Shadow; SamplerComparisonState sampler_Shadow;
float4 frag(float2 uv : TEXCOORD0) : SV_Target {
    return _A.Sample(sampler_A, uv) + _A.Sample(sampler_B, uv) + _B.Sample(sampler_B, uv)
        + _C.Sample(sampler_A, uv) + _Shadow.SampleCmpLevelZero(sampler_Shadow, uv, .5);
}"#;
        let words = spirv(source, "frag", shaderc::ShaderKind::Fragment);
        let named = |pairs: Vec<(String, String)>| -> Vec<(String, String)> { pairs };
        assert_eq!(
            named(sampled(&own_samplers(&words).unwrap())),
            [
                ("_A", "sampler_A"),
                ("_A", "sampler_A"),
                ("_B", "sampler_B"),
                ("_C", "sampler_C"),
                ("_Shadow", "sampler_Shadow"),
            ]
            .map(|(a, b)| (a.to_string(), b.to_string()))
        );
    }

    /// A program that samples each texture through its own sampler is returned as it was.
    #[test]
    fn programs_sampling_through_own_samplers_are_unchanged() {
        let source = r#"
Texture2D _MainTex;
SamplerState sampler_MainTex;
float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_Target { return _MainTex.Sample(sampler_MainTex, uv); }
"#;
        let words = spirv(source, "main", shaderc::ShaderKind::Fragment);
        assert_eq!(own_samplers(&words).unwrap(), words);
    }
}
