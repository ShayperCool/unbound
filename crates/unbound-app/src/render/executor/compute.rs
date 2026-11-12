//! Buffers and compute dispatches: GraphicsBuffer/ComputeBuffer storage, kernels compiled from
//! their `.compute` files (unbound-shaders), and each dispatch's bindings found by name among its
//! own properties, then the global ones.
use super::{Op, UnityRenderer};
use std::{
    collections::{BTreeMap, HashMap},
    sync::Arc,
};
use unbound_runtime::render_protocol::{
    commands::{self, buffer},
    owned::PropertyValue,
};
use unbound_shaders::{
    TextureDim, Uniform,
    compute::{CompiledKernel, ComputeProgram, ResourceKind},
};
use wgpu::util::DeviceExt;

pub struct GpuBuffer {
    pub buffer: wgpu::Buffer,
    pub size: u64,
    pub revision: u64,
    /// A constant buffer's bytes as written (draws copy them into their uniform blocks; what
    /// compute kernels write into it is not seen).
    pub shadow: Option<Vec<u8>>,
    /// The hidden counter of an Append or Counter buffer: one `u32`, bound where a kernel's
    /// `<name>@count` block (glslang's counter of `<name>`) is.
    pub counter: Option<wgpu::Buffer>,
    /// A dispatch has bound the counter: until then Unity's CopyCount writes nothing (while
    /// SetCounterValue still sets where the first dispatch starts).
    pub counter_used: bool,
}

pub struct DispatchOp {
    pub(super) pipeline: wgpu::ComputePipeline,
    pub(super) bind_group: wgpu::BindGroup,
    pub(super) groups: [u32; 3],
    /// Group counts read on the GPU: the arguments buffer and a byte offset.
    pub(super) indirect: Option<(wgpu::Buffer, u64)>,
}

/// A copy between buffers (or from a buffer's counter), encoded in order with the passes.
pub struct BufferCopyOp {
    source: wgpu::Buffer,
    source_offset: u64,
    destination: wgpu::Buffer,
    destination_offset: u64,
    size: u64,
}

/// The suffix of the int property holding the byte offset a constant buffer bound by a cbuffer's
/// name is read from (`ComputeShader.SetConstantBuffer`).
const CONSTANT_OFFSET_SUFFIX: &str = "@offset";

/// The suffix glslang gives the block holding a buffer's hidden counter.
pub(super) const COUNTER_SUFFIX: &str = "@count";

pub struct KernelGpu {
    kernel: Arc<CompiledKernel>,
    layout: wgpu::BindGroupLayout,
    pipeline: wgpu::ComputePipeline,
    sampler_defaults: Vec<Option<super::SamplerKey>>,
    sampler_alternates: Vec<Option<String>>,
    constant_names: Vec<(String, String)>,
}

#[derive(Clone, PartialEq, Eq, Hash)]
enum Bound {
    Buffer(wgpu::Buffer, u64, Option<std::num::NonZeroU64>),
    View(Option<wgpu::Texture>, wgpu::TextureView),
    Sampler(Option<wgpu::Texture>, super::SamplerKey),
}

struct UniformSlot {
    buffer: wgpu::Buffer,
    data: Vec<u8>,
}

// Keep recently used resource combinations and repeated-dispatch slots warm. Retirement bounds
// retained GPU objects by recent work, including workloads whose resource order changes each frame.
const BINDING_IDLE_FRAMES: u64 = 8;

struct ReuseSlots<T> {
    submission: u64,
    next: usize,
    slots: BTreeMap<usize, (u64, T)>,
}

impl<T> Default for ReuseSlots<T> {
    fn default() -> Self {
        Self {
            submission: 0,
            next: 0,
            slots: BTreeMap::new(),
        }
    }
}

impl<T> ReuseSlots<T> {
    fn claim(
        &mut self,
        submission: u64,
        frame: u64,
        distinct: bool,
        make: impl FnOnce() -> T,
    ) -> &mut T {
        if self.submission != submission {
            self.submission = submission;
            self.next = 0;
        }
        let index = if distinct {
            let index = self.next;
            self.next += 1;
            index
        } else {
            0
        };
        let slot = self.slots.entry(index).or_insert_with(|| (frame, make()));
        slot.0 = frame;
        &mut slot.1
    }

    fn retire(&mut self, cutoff: u64) -> bool {
        self.slots.retain(|_, (frame, _)| *frame >= cutoff);
        !self.slots.is_empty()
    }
}

struct DispatchSlot {
    uniforms: Vec<(usize, UniformSlot)>,
    group: wgpu::BindGroup,
}

/// A resource combination owns its uniform slots, independent of dispatch ordering. Repeated
/// dispatches within one submission need distinct contents; later submissions reuse those slots.
#[derive(Default)]
pub(super) struct BindingCache {
    submission: u64,
    frame: u64,
    groups: HashMap<wgpu::BindGroupLayout, HashMap<Vec<(u32, Bound)>, ReuseSlots<DispatchSlot>>>,
    scratch: Vec<(u32, Bound)>,
    formats: Vec<(usize, &'static str)>,
}

impl BindingCache {
    pub(super) fn begin(&mut self) {
        self.submission += 1;
    }

    pub(super) fn frame(&mut self, frame: u64) {
        self.frame = frame;
        let cutoff = frame.saturating_sub(BINDING_IDLE_FRAMES - 1);
        self.groups.retain(|_, groups| {
            groups.retain(|_, slots| slots.retire(cutoff));
            !groups.is_empty()
        });
    }

