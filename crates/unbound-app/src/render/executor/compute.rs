//! Buffers and compute dispatches: GraphicsBuffer/ComputeBuffer storage, kernels compiled from
//! their `.compute` files (unbound-shaders), and each dispatch's bindings found by name among its
//! own properties, then the global ones.
use super::{Op, UnityRenderer};
use std::{collections::HashMap, sync::Arc};
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
    pipeline: wgpu::ComputePipeline,
    bind_group: wgpu::BindGroup,
    groups: [u32; 3],
    /// Group counts read on the GPU: the arguments buffer and a byte offset.
    indirect: Option<(wgpu::Buffer, u64)>,
}

/// A copy between buffers (or from a buffer's counter), encoded in order with the passes.
pub struct BufferCopyOp {
    source: wgpu::Buffer,
    source_offset: u64,
    destination: wgpu::Buffer,
    destination_offset: u64,
    size: u64,
}

/// The suffix glslang gives the block holding a buffer's hidden counter.
const COUNTER_SUFFIX: &str = "@count";

pub struct KernelGpu {
    kernel: Arc<CompiledKernel>,
    layout: wgpu::BindGroupLayout,
    pipeline: wgpu::ComputePipeline,
}

/// Compiled kernels by (file, kernel, keywords, RW texture formats); failures keep their reason.
pub type KernelCache =
    HashMap<(String, String, Vec<String>, Vec<(String, String)>), Result<Arc<KernelGpu>, String>>;

/// naga's name of a texture format a kernel writes (its RW textures are compiled in it).
fn storage_format_name(format: wgpu::TextureFormat) -> Option<&'static str> {
    use wgpu::TextureFormat as F;
    Some(match format.remove_srgb_suffix() {
        F::R8Unorm => "R8Unorm",
        F::R16Float => "R16Float",
        F::R32Float => "R32Float",
        F::R32Uint => "R32Uint",
        F::R32Sint => "R32Sint",
        F::Rg16Float => "Rg16Float",
        F::Rg32Float => "Rg32Float",
        F::Rgba8Unorm => "Rgba8Unorm",
        F::Rgba16Float => "Rgba16Float",
        F::Rgba32Float => "Rgba32Float",
        F::Rgb10a2Unorm => "Rgb10a2Unorm",
        F::Rg11b10Ufloat => "Rg11b10Ufloat",
        _ => return None,
    })
}