    fn group(
        &mut self,
        device: &wgpu::Device,
        queue: &wgpu::Queue,
        gpu: &KernelGpu,
        bindings: &[(u32, Bound)],
        properties: &[(String, PropertyValue)],
        globals: &[(String, PropertyValue)],
        reversed_z: bool,
    ) -> wgpu::BindGroup {
        let groups = self.groups.entry(gpu.layout.clone()).or_default();
        if !groups.contains_key(bindings) {
            groups.insert(bindings.to_vec(), ReuseSlots::default());
        }
        let slots = groups.get_mut(bindings).unwrap();
        let generated = |binding| !bindings.iter().any(|(index, _)| *index == binding);
        let distinct = gpu
            .kernel
            .blocks
            .iter()
            .any(|block| generated(block.binding));
        let slot = slots.claim(self.submission, self.frame, distinct, || {
            let uniforms: Vec<_> = gpu
                .kernel
                .blocks
                .iter()
                .enumerate()
                .filter(|(_, block)| generated(block.binding))
                .map(|(index, block)| {
                    let size = block.size.max(16) as usize;
                    (
                        index,
                        UniformSlot {
                            buffer: device.create_buffer(&wgpu::BufferDescriptor {
                                label: Some("unbound kernel uniforms"),
                                size: size as u64,
                                usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
                                mapped_at_creation: false,
                            }),
                            data: vec![0; size],
                        },
                    )
                })
                .collect();
            // Samplers and binding groups are created only when a reusable slot is first needed.
            let samplers: Vec<_> = bindings
                .iter()
                .filter_map(|(_, binding)| match binding {
                    Bound::Sampler(_, key) => Some(super::super::resources::create_sampler(
                        device, *key, reversed_z,
                    )),
                    _ => None,
                })
                .collect();
            let mut samplers = samplers.iter();
            let mut entries: Vec<_> = bindings
                .iter()
                .map(|(binding, resource)| wgpu::BindGroupEntry {
                    binding: *binding,
                    resource: match resource {
                        Bound::Buffer(buffer, offset, size) => {
                            wgpu::BindingResource::Buffer(wgpu::BufferBinding {
                                buffer,
                                offset: *offset,
                                size: *size,
                            })
                        }
                        Bound::View(_, view) => wgpu::BindingResource::TextureView(view),
                        Bound::Sampler(_, _) => {
                            wgpu::BindingResource::Sampler(samplers.next().unwrap())
                        }
                    },
                })
                .collect();
            entries.extend(uniforms.iter().map(|(index, slot)| wgpu::BindGroupEntry {
                binding: gpu.kernel.blocks[*index].binding,
                resource: slot.buffer.as_entire_binding(),
            }));
            let group = device.create_bind_group(&wgpu::BindGroupDescriptor {
                label: Some("unbound kernel"),
                layout: &gpu.layout,
                entries: &entries,
            });
            DispatchSlot { uniforms, group }
        });
        for (index, uniform) in &mut slot.uniforms {
            uniform.data.fill(0);
            for u in &gpu.kernel.blocks[*index].uniforms {
                if let Some(v) = UnityRenderer::dispatch_value(properties, globals, &u.name) {
                    write_uniform(u, v, &mut uniform.data);
                }
            }
            queue.write_buffer(&uniform.buffer, 0, &uniform.data);
        }
        slot.group.clone()
    }

    pub(super) fn release_texture(&mut self, texture: &wgpu::Texture) {
        for groups in self.groups.values_mut() {
            groups.retain(|bindings, _| !bindings.iter().any(|(_, binding)| matches!(binding, Bound::View(Some(source), _) | Bound::Sampler(Some(source), _) if source == texture)));
        }
    }

    pub(super) fn release_sampler(&mut self, texture: &wgpu::Texture) {
        for groups in self.groups.values_mut() {
            groups.retain(|bindings, _| !bindings.iter().any(|(_, binding)| matches!(binding, Bound::Sampler(Some(source), _) if source == texture)));
        }
    }

    fn release_buffer(&mut self, buffer: &wgpu::Buffer) {
        for groups in self.groups.values_mut() {
            groups.retain(|bindings, _| !bindings.iter().any(|(_, binding)| matches!(binding, Bound::Buffer(source, _, _) if source == buffer)));
        }
    }
}

#[derive(Hash, PartialEq, Eq)]
pub(super) struct KernelKey {
    file: String,
    kernel: String,
    keywords: Vec<String>,
    formats: Vec<(String, String)>,
}

struct KernelLookup<'a> {
    file: &'a str,
    kernel: &'a str,
    keywords: &'a [String],
    formats: &'a [(usize, &'static str)],
    properties: &'a [(String, PropertyValue)],
}

impl std::hash::Hash for KernelLookup<'_> {
    fn hash<H: std::hash::Hasher>(&self, state: &mut H) {
        self.file.hash(state);
        self.kernel.hash(state);
        self.keywords.hash(state);
        self.formats.len().hash(state);
        for (index, format) in self.formats {
            (self.properties[*index].0.as_str(), *format).hash(state);
        }
    }
}

impl bevy::platform::collections::Equivalent<KernelKey> for KernelLookup<'_> {
    fn equivalent(&self, key: &KernelKey) -> bool {
        key.file == self.file
            && key.kernel == self.kernel
            && key.keywords.as_slice() == self.keywords
            && key.formats.len() == self.formats.len()
            && key
                .formats
                .iter()
                .zip(self.formats)
                .all(|((name, format), (index, expected))| {
                    name == &self.properties[*index].0 && format == expected
                })
    }
}

/// Compiled kernels by source, selected keywords and actual writable texture formats.
pub(super) type KernelCache =
    bevy::platform::collections::HashMap<KernelKey, Result<Arc<KernelGpu>, String>>;

/// naga's name of a texture format a kernel writes (its RW textures are compiled in it).
fn storage_format_name(format: wgpu::TextureFormat) -> Option<&'static str> {
    use wgpu::TextureFormat as F;
    Some(match format.remove_srgb_suffix() {
        F::R8Unorm => "R8Unorm",
        F::Rg8Unorm => "Rg8Unorm",
        F::R16Unorm => "R16Unorm",
        F::R16Float => "R16Float",
        F::R32Float => "R32Float",
        F::R32Uint => "R32Uint",
        F::R32Sint => "R32Sint",
        F::Rg16Unorm => "Rg16Unorm",
        F::Rg16Float => "Rg16Float",
        F::Rg32Float => "Rg32Float",
        F::Rgba8Unorm => "Rgba8Unorm",
        F::R8Snorm => "R8Snorm",
        F::Rg8Snorm => "Rg8Snorm",
        F::Rgba8Snorm => "Rgba8Snorm",
        F::Rgba16Unorm => "Rgba16Unorm",
        F::Rgba16Float => "Rgba16Float",
        F::Rgba32Float => "Rgba32Float",
        F::Rgb10a2Unorm => "Rgb10a2Unorm",
        F::Rg11b10Ufloat => "Rg11b10Ufloat",
        F::Rgba32Uint => "Rgba32Uint",
        F::Rgba32Sint => "Rgba32Sint",
        F::R8Uint => "R8Uint",
        F::R8Sint => "R8Sint",
        F::R16Uint => "R16Uint",
        F::R16Sint => "R16Sint",
        F::Rg8Uint => "Rg8Uint",
        F::Rg8Sint => "Rg8Sint",
        F::Rg16Uint => "Rg16Uint",
        F::Rg16Sint => "Rg16Sint",
        F::Rg32Uint => "Rg32Uint",
        F::Rg32Sint => "Rg32Sint",
        F::Rgba8Uint => "Rgba8Uint",
        F::Rgba8Sint => "Rgba8Sint",
        F::Rgba16Uint => "Rgba16Uint",
        F::Rgba16Sint => "Rgba16Sint",
        _ => return None,
    })
}

fn wgpu_storage_format(name: &str) -> wgpu::TextureFormat {
    use wgpu::TextureFormat as F;
    match name {
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
        "R8Snorm" => F::R8Snorm,
        "Rg8Snorm" => F::Rg8Snorm,
        "Rgba8Snorm" => F::Rgba8Snorm,
        "Rgba16Unorm" => F::Rgba16Unorm,
        "Rgba16Float" => F::Rgba16Float,
        "Rgba32Float" => F::Rgba32Float,
        "Rgba32Uint" => F::Rgba32Uint,
        "Rgba32Sint" => F::Rgba32Sint,
        "Rgb10a2Unorm" => F::Rgb10a2Unorm,
        "Rg11b10Ufloat" => F::Rg11b10Ufloat,
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
        _ => F::Rgba8Unorm,
    }
}

fn view_dimension(dim: TextureDim) -> wgpu::TextureViewDimension {
    super::super::compiled::view_dimension(dim)
}

/// Writes a uniform's value (floats as Unity stores them: matrices column-major) at its offset,
/// converted to its scalar type; arrays element by element at their stride.
fn write_uniform(u: &Uniform, value: &PropertyValue, out: &mut [u8]) {
    let mut put = |at: usize, v: f32, exact: Option<i32>| {
        if at + 4 > out.len() {
            return;
        }
        let bytes = match u.scalar {
            unbound_shaders::Scalar::Float => v.to_le_bytes(),
            unbound_shaders::Scalar::Int => exact.unwrap_or(v as i32).to_le_bytes(),
            unbound_shaders::Scalar::Uint => {
                (exact.map_or(v as i64, i64::from) as u32).to_le_bytes()
            }
        };
        out[at..at + 4].copy_from_slice(&bytes);
    };
    let (rows, columns) = (u.rows.max(1) as usize, u.columns.max(1) as usize);
    let element = |data: &[f32], base: usize, put: &mut dyn FnMut(usize, f32, Option<i32>)| {
        if columns > 1 {
            for c in 0..columns {
                for r in 0..rows {
                    if let Some(v) = data.get(c * 4 + r) {
                        put(base + c * 16 + r * 4, *v, None);
                    }
                }
            }
        } else {
            for r in 0..rows {
                if let Some(v) = data.get(r) {
                    put(base + r * 4, *v, None);
                }
            }
        }
    };
    let offset = u.offset as usize;
    match value {
        PropertyValue::Int(i) => put(offset, *i as f32, Some(*i)),
        PropertyValue::Float(f) => put(offset, *f, None),
        PropertyValue::Vector(v) => element(v, offset, &mut put),
        PropertyValue::Matrix(m) => element(m, offset, &mut put),
        // SetFloats/SetInts: consecutive values from the uniform's offset, as Unity copies them
        // into the constant buffer (array elements are 16 bytes apart there).
        PropertyValue::Array {
            components: 0,
            data,
        } => {
            for (i, v) in data.iter().enumerate() {
                put(offset + i * 4, *v, None);
            }
        }
        PropertyValue::Array { components, data } => {
            let n = (*components).max(1) as usize;
            let count = (u.count.max(1) as usize).min(data.len() / n);
            let stride = if u.count > 1 { u.stride as usize } else { 0 };
            for i in 0..count {
                element(&data[i * n..(i + 1) * n], offset + i * stride, &mut put);
            }
        }
        _ => {}
    }
}