fn wgpu_storage_format(name: &str) -> wgpu::TextureFormat {
    use wgpu::TextureFormat as F;
    match name {
        "R8Unorm" => F::R8Unorm,
        "R16Float" => F::R16Float,
        "R32Float" => F::R32Float,
        "R32Uint" => F::R32Uint,
        "R32Sint" => F::R32Sint,
        "Rg16Float" => F::Rg16Float,
        "Rg32Float" => F::Rg32Float,
        "Rgba16Float" => F::Rgba16Float,
        "Rgba32Float" => F::Rgba32Float,
        "Rgba32Uint" => F::Rgba32Uint,
        "Rgba32Sint" => F::Rgba32Sint,
        "Rgb10a2Unorm" => F::Rgb10a2Unorm,
        "Rg11b10Ufloat" => F::Rg11b10Ufloat,
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
        let mut flags = wgpu::BufferUsages::STORAGE
            | wgpu::BufferUsages::COPY_DST
            | wgpu::BufferUsages::COPY_SRC;
        if usage & buffer::VERTEX != 0 {
            flags |= wgpu::BufferUsages::VERTEX;
        }
        if usage & buffer::INDEX != 0 {
            flags |= wgpu::BufferUsages::INDEX;
        }
        // Any buffer can hold indirect arguments; the managed side checks what Unity allows.
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
        self.queue.write_buffer(&b.buffer, offset, &bytes);
    }

    pub(super) fn release_buffer(&mut self, handle: u64) {
        self.buffers.remove(&handle);
    }

    /// A buffer's bytes after everything queued.
    pub fn read_buffer(&mut self, handle: u64, offset: u64, size: u64) -> Option<Vec<u8>> {
        let b = self.buffers.get(&handle)?;
        let start = (offset / 4) * 4;
        let end = (offset + size).min(b.size).next_multiple_of(4).min(b.size);
        if end <= start {
            return Some(Vec::new());
        }
        let staging = self.device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("unbound read buffer"),
            size: end - start,
            usage: wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        });
        let mut encoder = self
            .device
            .create_command_encoder(&wgpu::CommandEncoderDescriptor {
                label: Some("unbound read buffer"),
            });
        encoder.copy_buffer_to_buffer(&b.buffer, start, &staging, 0, end - start);
        let index = self.queue.submit([encoder.finish()]);
        let slice = staging.slice(..);
        slice.map_async(wgpu::MapMode::Read, |_| {});
        if let Err(e) = self.device.poll(wgpu::PollType::Wait {
            submission_index: Some(index),
            timeout: None,
        }) {
            tracing::warn!("reading a buffer back from the GPU failed: {e}");
            return None;
        }
        let data = slice.get_mapped_range().ok()?;
        let skip = (offset - start) as usize;
        let take = (size as usize).min(data.len().saturating_sub(skip));
        Some(data[skip..skip + take].to_vec())
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
            .kernel(file, kernel, keywords, Vec::new())?
            .kernel
            .workgroup_size)
    }

    fn kernel(
        &mut self,
        file: &str,
        kernel: &str,
        keywords: &[String],
        formats: Vec<(String, String)>,
    ) -> Result<Arc<KernelGpu>, String> {
        let key = (
            file.to_string(),
            kernel.to_string(),
            keywords.to_vec(),
            formats.clone(),
        );
        if let Some(found) = self.kernels.get(&key) {
            return found.clone();
        }
        let built = self.build_kernel(file, kernel, keywords, &formats);
        if let Err(e) = &built {
            tracing::warn!("compute kernel `{kernel}` of {file} cannot run: {e}");
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
                ResourceKind::Sampler { comparison, .. } => {
                    wgpu::BindingType::Sampler(if *comparison {
                        wgpu::SamplerBindingType::Comparison
                    } else {
                        wgpu::SamplerBindingType::Filtering
                    })
                }
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
        Ok(Arc::new(KernelGpu {
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
        let globals = self.globals.clone();
        // RW textures compile in the formats of the textures bound to them.
        let mut formats = Vec::new();
        for (name, value) in &d.properties {
            if let PropertyValue::Image { handle, .. } = value
                && let Some(t) = self.textures.get(handle)
                && let Some(f) = storage_format_name(t.texture.format())
            {
                formats.push((name.clone(), f.to_string()));
            }
        }
        formats.sort();
        let gpu = self.kernel(&d.file, &d.kernel, &d.keywords, formats).ok()?;
        let space = self.color_space;
        let mut uniform_buffers = Vec::new();
        for block in &gpu.kernel.blocks {
            let mut data = vec![0u8; block.size.max(16) as usize];
            for u in &block.uniforms {
                if let Some(v) = Self::dispatch_value(&d.properties, &globals, &u.name) {
                    write_uniform(u, v, &mut data);
                }
            }
            let buffer = self
                .device
                .create_buffer_init(&wgpu::util::BufferInitDescriptor {
                    label: Some("unbound kernel uniforms"),
                    contents: &data,
                    usage: wgpu::BufferUsages::UNIFORM,
                });
            uniform_buffers.push((block.binding, buffer));
        }
        enum Bound {
            Buffer(wgpu::Buffer),
            View(wgpu::TextureView),
            Sampler(wgpu::Sampler),
        }
        let mut bound: Vec<(u32, Bound)> = Vec::new();
        for r in &gpu.kernel.resources {
            let value = Self::dispatch_value(&d.properties, &globals, &r.name);
            match &r.kind {
                ResourceKind::Buffer { .. } if r.name.ends_with(COUNTER_SUFFIX) => {
                    // The hidden counter of the buffer bound to the name before the suffix.
                    let owner = &r.name[..r.name.len() - COUNTER_SUFFIX.len()];
                    let bound_to = match Self::dispatch_value(&d.properties, &globals, owner) {
                        Some(PropertyValue::Buffer(h)) => Some(*h),
                        _ => None,
                    };
                    let counter = bound_to
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
                    bound.push((r.binding, Bound::Buffer(counter)));
                }
                ResourceKind::Buffer { .. } => {
                    let Some(b) = (match value {
                        Some(PropertyValue::Buffer(h)) => self.buffers.get(h),
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
                    bound.push((r.binding, Bound::Buffer(b.buffer.clone())));
                }
                ResourceKind::Texture { dim, .. } => {
                    let handle = match value {
                        Some(PropertyValue::Texture { handle, .. }) => *handle,
                        Some(PropertyValue::Image { handle, .. }) => *handle,
                        _ => 0,
                    };
                    let view = if *dim == TextureDim::D2 {
                        self.texture_or_default(handle, "white")
                            .view_for(space)
                            .clone()
                    } else {
                        self.dim_default(*dim, "white")
                    };
                    bound.push((r.binding, Bound::View(view)));
                }
                ResourceKind::StorageTexture { .. } => {
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
                    let view = t.texture.create_view(&wgpu::TextureViewDescriptor {
                        format: Some(t.texture.format().remove_srgb_suffix()),
                        base_mip_level: mip.max(0) as u32,
                        mip_level_count: Some(1),
                        ..Default::default()
                    });
                    bound.push((r.binding, Bound::View(view)));
                }
                ResourceKind::Sampler { texture, .. } => {
                    let key = texture
                        .as_ref()
                        .and_then(|t| {
                            match Self::dispatch_value(&d.properties, &globals, t).or_else(|| {
                                Self::dispatch_value(&d.properties, &globals, &format!("_{t}"))
                            }) {
                                Some(PropertyValue::Texture { handle, .. }) => {
                                    self.textures.get(handle)
                                }
                                _ => None,
                            }
                        })
                        .map(|t| t.sampler_key)
                        .unwrap_or_else(|| inline_sampler(&r.name));
                    bound.push((r.binding, Bound::Sampler(self.sampler(key))));
                }
            }
        }
        let mut entries: Vec<wgpu::BindGroupEntry> = Vec::new();
        for (binding, buffer) in &uniform_buffers {
            entries.push(wgpu::BindGroupEntry {
                binding: *binding,
                resource: buffer.as_entire_binding(),
            });
        }
        for (binding, b) in &bound {
            entries.push(wgpu::BindGroupEntry {
                binding: *binding,
                resource: match b {
                    Bound::Buffer(buffer) => buffer.as_entire_binding(),
                    Bound::View(view) => wgpu::BindingResource::TextureView(view),
                    Bound::Sampler(s) => wgpu::BindingResource::Sampler(s),
                },
            });
        }
        entries.sort_by_key(|e| e.binding);
        let bind_group = self.device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("unbound kernel"),
            layout: &gpu.layout,
            entries: &entries,
        });
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
    }
}

/// Unity's inline sampler states: the sampler's name spells its filter and wrap modes
/// (`sampler_linear_clamp`, `my_point_repeat_sampler`).
fn inline_sampler(name: &str) -> super::SamplerKey {
    let n = name.to_ascii_lowercase();
    let filter = if n.contains("trilinear") {
        2
    } else if n.contains("point") {
        0
    } else {
        1
    };
    let wrap = if n.contains("mirroronce") {
        3
    } else if n.contains("mirror") {
        2
    } else if n.contains("repeat") {
        0
    } else {
        1
    };
    super::SamplerKey {
        filter,
        wrap_u: wrap,
        wrap_v: wrap,
        wrap_w: 0,
        aniso: 1,
        compare: if n.contains("compare") { 4 } else { 0 },
        lod_bias_bits: 0,
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