impl UnityRenderer {
    /// A new zeroed buffer (replacing one of the same handle).
    pub(super) fn create_buffer(&mut self, handle: u64, size: u64, usage: u32) {
        self.release_buffer(handle);
        let mut flags = wgpu::BufferUsages::STORAGE
            | wgpu::BufferUsages::COPY_DST
            | wgpu::BufferUsages::COPY_SRC;
        if usage & buffer::VERTEX != 0 {
            flags |= wgpu::BufferUsages::VERTEX;
        }
        // Any buffer can be read as indices (RenderPrimitivesIndexed's documented example binds a
        // Structured one) or hold indirect arguments; the managed side checks what Unity allows.
        flags |= wgpu::BufferUsages::INDEX;
        flags |= wgpu::BufferUsages::INDIRECT;
        if usage & buffer::CONSTANT != 0 {
            flags |= wgpu::BufferUsages::UNIFORM;
        }
        let size = size.max(4).next_multiple_of(4);
        let buffer = self.device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("unbound buffer"),
            size,
            usage: flags,
            mapped_at_creation: false,
        });
        let shadow = (usage & buffer::CONSTANT != 0).then(|| vec![0u8; size as usize]);
        let counter = (usage & (buffer::APPEND | buffer::COUNTER) != 0).then(|| {
            self.device.create_buffer(&wgpu::BufferDescriptor {
                label: Some("unbound buffer counter"),
                size: 4,
                usage: wgpu::BufferUsages::STORAGE
                    | wgpu::BufferUsages::COPY_DST
                    | wgpu::BufferUsages::COPY_SRC,
                mapped_at_creation: false,
            })
        });
        self.buffers.insert(
            handle,
            GpuBuffer {
                revision: 0,
                buffer,
                size,
                shadow,
                counter,
                counter_used: false,
            },
        );
    }

    /// Sets an Append or Counter buffer's hidden counter (after what was queued before).
    pub(super) fn set_counter(&mut self, handle: u64, value: u32) {
        match self.buffers.get(&handle).map(|b| b.counter.as_ref()) {
            Some(Some(counter)) => self.queue.write_buffer(counter, 0, &value.to_le_bytes()),
            Some(None) => self.warn_once(format!("nocounter{handle}"), || {
                format!("buffer {handle} has no counter (not an Append or Counter buffer): its counter value is not set")
            }),
            None => self.warn_once(format!("buf{handle}"), || {
                format!("a counter value for unknown buffer {handle}: dropped")
            }),
        }
    }

    /// A copy of `size` bytes between buffers, or `None` (logged) when it cannot be made: the
    /// buffers must exist, the ranges fit and offsets be multiples of 4. A partial final word
    /// uses the padding present in both backing allocations.
    pub(super) fn plan_buffer_copy(
        &mut self,
        source: u64,
        source_offset: u64,
        destination: u64,
        destination_offset: u64,
        size: u64,
    ) -> Option<BufferCopyOp> {
        let (Some(s), Some(d)) = (self.buffers.get(&source), self.buffers.get(&destination)) else {
            self.warn_once(format!("copybuf{source}-{destination}"), || {
                format!("a copy between buffers {source} and {destination}, one of which does not exist: skipped")
            });
            return None;
        };
        if source == destination {
            // Unity copies a buffer onto itself as nothing; wgpu refuses the same buffer twice.
            if source_offset != destination_offset {
                self.warn_once(format!("copybufself{source}"), || {
                    format!("a copy within buffer {source} between different offsets is not supported: skipped")
                });
            }
            return None;
        }
        let Some(copy_size) = size.checked_add(3).map(|n| n & !3) else {
            self.warn_once(format!("copybufrange{source}-{destination}"), || {
                "a buffer copy size exceeds the supported range: skipped".into()
            });
            return None;
        };
        let fits = source_offset
            .checked_add(copy_size)
            .is_some_and(|e| e <= s.size)
            && destination_offset
                .checked_add(copy_size)
                .is_some_and(|e| e <= d.size);
        if !fits || (source_offset | destination_offset) % 4 != 0 {
            self.warn_once(format!("copybufrange{source}-{destination}"), || {
                format!("a copy of {size} bytes from buffer {source} at {source_offset} to buffer {destination} at {destination_offset} does not fit or is not 4-byte aligned: skipped")
            });
            return None;
        }
        Some(BufferCopyOp {
            source: s.buffer.clone(),
            source_offset,
            destination: d.buffer.clone(),
            destination_offset,
            size: copy_size,
        })
    }

    /// The copy of an Append or Counter buffer's counter into another buffer at byte `offset`:
    /// a buffer copy, or at an offset that is not a multiple of 4 (which Unity writes too) a
    /// one-thread pass that merges the counter's bytes into the two words it spans.
    pub(super) fn plan_counter_copy(
        &mut self,
        source: u64,
        destination: u64,
        offset: u32,
    ) -> Option<Op> {
        let counter = self
            .buffers
            .get(&source)
            .map(|b| (b.counter.clone(), b.counter_used));
        let counter = match counter {
            Some((Some(counter), true)) => counter,
            // A counter no dispatch has used yet: nothing is written, as in Unity.
            Some((Some(_), false)) => return None,
            _ => {
                self.warn_once(format!("nocounter{source}"), || {
                    format!("buffer {source} has no counter (not an Append or Counter buffer): its count is not copied")
                });
                return None;
            }
        };
        let Some(d) = self.buffers.get(&destination) else {
            self.warn_once(format!("buf{destination}"), || {
                format!("a count copied into unknown buffer {destination}: dropped")
            });
            return None;
        };
        let destination_buffer = d.buffer.clone();
        let offset = offset as u64;
        if offset + 4 > d.size {
            self.warn_once(format!("copycount{destination}"), || {
                format!("a count copied into buffer {destination} at byte {offset}, past its end: skipped")
            });
            return None;
        }
        if offset % 4 == 0 {
            return Some(Op::BufferCopy(BufferCopyOp {
                source: counter,
                source_offset: 0,
                destination: destination_buffer,
                destination_offset: offset,
                size: 4,
            }));
        }
        let (pipeline, layout) = self.unaligned_count_pipeline();
        let parameters = self
            .device
            .create_buffer_init(&wgpu::util::BufferInitDescriptor {
                label: Some("unbound unaligned count"),
                contents: &[offset as u32, 0, 0, 0].map(u32::to_le_bytes).concat(),
                usage: wgpu::BufferUsages::UNIFORM,
            });
        let bind_group = self.device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("unbound unaligned count"),
            layout: &layout,
            entries: &[
                wgpu::BindGroupEntry {
                    binding: 0,
                    resource: counter.as_entire_binding(),
                },
                wgpu::BindGroupEntry {
                    binding: 1,
                    resource: destination_buffer.as_entire_binding(),
                },
                wgpu::BindGroupEntry {
                    binding: 2,
                    resource: parameters.as_entire_binding(),
                },
            ],
        });
        Some(Op::Dispatch(DispatchOp {
            pipeline,
            bind_group,
            groups: [1, 1, 1],
            indirect: None,
        }))
    }

    fn unaligned_count_pipeline(&mut self) -> (wgpu::ComputePipeline, wgpu::BindGroupLayout) {
        if let Some((pipeline, layout)) = &self.unaligned_count {
            return (pipeline.clone(), layout.clone());
        }
        const SOURCE: &str = r#"
@group(0) @binding(0) var<storage, read> counter: array<u32>;
@group(0) @binding(1) var<storage, read_write> destination: array<u32>;
struct Parameters { offset: u32 }
@group(0) @binding(2) var<uniform> parameters: Parameters;
@compute @workgroup_size(1)
fn main() {
    let value = counter[0];
    let word = parameters.offset / 4u;
    let shift = (parameters.offset % 4u) * 8u;
    let low = (1u << shift) - 1u;
    destination[word] = (destination[word] & low) | (value << shift);
    destination[word + 1u] = (destination[word + 1u] & ~low) | (value >> (32u - shift));
}
"#;
        let storage = |binding, read_only| wgpu::BindGroupLayoutEntry {
            binding,
            visibility: wgpu::ShaderStages::COMPUTE,
            ty: wgpu::BindingType::Buffer {
                ty: wgpu::BufferBindingType::Storage { read_only },
                has_dynamic_offset: false,
                min_binding_size: None,
            },
            count: None,
        };
        let layout = self
            .device
            .create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
                label: Some("unbound unaligned count"),
                entries: &[
                    storage(0, true),
                    storage(1, false),
                    wgpu::BindGroupLayoutEntry {
                        binding: 2,
                        visibility: wgpu::ShaderStages::COMPUTE,
                        ty: wgpu::BindingType::Buffer {
                            ty: wgpu::BufferBindingType::Uniform,
                            has_dynamic_offset: false,
                            min_binding_size: None,
                        },
                        count: None,
                    },
                ],
            });
        let pipeline_layout = self
            .device
            .create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
                label: Some("unbound unaligned count"),
                bind_group_layouts: &[Some(&layout)],
                immediate_size: 0,
            });
        let module = self
            .device
            .create_shader_module(wgpu::ShaderModuleDescriptor {
                label: Some("unbound unaligned count"),
                source: wgpu::ShaderSource::Wgsl(SOURCE.into()),
            });
        let pipeline = self
            .device
            .create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
                label: Some("unbound unaligned count"),
                layout: Some(&pipeline_layout),
                module: &module,
                entry_point: Some("main"),
                compilation_options: Default::default(),
                cache: None,
            });
        self.unaligned_count = Some((pipeline.clone(), layout.clone()));
        (pipeline, layout)
    }

    pub(super) fn write_buffer(&mut self, handle: u64, offset: u64, data: &[u8]) {
        let Some(b) = self.buffers.get_mut(&handle) else {
            self.warn_once(format!("buf{handle}"), || {
                format!("data for unknown buffer {handle}: dropped")
            });
            return;
        };
        let end = (offset + data.len() as u64).min(b.size);
        if end <= offset {
            return;
        }
        // Writes go in 4-byte units: pad the tail with what the buffer cannot see anyway.
        let mut bytes = data[..(end - offset) as usize].to_vec();
        let aligned = (offset / 4) * 4;
        if aligned != offset {
            self.warn_once(format!("bufalign{handle}"), || {
                "a buffer write at an offset that is not a multiple of 4 bytes is not supported: dropped".into()
            });
            return;
        }
        bytes.resize(bytes.len().next_multiple_of(4), 0);
        if offset + bytes.len() as u64 > b.size {
            bytes.truncate((b.size - offset) as usize);
        }
        if let Some(shadow) = &mut b.shadow {
            let at = offset as usize;
            shadow[at..at + bytes.len()].copy_from_slice(&bytes);
        }
        b.revision = b.revision.wrapping_add(1);
        self.queue.write_buffer(&b.buffer, offset, &bytes);
    }

    pub(super) fn release_buffer(&mut self, handle: u64) {
        if let Some(buffer) = self.buffers.remove(&handle) {
            self.compute_bindings.release_buffer(&buffer.buffer);
            if let Some(counter) = buffer.counter {
                self.compute_bindings.release_buffer(&counter);
            }
        }
    }

    /// A buffer's bytes after everything queued.
    pub fn read_buffer(&mut self, handle: u64, offset: u64, size: u64) -> Option<Vec<u8>> {
        let b = self.buffers.get(&handle)?;
        let start = (offset / 4) * 4;
        let end = (offset + size).min(b.size).next_multiple_of(4).min(b.size);
        if end <= start {
            return Some(Vec::new());
        }
        let source = b.buffer.clone();
        self.read_gpu_data(
            end - start,
            "unbound read buffer",
            |encoder, staging| {
                encoder.copy_buffer_to_buffer(&source, start, staging, 0, end - start)
            },
            |data| {
                let skip = (offset - start) as usize;
                let take = (size as usize).min(data.len().saturating_sub(skip));
                Some(data[skip..skip + take].to_vec())
            },
        )
    }

    /// Marker callbacks track GPU completion without retaining one allocation per fence.
    pub fn graphics_fence(&mut self, insert: bool) -> Result<u64, String> {
        use std::sync::atomic::Ordering;
        if insert {
            self.fence_issued += 1;
            let sequence = self.fence_issued;
            let completed = self.fence_completed.clone();
            self.queue.submit([]);
            self.queue.on_submitted_work_done(move || {
                completed.fetch_max(sequence, Ordering::Release);
            });
            Ok(sequence)
        } else {
            self.device
                .poll(wgpu::PollType::Poll)
                .map_err(|e| e.to_string())?;
            Ok(self.fence_completed.load(Ordering::Acquire))
        }
    }

    /// A shader's passes under the active pipeline (see [`crate::materials::Resolver::describe`]).
    pub fn shader_passes(&mut self, shader: &str) -> String {
        self.resolver.describe(shader)
    }

    /// A kernel's thread group size.
    pub fn kernel_threads(
        &mut self,
        file: &str,
        kernel: &str,
        keywords: &[String],
    ) -> Result<[u32; 3], String> {
        Ok(self
            .kernel(file, kernel, keywords, &[], &[])?
            .kernel
            .workgroup_size)
    }

    fn kernel(
        &mut self,
        file: &str,
        kernel: &str,
        keywords: &[String],
        formats: &[(usize, &'static str)],
        properties: &[(String, PropertyValue)],
    ) -> Result<Arc<KernelGpu>, String> {
        let lookup = KernelLookup {
            file,
            kernel,
            keywords,
            formats,
            properties,
        };
        if let Some(found) = self.kernels.get(&lookup) {
            return found.clone();
        }
        let key = KernelKey {
            file: file.into(),
            kernel: kernel.into(),
            keywords: keywords.to_vec(),
            formats: formats
                .iter()
                .map(|(index, format)| (properties[*index].0.clone(), (*format).into()))
                .collect(),
        };
        let built = self.build_kernel(file, kernel, keywords, &key.formats);
        if let Err(e) = &built {
            tracing::error!("compute kernel `{kernel}` of {file} cannot run: {e}");
        }
        self.kernels.insert(key, built.clone());
        built
    }

    fn build_kernel(
        &mut self,
        file: &str,
        kernel: &str,
        keywords: &[String],
        formats: &[(String, String)],
    ) -> Result<Arc<KernelGpu>, String> {
        let source = std::fs::read_to_string(file).map_err(|e| format!("reading {file}: {e}"))?;
        let program = ComputeProgram {
            file: file.into(),
            source,
        };
        let compiler = self
            .compiled
            .compiler()
            .ok_or("shaders cannot be compiled on this machine")?;
        let compiled = compiler
            .compile_kernel(&program, kernel, keywords, formats)
            .map_err(|e| format!("{e:#}"))?;
        let mut entries: Vec<wgpu::BindGroupLayoutEntry> = Vec::new();
        let entry = |binding: u32, ty: wgpu::BindingType| wgpu::BindGroupLayoutEntry {
            binding,
            visibility: wgpu::ShaderStages::COMPUTE,
            ty,
            count: None,
        };
        for block in &compiled.blocks {
            entries.push(entry(
                block.binding,
                wgpu::BindingType::Buffer {
                    ty: wgpu::BufferBindingType::Uniform,
                    has_dynamic_offset: false,
                    min_binding_size: None,
                },
            ));
        }
        for r in &compiled.resources {
            let ty = match &r.kind {
                ResourceKind::Buffer { read_only } => wgpu::BindingType::Buffer {
                    ty: wgpu::BufferBindingType::Storage {
                        read_only: *read_only,
                    },
                    has_dynamic_offset: false,
                    min_binding_size: None,
                },
                ResourceKind::Texture { dim, sample } => wgpu::BindingType::Texture {
                    sample_type: match sample.as_str() {
                        "sint" => wgpu::TextureSampleType::Sint,
                        "uint" => wgpu::TextureSampleType::Uint,
                        "depth" => wgpu::TextureSampleType::Depth,
                        _ => wgpu::TextureSampleType::Float { filterable: true },
                    },
                    view_dimension: view_dimension(*dim),
                    multisampled: false,
                },
                ResourceKind::StorageTexture {
                    dim,
                    format,
                    write_only,
                } => wgpu::BindingType::StorageTexture {
                    access: if *write_only {
                        wgpu::StorageTextureAccess::WriteOnly
                    } else {
                        wgpu::StorageTextureAccess::ReadWrite
                    },
                    format: wgpu_storage_format(format),
                    view_dimension: view_dimension(*dim),
                },
                ResourceKind::Sampler {
                    comparison,
                    unfiltered,
                    ..
                } => wgpu::BindingType::Sampler(if *comparison {
                    wgpu::SamplerBindingType::Comparison
                } else if *unfiltered {
                    wgpu::SamplerBindingType::NonFiltering
                } else {
                    wgpu::SamplerBindingType::Filtering
                }),
            };
            entries.push(entry(r.binding, ty));
        }
        entries.sort_by_key(|e| e.binding);
        let layout = self
            .device
            .create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
                label: Some("unbound kernel"),
                entries: &entries,
            });
        let pipeline_layout = self
            .device
            .create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
                label: Some("unbound kernel"),
                bind_group_layouts: &[Some(&layout)],
                immediate_size: 0,
            });
        let module = self
            .device
            .create_shader_module(wgpu::ShaderModuleDescriptor {
                label: Some(kernel),
                source: wgpu::ShaderSource::Wgsl(compiled.wgsl.clone().into()),
            });
        let pipeline = self
            .device
            .create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
                label: Some(kernel),
                layout: Some(&pipeline_layout),
                module: &module,
                entry_point: Some(&compiled.entry),
                compilation_options: Default::default(),
                cache: None,
            });
        tracing::info!(
            "compute kernel `{kernel}` of {file}: compiled ({})",
            if compiled.keywords.is_empty() {
                "no keywords".to_string()
            } else {
                compiled.keywords.join(" ")
            }
        );
        let sampler_defaults = compiled
            .resources
            .iter()
            .map(|resource| match &resource.kind {
                ResourceKind::Sampler { comparison, .. } => {
                    // The state its name spells, for a sampler that is no texture's; a
                    // wrongly spelt one is logged (what the Editor does with it in a kernel
                    // is unmeasured) and samples as the default.
                    let key = match unbound_shaders::InlineSampler::parse(&resource.name) {
                        Ok(Some(state)) => super::SamplerKey::inline(state),
                        Ok(None) => super::SamplerKey::LINEAR_CLAMP,
                        Err(wrong) => {
                            tracing::error!(
                                "compute kernel `{kernel}` of {file}: {wrong}; the sampler is bilinear and clamped unless a texture's state replaces it"
                            );
                            super::SamplerKey::LINEAR_CLAMP
                        }
                    };
                    Some(super::SamplerKey {
                        compare: if *comparison { 4 } else { 0 },
                        ..key
                    })
                }
                _ => None,
            })
            .collect();
        let sampler_alternates = compiled
            .resources
            .iter()
            .map(|resource| match &resource.kind {
                ResourceKind::Sampler {
                    texture: Some(name),
                    ..
                } => Some(format!("_{name}")),
                _ => None,
            })
            .collect();
        let constant_names = compiled
            .blocks
            .iter()
            .map(|block| {
                (
                    format!("{}{}", block.name, CONSTANT_OFFSET_SUFFIX),
                    format!("{}@size", block.name),
                )
            })
            .collect();
        Ok(Arc::new(KernelGpu {
            sampler_defaults,
            sampler_alternates,
            constant_names,
            kernel: compiled,
            layout,
            pipeline,
        }))
    }

    /// A property of a dispatch: its own, else the global one.
    fn dispatch_value<'a>(
        properties: &'a [(String, PropertyValue)],
        globals: &'a [(String, PropertyValue)],
        name: &str,
    ) -> Option<&'a PropertyValue> {
        properties
            .iter()
            .chain(globals.iter())
            .find(|(n, _)| n == name)
            .map(|(_, v)| v)
    }

    /// The pipeline and bindings of a dispatch, or `None` (logged) when it cannot run.
    pub(super) fn plan_dispatch(&mut self, d: &commands::Dispatch) -> Option<Op> {
        // RW textures compile in the formats of the textures bound to them.
        let mut formats = std::mem::take(&mut self.compute_bindings.formats);
        formats.clear();
        for (index, (_, value)) in d.properties.iter().enumerate() {
            if let PropertyValue::Image { handle, .. } = value
                && let Some(t) = self.textures.get(handle)
                && let Some(f) = storage_format_name(t.texture.format())
            {
                formats.push((index, f));
            }
        }
        formats.sort_unstable_by(|(a, af), (b, bf)| {
            (&d.properties[*a].0, af).cmp(&(&d.properties[*b].0, bf))
        });
        let gpu = self.kernel(&d.file, &d.kernel, &d.keywords, &formats, &d.properties);
        self.compute_bindings.formats = formats;
        let gpu = gpu.ok()?;
        let space = self.color_space;
        let mut bound = std::mem::take(&mut self.compute_bindings.scratch);
        bound.clear();
        let result = (|| {
            for (block, (offset_name, size_name)) in
                gpu.kernel.blocks.iter().zip(&gpu.constant_names)
            {
                // Bind the original GPU buffer so earlier GPU writes remain ordered and visible.
                if let Some(PropertyValue::Buffer(handle)) =
                    Self::dispatch_value(&d.properties, &self.globals, &block.name)
                {
                    let integer =
                        |name: &str| match Self::dispatch_value(&d.properties, &self.globals, name)
                        {
                            Some(PropertyValue::Int(value)) => (*value).max(0) as u64,
                            _ => 0,
                        };
                    let offset = integer(offset_name);
                    let size = integer(size_name);
                    let valid = self.buffers.get(handle).filter(|b| {
                        b.buffer.usage().contains(wgpu::BufferUsages::UNIFORM)
                            && offset.is_multiple_of(
                                self.device.limits().min_uniform_buffer_offset_alignment as u64,
                            )
                            && size >= block.size.max(16) as u64
                            && offset.checked_add(size).is_some_and(|end| end <= b.size)
                    });
                    let Some(buffer) = valid else {
                        self.warn_once(format!("kconstant{}{}", d.kernel, block.name), || format!("kernel `{}`: constant buffer `{}` is missing, too small, unaligned or lacks Constant usage: the dispatch is skipped", d.kernel, block.name));
                        return None;
                    };
                    bound.push((
                        block.binding,
                        Bound::Buffer(buffer.buffer.clone(), offset, wgpu::BufferSize::new(size)),
                    ));
                    continue;
                }
            }
            for (resource_index, r) in gpu.kernel.resources.iter().enumerate() {
                let value = Self::dispatch_value(&d.properties, &self.globals, &r.name);
                match &r.kind {
                    ResourceKind::Buffer { .. } if r.name.ends_with(COUNTER_SUFFIX) => {
                        // The hidden counter of the buffer bound to the name before the suffix.
                        let owner = &r.name[..r.name.len() - COUNTER_SUFFIX.len()];
                        let bound_to =
                            match Self::dispatch_value(&d.properties, &self.globals, owner) {
                                Some(PropertyValue::Buffer(h)) => Some(*h),
                                _ => None,
                            };
                        let counter =
                            bound_to
                                .and_then(|h| self.buffers.get_mut(&h))
                                .and_then(|b| {
                                    b.counter_used |= b.counter.is_some();
                                    b.counter.clone()
                                });
                        let Some(counter) = counter else {
                            self.warn_once(format!("kcount{}{owner}", d.kernel), || {
                            format!(
                                "kernel `{}` uses the counter of buffer `{owner}`, which is not set or not an Append or Counter buffer: the dispatch is skipped",
                                d.kernel
                            )
                        });
                            return None;
                        };
                        bound.push((r.binding, Bound::Buffer(counter, 0, None)));
                    }
                    ResourceKind::Buffer { read_only } => {
                        let Some(b) = (match value {
                            Some(PropertyValue::Buffer(h)) => self.buffers.get_mut(h),
                            _ => None,
                        }) else {
                            self.warn_once(format!("kbuf{}{}", d.kernel, r.name), || {
                                format!(
                                    "kernel `{}`: buffer `{}` is not set: the dispatch is skipped",
                                    d.kernel, r.name
                                )
                            });
                            return None;
                        };
                        if !read_only {
                            b.revision = b.revision.wrapping_add(1);
                        }
                        bound.push((r.binding, Bound::Buffer(b.buffer.clone(), 0, None)));
                    }
                    ResourceKind::Texture { dim, sample } => {
                        let handle = match value {
                            Some(PropertyValue::Texture { handle, .. }) => *handle,
                            Some(PropertyValue::Image { handle, .. }) => *handle,
                            _ => 0,
                        };
                        // A depth slot (compared, or read as depth): the texture's depth buffer when
                        // its shape is the slot's, else a far-plane default, as graphics programs
                        // read one (the float copy of a depth target is not a depth texture).
                        if sample == "depth" {
                            let cube_slot = *dim == TextureDim::Cube;
                            let depth = self
                                .textures
                                .get(&handle)
                                .filter(|t| texture_dimension(t) == *dim)
                                .and_then(|t| Some((t.texture.clone(), t.depth_sample.clone()?)));
                            let (source, view) = match depth {
                                Some((texture, view)) => (Some(texture), view),
                                // The far-plane default is 2D or a cube: an array slot without a
                                // depth texture of its shape is skipped.
                                None if !matches!(*dim, TextureDim::D2 | TextureDim::Cube) => {
                                    self.warn_once(format!("kdeptharray{}{}", d.kernel, r.name), || format!("kernel `{}`: depth texture `{}` ({dim:?}) has no depth texture of its shape: the dispatch is skipped", d.kernel, r.name));
                                    return None;
                                }
                                None => {
                                    if handle != 0 {
                                        self.warn_once(format!("kdepth{}{}{handle}", d.kernel, r.name), || format!("kernel `{}`: depth texture `{}` is given render texture {handle}, which has no depth buffer of the slot's shape: it reads the far plane", d.kernel, r.name));
                                    }
                                    (None, self.depth_default(cube_slot))
                                }
                            };
                            bound.push((r.binding, Bound::View(source, view)));
                            continue;
                        }
                        // Integer textures are read by 2D integer slots of their signedness; any
                        // other pairing with an integer slot or texture reads the slot's default.
                        let wanted = match sample.as_str() {
                            "sint" => Some(wgpu::TextureSampleType::Sint),
                            "uint" => Some(wgpu::TextureSampleType::Uint),
                            _ => None,
                        };
                        let has = self
                            .textures
                            .get(&handle)
                            .and_then(|t| t.texture.format().sample_type(None, None))
                            .filter(|t| {
                                matches!(
                                    t,
                                    wgpu::TextureSampleType::Sint | wgpu::TextureSampleType::Uint
                                )
                            });
                        let actual = self
                            .textures
                            .get(&handle)
                            .filter(|t| texture_dimension(t) == *dim);
                        let (source, view) = if let Some(texture) = actual.filter(|_| wanted == has)
                        {
                            (
                                Some(texture.texture.clone()),
                                self.sampled_texture_view(texture, space),
                            )
                        } else {
                            if handle != 0 && (wanted.is_some() || has.is_some()) {
                                self.warn_once(format!("kint{}{}{handle}", d.kernel, r.name), || format!("kernel `{}`: texture `{}` ({sample}) cannot read render texture {handle} of another texel type: it reads its default", d.kernel, r.name));
                            }
                            let view = match wanted {
                                Some(w) if *dim == TextureDim::D2 => {
                                    self.integer_default(w == wgpu::TextureSampleType::Sint)
                                }
                                Some(_) => {
                                    self.warn_once(format!("kintdim{}{}", d.kernel, r.name), || format!("kernel `{}`: missing or incompatible integer texture `{}` ({dim:?}): the dispatch is skipped", d.kernel, r.name));
                                    return None;
                                }
                                None if *dim == TextureDim::D2 => {
                                    self.texture_or_default(0, "white").view_for(space).clone()
                                }
                                None => self.dim_default(*dim, "white"),
                            };
                            (None, view)
                        };
                        bound.push((r.binding, Bound::View(source, view)));
                    }
                    ResourceKind::StorageTexture { dim, .. } => {
                        let (handle, mip) = match value {
                            Some(PropertyValue::Image { handle, mip }) => (*handle, *mip),
                            Some(PropertyValue::Texture { handle, .. }) => (*handle, 0),
                            _ => (0, 0),
                        };
                        let Some(t) = self.textures.get(&handle) else {
                            self.warn_once(format!("ktex{}{}", d.kernel, r.name), || {
                            format!(
                                "kernel `{}`: RW texture `{}` is not set: the dispatch is skipped",
                                d.kernel, r.name
                            )
                        });
                            return None;
                        };
                        if !t.random_write {
                            self.warn_once(format!("kuav{handle}"), || {
                            format!(
                                "kernel `{}` writes texture `{}`, which is not a random-write target (RenderTexture.enableRandomWrite): skipped",
                                d.kernel, r.name
                            )
                        });
                            return None;
                        }
                        let dimension = if t.volume {
                            TextureDim::D3
                        } else if t.layers > 1 {
                            TextureDim::D2Array
                        } else {
                            TextureDim::D2
                        };
                        if mip < 0 || dimension != *dim || mip as usize >= t.storage_views.len() {
                            self.warn_once(format!("kuavdim{handle}/{mip}/{dim:?}"), || format!("kernel `{}`: RW texture `{}` has an incompatible dimension or mip {mip}: the dispatch is skipped", d.kernel, r.name));
                            return None;
                        }
                        bound.push((
                            r.binding,
                            Bound::View(
                                Some(t.texture.clone()),
                                t.storage_views[mip as usize].clone(),
                            ),
                        ));
                    }
                    ResourceKind::Sampler {
                        texture,
                        comparison,
                        unfiltered,
                    } => {
                        let texture = texture.as_ref().and_then(|t| {
                            match Self::dispatch_value(&d.properties, &self.globals, t).or_else(
                                || {
                                    gpu.sampler_alternates[resource_index].as_ref().and_then(
                                        |name| {
                                            Self::dispatch_value(&d.properties, &self.globals, name)
                                        },
                                    )
                                },
                            ) {
                                Some(
                                    PropertyValue::Texture { handle, .. }
                                    | PropertyValue::Image { handle, .. },
                                ) => self.textures.get(handle),
                                _ => None,
                            }
                        });
                        let source = texture.map(|texture| texture.texture.clone());
                        let key = texture
                            .map(|texture| texture.sampler_key)
                            .unwrap_or_else(|| {
                                gpu.sampler_defaults[resource_index].expect("sampler fallback")
                            });
                        // A comparison sampler compares less-or-equal through the texture's state,
                        // as a graphics program's does (mirrored with a reversed depth buffer).
                        let key = if *comparison {
                            super::SamplerKey { compare: 4, ..key }
                        } else {
                            key
                        };
                        // One sampling an integer texture reads texels unfiltered.
                        let key = if *unfiltered {
                            super::SamplerKey {
                                filter: 0,
                                aniso: 1,
                                ..key
                            }
                        } else {
                            key
                        };
                        bound.push((r.binding, Bound::Sampler(source, key)));
                    }
                }
            }
            bound.sort_by_key(|(binding, _)| *binding);
            let bind_group = self.compute_bindings.group(
                &self.device,
                &self.queue,
                &gpu,
                &bound,
                &d.properties,
                &self.globals,
                self.conventions.reversed_z,
            );
            let indirect = match d.indirect {
                None => None,
                Some((args, offset)) => {
                    let fits = self
                        .buffers
                        .get(&args)
                        .map(|b| (b.buffer.clone(), offset as u64 + 12 <= b.size));
                    match fits {
                        Some((buffer, true)) if offset % 4 == 0 => Some((buffer, offset as u64)),
                        _ => {
                            self.warn_once(format!("kargs{args}"), || {
                            format!(
                                "kernel `{}`: the indirect arguments at byte {offset} of buffer {args} do not exist, fit or align to 4 bytes: the dispatch is skipped",
                                d.kernel
                            )
                        });
                            return None;
                        }
                    }
                }
            };
            Some(Op::Dispatch(DispatchOp {
                pipeline: gpu.pipeline.clone(),
                bind_group,
                groups: d.groups,
                indirect,
            }))
        })();
        self.compute_bindings.scratch = bound;
        result
    }
}

fn texture_dimension(texture: &super::super::resources::GpuTexture) -> TextureDim {
    if texture.volume {
        TextureDim::D3
    } else if texture.cube_array {
        TextureDim::CubeArray
    } else if texture.cube {
        TextureDim::Cube
    } else if texture.layers > 1 {
        TextureDim::D2Array
    } else {
        TextureDim::D2
    }
}

/// Encodes a planned dispatch.
pub(super) fn encode(encoder: &mut wgpu::CommandEncoder, d: &DispatchOp) {
    let mut pass = encoder.begin_compute_pass(&wgpu::ComputePassDescriptor {
        label: Some("unbound dispatch"),
        timestamp_writes: None,
    });
    pass.set_pipeline(&d.pipeline);
    pass.set_bind_group(0, &d.bind_group, &[]);
    match &d.indirect {
        Some((buffer, offset)) => pass.dispatch_workgroups_indirect(buffer, *offset),
        None => {
            let [x, y, z] = d.groups;
            pass.dispatch_workgroups(x, y, z);
        }
    }
}

/// Encodes a planned buffer copy.
pub(super) fn encode_copy(encoder: &mut wgpu::CommandEncoder, c: &BufferCopyOp) {
    encoder.copy_buffer_to_buffer(
        &c.source,
        c.source_offset,
        &c.destination,
        c.destination_offset,
        c.size,
    );
}

#[cfg(test)]
mod tests {

    #[test]
    fn dispatch_slots_reuse_resources_when_order_rotates() {
        let mut pools: HashMap<usize, ReuseSlots<usize>> = HashMap::new();
        let mut allocations = 0;
        for frame in 1..=256 {
            for position in 0..256 {
                let resource = (position + frame as usize) % 256;
                let slot = pools
                    .entry(resource)
                    .or_default()
                    .claim(frame, frame, true, || {
                        allocations += 1;
                        resource
                    });
                assert_eq!(*slot, resource);
            }
        }
        assert_eq!(allocations, 256);
        assert_eq!(
            pools.values().map(|pool| pool.slots.len()).sum::<usize>(),
            256
        );
    }

    #[test]
    fn dispatch_slots_preserve_submission_contents_and_retire_idle_slots() {
        let mut pool = ReuseSlots::default();
        for index in 0..4 {
            assert_eq!(*pool.claim(1, 1, true, || index), index);
        }
        assert_eq!(*pool.claim(2, 2, true, || 99), 0);
        assert_eq!(*pool.claim(2, 2, false, || 99), 0);
        assert!(pool.retire(2));
        assert_eq!(pool.slots.len(), 1);
        assert!(!pool.retire(3));
    }

    #[test]
    fn dispatch_slots_bound_rotating_repetition_history() {
        let mut pools: HashMap<usize, ReuseSlots<()>> = HashMap::new();
        for frame in 1u64..=256 {
            let resource = frame as usize % 64;
            for _ in 0..64 {
                pools
                    .entry(resource)
                    .or_default()
                    .claim(frame, frame, true, || ());
            }
            let cutoff = frame.saturating_sub(BINDING_IDLE_FRAMES - 1);
            pools.retain(|_, pool| pool.retire(cutoff));
            assert!(
                pools.values().map(|pool| pool.slots.len()).sum::<usize>()
                    <= BINDING_IDLE_FRAMES as usize * 64
            );
        }
    }

    #[test]
    fn kernel_lookup_uses_names_and_formats_independently_of_property_order() {
        let mut cache = bevy::platform::collections::HashMap::new();
        cache.insert(
            KernelKey {
                file: "volume.compute".into(),
                kernel: "Fill".into(),
                keywords: vec!["QUALITY".into()],
                formats: vec![("Target".into(), "R32Float".into())],
            },
            17,
        );
        let keywords = vec!["QUALITY".into()];
        let first = vec![
            ("Bias".into(), PropertyValue::Float(1.0)),
            ("Target".into(), PropertyValue::Image { handle: 3, mip: 0 }),
        ];
        let second = vec![
            ("Target".into(), PropertyValue::Image { handle: 9, mip: 1 }),
            ("Bias".into(), PropertyValue::Float(2.0)),
        ];
        for (properties, index) in [(&first, 1), (&second, 0)] {
            let formats = [(index, "R32Float")];
            let lookup = KernelLookup {
                file: "volume.compute",
                kernel: "Fill",
                keywords: &keywords,
                formats: &formats,
                properties,
            };
            assert_eq!(cache.get(&lookup), Some(&17));
            let changed = [(index, "Rgba32Float")];
            assert_eq!(
                cache.get(&KernelLookup {
                    formats: &changed,
                    ..lookup
                }),
                None
            );
        }
    }
    use super::*;

    /// A kernel's RW texture of a normalized 16-bit format binds in that format, not RGBA8, so the
    /// bind group matches the texture the renderer stores.
    #[test]
    fn normalized_16_bit_storage_formats_round_trip() {
        use wgpu::TextureFormat as F;
        for (format, name) in [
            (F::R16Unorm, "R16Unorm"),
            (F::Rg16Unorm, "Rg16Unorm"),
            (F::Rgba16Unorm, "Rgba16Unorm"),
        ] {
            assert_eq!(storage_format_name(format), Some(name));
            assert_eq!(wgpu_storage_format(name), format);
        }
        // Every named format binds as itself.
        for format in [
            F::R8Unorm,
            F::Rg8Unorm,
            F::R16Float,
            F::R32Float,
            F::R32Uint,
            F::R32Sint,
            F::Rg16Float,
            F::Rg32Float,
            F::Rgba8Unorm,
            F::Rgba16Float,
            F::Rgba32Float,
            F::Rgb10a2Unorm,
            F::Rg11b10Ufloat,
        ] {
            let name = storage_format_name(format).unwrap();
            assert_eq!(wgpu_storage_format(name), format, "{name}");
        }
    }
}
