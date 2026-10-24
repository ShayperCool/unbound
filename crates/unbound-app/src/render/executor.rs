//! Executes the rendering command stream (render_commands) on the GPU, on the thread that submits
//! it: each submission is planned (targets, passes, pipelines, uniforms, bind groups), encoded and
//! queued at once, so later commands, `read_pixels` and the next frame see its results in order.
//! The screen is a ring of offscreen targets; a completed frame's target is handed to the window
//! (`Presented`), which the render world shows.
mod compute;

use super::{
    compiled,
    pipelines::{ClearKey, DEPTH_FORMAT, DRAW_SLOT, PipelineKey, Pipelines},
    resources::{self, GpuMesh, GpuTexture, SamplerKey},
};
use crate::materials::{ResolvedMaterial, Resolver};
use bytemuck::{Pod, Zeroable};
use std::{
    collections::{HashMap, HashSet},
    ops::Range,
    sync::{Arc, Mutex},
    time::Instant,
};
use unbound_runtime::render_protocol::{
    commands::{self, Command},
    owned,
};

/// Resource changes, applied in order with the commands.
#[derive(Debug)]
pub enum Upload {
    UploadMesh(u64, owned::Mesh),
    ReleaseMesh(u64),
    UploadTexture(u64, owned::Texture),
    ReleaseTexture(u64),
    UpdateMaterial(u64, owned::Material),
    ReleaseMaterial(u64),
}

#[repr(C)]
#[derive(Clone, Copy, Pod, Zeroable)]
struct DrawUniform {
    model: [f32; 16],
    view: [f32; 16],
    proj: [f32; 16],
    color: [f32; 4],
    screen: [f32; 4],
    misc: [f32; 4],
    pad: [f32; 4],
}

/// A compiled program's texture bind group and what it was built from (program, color space,
/// texture generations).
type ProgramTextures = (wgpu::BindGroup, (u32, i32, Vec<u64>));

struct GpuMaterial {
    resolved: ResolvedMaterial,
    /// Per pass: the textures of the program it last drew with.
    pass_textures: Vec<Option<ProgramTextures>>,
}

struct ScreenTarget {
    width: u32,
    height: u32,
    format: wgpu::TextureFormat,
    texture: wgpu::Texture,
    color: wgpu::TextureView,
    depth: wgpu::TextureView,
}

/// The latest complete frame, for the window.
#[derive(Clone)]
pub struct Presented {
    pub color: wgpu::TextureView,
    pub color_space: i32,
    /// Changes with every frame (bind groups compare it).
    pub frame: u64,
    /// Which target of the ring it is.
    pub slot: usize,
}

/// Where draws go.
#[derive(Clone)]
struct Target {
    color: wgpu::TextureView,
    /// A multisampled target's resolve: the texture's own view.
    resolve: Option<wgpu::TextureView>,
    samples: u32,
    depth: Option<wgpu::TextureView>,
    format: wgpu::TextureFormat,
    width: u32,
    height: u32,
    /// The screen of this frame (first use clears it).
    screen: bool,
}

struct PlannedDraw {
    pipeline: wgpu::RenderPipeline,
    vertex: wgpu::Buffer,
    index: wgpu::Buffer,
    range: Range<u32>,
    base_vertex: i32,
    material: wgpu::BindGroup,
    texture: wgpu::BindGroup,
    offset: u32,
    stencil_ref: u32,
    viewport: [f32; 4],
    scissor: [u32; 4],
    /// The compiled program: its id and the offset of the draw's uniform block.
    program: (u32, u32),
}

enum PassItem {
    /// A clear drawn over the viewport: pipeline, uniform offset, viewport.
    Clear(wgpu::RenderPipeline, u32, [f32; 4]),
    Draw(PlannedDraw),
}

/// A texture-to-texture copy.
struct Copy {
    source: wgpu::Texture,
    source_mip: u32,
    source_origin: wgpu::Origin3d,
    destination: wgpu::Texture,
    destination_mip: u32,
    destination_origin: wgpu::Origin3d,
    size: wgpu::Extent3d,
}

/// The work of a submission, in order.
enum Op {
    Pass(Pass),
    Copy(Copy),
    Dispatch(compute::DispatchOp),
}

/// A render pass being built: consecutive commands on one target.
struct Pass {
    target: Target,
    color_load: wgpu::LoadOp<wgpu::Color>,
    /// Depth and stencil clear values; `None` loads them.
    depth_clear: Option<(f32, u32)>,
    items: Vec<PassItem>,
}

/// The state commands set for those after them.
struct State {
    target: Option<Target>,
    viewport: [f32; 4],
    view: [f32; 16],
    projection: [f32; 16],
    overlay: bool,
}

pub struct UnityRenderer {
    device: wgpu::Device,
    queue: wgpu::Queue,
    resolver: Resolver,
    pipelines: Pipelines,
    meshes: HashMap<u64, GpuMesh>,
    textures: HashMap<u64, GpuTexture>,
    defaults: HashMap<&'static str, GpuTexture>,
    /// Default textures of other dimensions (cube, 3D, array): views by (dimension, default).
    dim_defaults: HashMap<(unbound_shaders::TextureDim, String), wgpu::TextureView>,
    /// The error shader's program, compiled on first need.
    error_program: Option<unbound_shaders::Program>,
    depth_default: Option<wgpu::TextureView>,
    samplers: HashMap<SamplerKey, wgpu::Sampler>,
    materials: HashMap<u64, GpuMaterial>,
    buffers: HashMap<u64, compute::GpuBuffer>,
    kernels: compute::KernelCache,
    generation: u64,
    draw_buffer: Option<wgpu::Buffer>,
    draw_bind_group: Option<wgpu::BindGroup>,
    uniforms: Vec<DrawUniform>,
    /// The screen's ring of targets and the one this frame draws into.
    screens: [Option<ScreenTarget>; 3],
    screen_slot: usize,
    screen_size: (u32, u32),
    /// The screen has been drawn into this frame.
    screen_touched: bool,
    frame_number: u64,
    color_space: i32,
    /// Global shader properties and keywords (`Shader.SetGlobal*`, `Shader.EnableKeyword`).
    globals: Arc<Vec<(String, owned::PropertyValue)>>,
    keywords: Vec<String>,
    /// The `LightMode` tags of tagged-pass draws (renderer lists).
    pass_tags: Vec<String>,
    state: State,
    presented: Arc<Mutex<Option<Presented>>>,
    started: Instant,
    warned: HashSet<String>,
    traced_frames: u64,
    compiled: compiled::CompiledPrograms,
}

impl UnityRenderer {
    pub fn new(
        device: wgpu::Device,
        queue: wgpu::Queue,
        resolver: Resolver,
        compiled: compiled::CompiledPrograms,
        presented: Arc<Mutex<Option<Presented>>>,
    ) -> Self {
        let pipelines = Pipelines::new(&device);
        let defaults = resources::default_textures(&device, &queue);
        UnityRenderer {
            device,
            queue,
            resolver,
            pipelines,
            meshes: HashMap::new(),
            textures: HashMap::new(),
            defaults,
            dim_defaults: HashMap::new(),
            error_program: None,
            depth_default: None,
            samplers: HashMap::new(),
            materials: HashMap::new(),
            buffers: HashMap::new(),
            kernels: HashMap::new(),
            generation: 1,
            draw_buffer: None,
            draw_bind_group: None,
            uniforms: Vec::new(),
            screens: [None, None, None],
            screen_slot: 0,
            screen_size: (1, 1),
            screen_touched: false,
            frame_number: 0,
            color_space: 0,
            globals: Arc::new(Vec::new()),
            keywords: Vec::new(),
            pass_tags: Vec::new(),
            state: State {
                target: None,
                viewport: [0.0; 4],
                view: IDENTITY,
                projection: IDENTITY,
                overlay: false,
            },
            presented,
            started: Instant::now(),
            warned: HashSet::new(),
            traced_frames: 0,
            compiled,
        }
    }

    fn warn_once(&mut self, key: String, message: impl FnOnce() -> String) {
        if self.warned.insert(key) {
            tracing::warn!("{}", message());
        }
    }

    /// Applies a resource change.
    pub fn apply(&mut self, upload: Upload) {
        let device = self.device.clone();
        match upload {
            Upload::UploadMesh(handle, mesh) => {
                let (vertices, indices, submeshes) = resources::build_mesh(&mesh);
                let old = self.meshes.remove(&handle);
                let (old_vertex, old_index) = match old {
                    Some(m) => (Some(m.vertex), Some(m.index)),
                    None => (None, None),
                };
                let vertex = resources::write_buffer(
                    &device,
                    &self.queue,
                    old_vertex,
                    bytemuck::cast_slice(&vertices),
                    wgpu::BufferUsages::VERTEX,
                    "unbound vertices",
                );
                let index = resources::write_buffer(
                    &device,
                    &self.queue,
                    old_index,
                    bytemuck::cast_slice(&indices),
                    wgpu::BufferUsages::INDEX,
                    "unbound indices",
                );
                self.meshes.insert(
                    handle,
                    GpuMesh {
                        vertex,
                        index,
                        submeshes,
                    },
                );
            }
            Upload::ReleaseMesh(handle) => {
                self.meshes.remove(&handle);
            }
            Upload::UploadTexture(handle, texture) => {
                self.generation += 1;
                let old = self.textures.remove(&handle);
                let gpu =
                    resources::upload_texture(&device, &self.queue, old, &texture, self.generation);
                self.textures.insert(handle, gpu);
            }
            Upload::ReleaseTexture(handle) => {
                self.generation += 1;
                self.textures.remove(&handle);
            }
            Upload::UpdateMaterial(handle, material) => {
                let resolved = self.resolver.resolve(&material);
                let pass_textures = vec![None; resolved.passes.len() + resolved.all.len()];
                self.materials.insert(
                    handle,
                    GpuMaterial {
                        resolved,
                        pass_textures,
                    },
                );
            }
            Upload::ReleaseMaterial(handle) => {
                self.materials.remove(&handle);
            }
        }
    }

    fn sampler(&mut self, key: SamplerKey) -> wgpu::Sampler {
        let device = &self.device;
        self.samplers
            .entry(key)
            .or_insert_with(|| resources::create_sampler(device, key))
            .clone()
    }

    /// A texture by handle, or the named default.
    fn texture_or_default(&self, handle: u64, default: &str) -> &GpuTexture {
        self.textures.get(&handle).unwrap_or_else(|| {
            self.defaults
                .get(default)
                .unwrap_or_else(|| &self.defaults["white"])
        })
    }

    /// The main-texture bind group of a texture handle (or default), cached on the texture.
    fn main_bind_group(
        &mut self,
        handle: u64,
        default: &str,
        space: i32,
    ) -> (wgpu::BindGroup, bool) {
        let slot = space.clamp(0, 1) as usize;
        let key = {
            let t = self.texture_or_default(handle, default);
            if let Some(bg) = &t.bind_groups[slot] {
                return (bg.clone(), t.alpha_only);
            }
            t.sampler_key
        };
        let sampler = self.sampler(key);
        let layout = self.pipelines.texture_layout.clone();
        let device = self.device.clone();
        let use_default = !self.textures.contains_key(&handle);
        let t = if use_default {
            let name = if self.defaults.contains_key(default) {
                default
            } else {
                "white"
            };
            self.defaults.get_mut(name).unwrap()
        } else {
            self.textures.get_mut(&handle).unwrap()
        };
        let bg = device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("unbound main texture"),
            layout: &layout,
            entries: &[
                wgpu::BindGroupEntry {
                    binding: 0,
                    resource: wgpu::BindingResource::TextureView(t.view_for(space)),
                },
                wgpu::BindGroupEntry {
                    binding: 1,
                    resource: wgpu::BindingResource::Sampler(&sampler),
                },
            ],
        });
        t.bind_groups[slot] = Some(bg.clone());
        (bg, t.alpha_only)
    }

    /// A default texture of a dimension other than 2D (all texels the default's color).
    fn dim_default(&mut self, dim: unbound_shaders::TextureDim, default: &str) -> wgpu::TextureView {
        let key = (dim, default.to_string());
        if let Some(v) = self.dim_defaults.get(&key) {
            return v.clone();
        }
        use unbound_shaders::TextureDim;
        let rgba: [u8; 4] = match default {
            "white" => [255, 255, 255, 255],
            "black" => [0, 0, 0, 0],
            "bump" => [128, 128, 255, 128],
            "red" => [255, 0, 0, 0],
            _ => [128, 128, 128, 128],
        };
        let (layers, dimension) = match dim {
            TextureDim::Cube => (6, wgpu::TextureDimension::D2),
            TextureDim::D3 => (1, wgpu::TextureDimension::D3),
            _ => (1, wgpu::TextureDimension::D2),
        };
        let device = &self.device;
        let texture = device.create_texture(&wgpu::TextureDescriptor {
            label: Some("unbound default texture"),
            size: wgpu::Extent3d {
                width: 1,
                height: 1,
                depth_or_array_layers: layers,
            },
            mip_level_count: 1,
            sample_count: 1,
            dimension,
            format: wgpu::TextureFormat::Rgba8Unorm,
            usage: wgpu::TextureUsages::TEXTURE_BINDING | wgpu::TextureUsages::COPY_DST,
            view_formats: &[],
        });
        let data: Vec<u8> = (0..layers).flat_map(|_| rgba).collect();
        self.queue.write_texture(
            texture.as_image_copy(),
            &data,
            wgpu::TexelCopyBufferLayout {
                offset: 0,
                bytes_per_row: Some(4),
                rows_per_image: Some(1),
            },
            wgpu::Extent3d {
                width: 1,
                height: 1,
                depth_or_array_layers: layers,
            },
        );
        let view = texture.create_view(&wgpu::TextureViewDescriptor {
            dimension: Some(compiled::view_dimension(dim)),
            ..Default::default()
        });
        self.dim_defaults.insert(key, view.clone());
        view
    }

    /// The textures (group 1) of a material's pass drawn with program `id`, cached on the
    /// material. A texture property takes the material's texture, else the global one
    /// (`Shader.SetGlobalTexture`), else the property's default.
    fn program_bind_group(
        &mut self,
        handle: u64,
        pass: usize,
        id: u32,
        space: i32,
        globals: &[(String, owned::PropertyValue)],
        overrides: &[(String, owned::PropertyValue)],
    ) -> Option<wgpu::BindGroup> {
        let program = self.compiled.program(id).clone();
        let texture_override = |name: &str| {
            overrides.iter().find_map(|(n, v)| match v {
                owned::PropertyValue::Texture { handle, .. } if n == name => Some(*handle),
                _ => None,
            })
        };
        // Per-draw textures make a bind group of their own, not cached on the material.
        let cached = !overrides
            .iter()
            .any(|(_, v)| matches!(v, owned::PropertyValue::Texture { .. }));
        let slots: Vec<(u64, String, unbound_shaders::TextureDim, unbound_shaders::TextureSlot)> = {
            let resolved = &self.materials.get(&handle)?.resolved;
            program
                .textures
                .iter()
                .enumerate()
                .map(|(k, name)| {
                    let (mut texture, default) = resolved.texture(name);
                    if let Some(h) = texture_override(name) {
                        texture = h;
                    }
                    if texture == 0
                        && let Some((_, owned::PropertyValue::Texture { handle, .. })) =
                            globals.iter().find(|(n, _)| n == name)
                    {
                        texture = *handle;
                    }
                    let dim = program
                        .texture_dims
                        .get(k)
                        .copied()
                        .unwrap_or(unbound_shaders::TextureDim::D2);
                    let slot = program.texture_slots.get(k).copied().unwrap_or_default();
                    (texture, default, dim, slot)
                })
                .collect()
        };
        let is_2d = |dim: unbound_shaders::TextureDim| dim == unbound_shaders::TextureDim::D2;
        let generations: Vec<u64> = slots
            .iter()
            .map(|(h, d, dim, _)| {
                if is_2d(*dim) {
                    self.texture_or_default(*h, d).generation
                } else {
                    self.textures.get(h).map_or(0, |t| t.generation)
                }
            })
            .collect();
        let stamp = (id, space, generations);
        if cached
            && let Some(Some((bg, built))) = self.materials[&handle].pass_textures.get(pass)
            && *built == stamp
        {
            return Some(bg.clone());
        }
        let mut views = Vec::new();
        let mut samplers = Vec::new();
        for (h, d, dim, slot) in &slots {
            // A sampler is the texture's, or the state its name spells; comparison samplers
            // compare less-or-equal (Unity's shadow maps, without reversed depth).
            let mut key = match slot.inline {
                Some(inline) => resources::SamplerKey {
                    filter: inline.filter,
                    wrap_u: inline.wrap,
                    wrap_v: inline.wrap,
                    aniso: 1,
                    compare: 0,
                },
                None if is_2d(*dim) => self.texture_or_default(*h, d).sampler_key,
                None => self.textures.get(h).map_or(
                    resources::SamplerKey {
                        filter: 1,
                        wrap_u: 1,
                        wrap_v: 1,
                        aniso: 1,
                        compare: 0,
                    },
                    |t| t.sampler_key,
                ),
            };
            if slot.comparison {
                key.compare = 4;
            }
            if slot.depth {
                samplers.push(self.sampler(key));
                let view = self
                    .textures
                    .get(h)
                    .and_then(|t| t.depth_sample.clone())
                    .unwrap_or_else(|| self.depth_default());
                views.push(view);
            } else if is_2d(*dim) {
                samplers.push(self.sampler(key));
                views.push(self.texture_or_default(*h, d).view_for(space).clone());
            } else {
                samplers.push(self.sampler(key));
                // A cubemap bound to a cube slot; anything else reads the slot's default.
                let bound = self.textures.get(h).filter(|t| {
                    *dim == unbound_shaders::TextureDim::Cube && t.cube
                });
                match bound {
                    Some(t) => views.push(t.view_for(space).clone()),
                    None => views.push(self.dim_default(*dim, d)),
                }
            }
        }
        let mut entries = Vec::new();
        for (k, (view, sampler)) in views.iter().zip(&samplers).enumerate() {
            entries.push(wgpu::BindGroupEntry {
                binding: 2 * k as u32,
                resource: wgpu::BindingResource::TextureView(view),
            });
            entries.push(wgpu::BindGroupEntry {
                binding: 2 * k as u32 + 1,
                resource: wgpu::BindingResource::Sampler(sampler),
            });
        }
        let bg = self.device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("unbound program textures"),
            layout: self.compiled.texture_layout(id),
            entries: &entries,
        });
        if cached
            && let Some(slot) = self
                .materials
                .get_mut(&handle)
                .unwrap()
                .pass_textures
                .get_mut(pass)
        {
            *slot = Some((bg.clone(), stamp));
        }
        Some(bg)
    }

    /// A 1x1 depth texture at the far plane (1): what an unbound depth texture reads, so that
    /// comparisons against it pass (an absent shadow map shadows nothing).
    fn depth_default(&mut self) -> wgpu::TextureView {
        if let Some(view) = &self.depth_default {
            return view.clone();
        }
        let texture = self.device.create_texture(&wgpu::TextureDescriptor {
            label: Some("unbound default depth"),
            size: wgpu::Extent3d {
                width: 1,
                height: 1,
                depth_or_array_layers: 1,
            },
            mip_level_count: 1,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format: wgpu::TextureFormat::Depth32Float,
            usage: wgpu::TextureUsages::RENDER_ATTACHMENT | wgpu::TextureUsages::TEXTURE_BINDING,
            view_formats: &[],
        });
        let view = texture.create_view(&wgpu::TextureViewDescriptor::default());
        let mut encoder = self
            .device
            .create_command_encoder(&wgpu::CommandEncoderDescriptor::default());
        encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
            label: Some("unbound default depth clear"),
            color_attachments: &[],
            depth_stencil_attachment: Some(wgpu::RenderPassDepthStencilAttachment {
                view: &view,
                depth_ops: Some(wgpu::Operations {
                    load: wgpu::LoadOp::Clear(1.0),
                    store: wgpu::StoreOp::Store,
                }),
                stencil_ops: None,
            }),
            timestamp_writes: None,
            occlusion_query_set: None,
            multiview_mask: None,
        });
        self.queue.submit([encoder.finish()]);
        self.depth_default = Some(view.clone());
        view
    }

    /// The error shader's program.
    fn error_program(&mut self) -> unbound_shaders::Program {
        if let Some(p) = &self.error_program {
            return p.clone();
        }
        let lab = self.resolver.error_shader();
        let material = owned::Material {
            shader: lab.name.clone(),
            ..Default::default()
        };
        let program = self
            .resolver
            .resolve(&material)
            .passes
            .first()
            .map(|p| p.program.clone())
            .expect("the error shader has a pass");
        self.error_program = Some(program.clone());
        program
    }

    fn push_uniform(&mut self, u: DrawUniform) -> u32 {
        self.uniforms.push(u);
        ((self.uniforms.len() - 1) as u64 * DRAW_SLOT) as u32
    }

    fn screen_format(color_space: i32) -> wgpu::TextureFormat {
        if color_space == 1 {
            wgpu::TextureFormat::Rgba16Float
        } else {
            wgpu::TextureFormat::Rgba8Unorm
        }
    }

    /// The screen target of a ring slot, (re)created at the frame's size and format.
    fn ensure_screen(&mut self, slot: usize) -> &ScreenTarget {
        let (width, height) = self.screen_size;
        let format = Self::screen_format(self.color_space);
        let fits = self.screens[slot]
            .as_ref()
            .is_some_and(|s| s.width == width && s.height == height && s.format == format);
        if !fits {
            let size = wgpu::Extent3d {
                width,
                height,
                depth_or_array_layers: 1,
            };
            let texture = self.device.create_texture(&wgpu::TextureDescriptor {
                label: Some("unbound screen"),
                size,
                mip_level_count: 1,
                sample_count: 1,
                dimension: wgpu::TextureDimension::D2,
                format,
                usage: wgpu::TextureUsages::RENDER_ATTACHMENT
                    | wgpu::TextureUsages::TEXTURE_BINDING
                    | wgpu::TextureUsages::COPY_SRC
                    | wgpu::TextureUsages::COPY_DST,
                view_formats: &[],
            });
            let color = texture.create_view(&Default::default());
            let depth = self
                .device
                .create_texture(&wgpu::TextureDescriptor {
                    label: Some("unbound screen depth"),
                    size,
                    mip_level_count: 1,
                    sample_count: 1,
                    dimension: wgpu::TextureDimension::D2,
                    format: DEPTH_FORMAT,
                    usage: wgpu::TextureUsages::RENDER_ATTACHMENT,
                    view_formats: &[],
                })
                .create_view(&Default::default());
            self.screens[slot] = Some(ScreenTarget {
                width,
                height,
                format,
                texture,
                color,
                depth,
            });
        }
        self.screens[slot].as_ref().unwrap()
    }

    fn screen_target(&mut self) -> Target {
        let s = self.ensure_screen(self.screen_slot);
        Target {
            color: s.color.clone(),
            resolve: None,
            samples: 1,
            depth: Some(s.depth.clone()),
            format: s.format,
            width: s.width,
            height: s.height,
            screen: true,
        }
    }

    /// The target `SetTarget` names: the screen or a render texture.
    fn resolve_target(&mut self, color: u64, depth: u64, mip: i32, slice: i32) -> Option<Target> {
        let mut target = if color == 0 {
            self.screen_target()
        } else {
            let space = self.color_space;
            let Some(t) = self.textures.get(&color).filter(|t| t.render_target) else {
                self.warn_once(format!("rt{color}"), || {
                    format!("rendering into texture {color}, which is not a render target: skipped")
                });
                return None;
            };
            let view = match (&t.srgb_view, space) {
                (Some(srgb), 1) => srgb.clone(),
                _ => t.view.clone(),
            };
            // Multisampled: draws go into the samples, resolved into the texture.
            let (color, resolve) = match &t.msaa {
                Some((raw, srgb)) => {
                    let samples = match (srgb, space) {
                        (Some(srgb), 1) => srgb.clone(),
                        _ => raw.clone(),
                    };
                    (samples, Some(view))
                }
                None => (view, None),
            };
            Target {
                color,
                resolve,
                samples: t.samples,
                depth: t.depth.clone(),
                format: t.target_format(space),
                width: t.width,
                height: t.height,
                screen: false,
            }
        };
        if mip != 0 || slice != 0 {
            self.warn_once(format!("rt-sub{color}"), || {
                format!("rendering into mip {mip} / slice {slice} of texture {color} is not supported yet: drawn into the first")
            });
        }
        if depth == commands::NO_TEXTURE {
            target.depth = None;
        } else if depth != 0 && depth != color {
            target.depth = self.textures.get(&depth).and_then(|d| d.depth.clone());
        }
        Some(target)
    }

    fn new_pass(&mut self, target: Target) -> Pass {
        let mut pass = Pass {
            target,
            color_load: wgpu::LoadOp::Load,
            depth_clear: None,
            items: Vec::new(),
        };
        if pass.target.screen {
            if !self.screen_touched {
                // The screen's previous content is undefined in Unity; start from black.
                pass.color_load = wgpu::LoadOp::Clear(wgpu::Color::BLACK);
                pass.depth_clear = Some((1.0, 0));
            }
            self.screen_touched = true;
        }
        pass
    }

    /// Executes commands: plans them, then encodes and queues the work.
    pub fn execute(&mut self, commands: Vec<Command>) {
        self.uniforms.clear();
        self.compiled.begin_frame();
        let time = self.started.elapsed().as_secs_f32();
        let device = self.device.clone();
        let mut passes: Vec<Op> = Vec::new();
        // Whether the last pass is still open for the current target.
        let mut open = false;
        let mut end_frame = false;
        let mut draws = 0usize;
        for command in commands {
            match command {
                Command::BeginFrame {
                    screen_width,
                    screen_height,
                    color_space,
                } => {
                    self.screen_size = (screen_width.max(1) as u32, screen_height.max(1) as u32);
                    self.color_space = color_space.clamp(0, 1);
                    self.screen_slot = (self.screen_slot + 1) % self.screens.len();
                    self.screen_touched = false;
                    self.frame_number += 1;
                    self.state.target = None;
                    open = false;
                }
                Command::EndFrame => {
                    if !self.screen_touched {
                        // Nothing drew on the screen: it shows black.
                        let target = self.screen_target();
                        let pass = self.new_pass(target);
                        passes.push(Op::Pass(pass));
                    }
                    open = false;
                    end_frame = true;
                }
                Command::SetTarget {
                    color,
                    depth,
                    mip,
                    slice,
                } => {
                    self.state.target = self.resolve_target(color, depth, mip, slice);
                    if let Some(t) = &self.state.target {
                        self.state.viewport = [0.0, 0.0, t.width as f32, t.height as f32];
                    }
                    open = false;
                }
                Command::Viewport(v) => self.state.viewport = v,
                Command::Camera {
                    view,
                    projection,
                    flags,
                } => {
                    self.state.view = view;
                    self.state.projection = projection;
                    self.state.overlay = flags & commands::camera::OVERLAY != 0;
                }
                Command::Global(name, value) => {
                    let globals = Arc::make_mut(&mut self.globals);
                    match globals.iter_mut().find(|(n, _)| *n == name) {
                        Some(slot) => slot.1 = value,
                        None => globals.push((name, value)),
                    }
                }
                Command::RenderPipeline(tag) => {
                    if self.resolver.set_pipeline(&tag) {
                        // Materials draw with the SubShader the new pipeline runs.
                        self.error_program = None;
                        let handles: Vec<u64> = self.materials.keys().copied().collect();
                        for handle in handles {
                            let material = self.materials[&handle].resolved.material.clone();
                            let resolved = self.resolver.resolve(&material);
                            let pass_textures =
                                vec![None; resolved.passes.len() + resolved.all.len()];
                            self.materials.insert(
                                handle,
                                GpuMaterial {
                                    resolved,
                                    pass_textures,
                                },
                            );
                        }
                    }
                }
                Command::PassTags(tags) => self.pass_tags = tags,
                Command::Keyword(name, on) => {
                    let present = self.keywords.iter().position(|k| *k == name);
                    match (present, on) {
                        (None, true) => self.keywords.push(name),
                        (Some(i), false) => {
                            self.keywords.remove(i);
                        }
                        _ => {}
                    }
                }
                Command::Clear {
                    flags,
                    color,
                    depth,
                    stencil,
                } => {
                    let Some(target) = self.state.target.clone() else {
                        continue;
                    };
                    if !open {
                        let pass = self.new_pass(target.clone());
                        passes.push(Op::Pass(pass));
                        open = true;
                    }
                    let clear_color = flags & commands::clear::COLOR != 0;
                    let clear_depth =
                        flags & (commands::clear::DEPTH | commands::clear::STENCIL) != 0;
                    let viewport = self.state.viewport;
                    let full = viewport[0] <= 0.0
                        && viewport[1] <= 0.0
                        && viewport[0] + viewport[2] >= target.width as f32
                        && viewport[1] + viewport[3] >= target.height as f32;
                    let pass = last_pass(&mut passes);
                    if full && pass.items.is_empty() {
                        if clear_color {
                            pass.color_load = wgpu::LoadOp::Clear(wgpu::Color {
                                r: color[0] as f64,
                                g: color[1] as f64,
                                b: color[2] as f64,
                                a: color[3] as f64,
                            });
                        }
                        if clear_depth {
                            pass.depth_clear = Some((depth, stencil));
                        }
                    } else if clear_color || clear_depth {
                        let pipeline = self.pipelines.clear(
                            &device,
                            ClearKey {
                                format: target.format,
                                depth: target.depth.is_some(),
                                samples: target.samples,
                                color: clear_color,
                                clear_depth,
                            },
                        );
                        let offset = self.push_uniform(DrawUniform {
                            color,
                            ..Zeroable::zeroed()
                        });
                        last_pass(&mut passes)
                            .items
                            .push(PassItem::Clear(pipeline, offset, viewport));
                    }
                }
                Command::Draw(draw) => {
                    let Some(target) = self.state.target.clone() else {
                        self.warn_once("draw-no-target".into(), || {
                            "a draw came before any render target was set: skipped".into()
                        });
                        continue;
                    };
                    if !open {
                        let pass = self.new_pass(target.clone());
                        passes.push(Op::Pass(pass));
                        open = true;
                    }
                    let planned = self.plan_draw(&draw, &target, time);
                    draws += planned.len();
                    last_pass(&mut passes)
                        .items
                        .extend(planned.into_iter().map(PassItem::Draw));
                }
                Command::CreateBuffer {
                    handle,
                    size,
                    usage,
                } => {
                    // Earlier work in this submission runs first.
                    self.flush_ops(&mut passes, &mut open);
                    self.create_buffer(handle, size, usage);
                }
                Command::BufferData {
                    handle,
                    offset,
                    data,
                } => {
                    self.flush_ops(&mut passes, &mut open);
                    self.write_buffer(handle, offset, &data);
                }
                Command::ReleaseBuffer(handle) => {
                    self.flush_ops(&mut passes, &mut open);
                    self.release_buffer(handle);
                }
                Command::Dispatch(d) => {
                    open = false;
                    if let Some(op) = self.plan_dispatch(&d) {
                        passes.push(op);
                    }
                }
                Command::CopyTexture {
                    source,
                    source_slice,
                    source_mip,
                    source_rect,
                    destination,
                    destination_slice,
                    destination_mip,
                    destination_x,
                    destination_y,
                } => {
                    open = false;
                    if let Some(copy) = self.plan_copy(
                        source,
                        [source_slice, source_mip],
                        source_rect,
                        destination,
                        [destination_slice, destination_mip],
                        [destination_x, destination_y],
                    ) {
                        passes.push(Op::Copy(copy));
                    }
                }
            }
        }
        if end_frame && trace_render() {
            self.traced_frames += 1;
            if self.traced_frames % 60 == 1 {
                tracing::info!(
                    "[trace] renderer: frame {} in {} passes, {draws} draws, screen {}x{}",
                    self.frame_number,
                    passes.len(),
                    self.screen_size.0,
                    self.screen_size.1,
                );
            }
        }
        self.encode(passes);
        if end_frame {
            let slot = self.screen_slot;
            let screen = self.ensure_screen(slot).color.clone();
            *self.presented.lock().unwrap() = Some(Presented {
                color: screen,
                color_space: self.color_space,
                frame: self.frame_number,
                slot,
            });
        }
    }

    /// The pipelines, bind groups and uniforms of a draw: one per pass it draws.
    fn plan_draw(&mut self, draw: &commands::Draw, target: &Target, time: f32) -> Vec<PlannedDraw> {
        let mut planned = Vec::new();
        let device = self.device.clone();
        let space = self.color_space;
        let (tw, th) = (target.width, target.height);
        let Some(material) = self.materials.get(&draw.material) else {
            self.warn_once(format!("mat{}", draw.material), || {
                format!("a draw uses unknown material {}; skipped", draw.material)
            });
            return planned;
        };
        let resolved = material.resolved.clone();
        let Some(mesh) = self.meshes.get(&draw.mesh) else {
            self.warn_once(format!("mesh{}", draw.mesh), || {
                format!("a draw uses unknown mesh {}; skipped", draw.mesh)
            });
            return planned;
        };
        let Some(sub) = mesh.submeshes.get(draw.submesh.max(0) as usize).cloned() else {
            self.warn_once(format!("sub{}/{}", draw.mesh, draw.submesh), || {
                format!(
                    "a draw uses missing submesh {} of mesh {}; skipped",
                    draw.submesh, draw.mesh
                )
            });
            return planned;
        };
        if sub.count == 0 {
            return planned;
        }
        let (vertex, index) = (mesh.vertex.clone(), mesh.index.clone());
        let viewport = clamp_viewport(self.state.viewport, tw, th);
        if viewport[2] <= 0.0 || viewport[3] <= 0.0 {
            return planned;
        }
        let viewport_scissor = clamp_rect(viewport, tw, th);
        let scissor = match draw.clip {
            Some(clip) => intersect(clamp_rect(clip, tw, th), viewport_scissor),
            None => viewport_scissor,
        };
        if scissor[2] == 0 || scissor[3] == 0 {
            if trace_render() {
                tracing::info!(
                    "[trace] draw of mesh {} clipped away (scissor {scissor:?})",
                    draw.mesh
                );
            }
            return planned;
        }
        let (view, projection) = (self.state.view, self.state.projection);
        // The passes drawn: the forward ones (a renderer's), every one, or one by index.
        let forward = resolved.passes.len();
        let passes: Vec<(usize, &crate::materials::ResolvedPass)> = match draw.pass {
            -1 => resolved.passes.iter().enumerate().collect(),
            // A renderer list's: per tag in order, the first pass with that LightMode.
            -3 => {
                let mut chosen = Vec::new();
                let tags = self.pass_tags.clone();
                for tag in &tags {
                    let Some(i) = resolved.lab.tagged_pass(tag) else {
                        continue;
                    };
                    if chosen.iter().any(|(c, _)| *c == forward + i) {
                        continue;
                    }
                    match &resolved.all[i] {
                        Ok(p) => chosen.push((forward + i, p)),
                        Err(reason) => {
                            let reason = reason.clone();
                            self.warn_once(format!("{}#{i}", resolved.shader), || {
                                format!(
                                    "shader `{}`: pass {i} is not drawn: {reason}",
                                    resolved.shader
                                )
                            });
                        }
                    }
                }
                chosen
            }
            -2 => resolved
                .all
                .iter()
                .enumerate()
                .filter_map(|(i, p)| p.as_ref().ok().map(|p| (forward + i, p)))
                .collect(),
            n => match resolved.all.get(n as usize) {
                Some(Ok(p)) => vec![(forward + n as usize, p)],
                Some(Err(reason)) => {
                    let reason = reason.clone();
                    self.warn_once(format!("{}#{n}", resolved.shader), || {
                        format!(
                            "shader `{}`: pass {n} is not drawn: {reason}",
                            resolved.shader
                        )
                    });
                    return planned;
                }
                None => {
                    self.warn_once(format!("{}#{n}?", resolved.shader), || {
                        format!(
                            "shader `{}` has no pass {n} ({} passes): nothing drawn",
                            resolved.shader,
                            resolved.all.len()
                        )
                    });
                    return planned;
                }
            },
        };
        // The material's properties under the draw's own (a MaterialPropertyBlock).
        let merged;
        let material_props: &owned::Material = if draw.properties.is_empty() {
            &resolved.material
        } else {
            let mut m = resolved.material.clone();
            for (name, value) in &draw.properties {
                match m.properties.iter_mut().find(|(n, _)| n == name) {
                    Some(slot) => slot.1 = value.clone(),
                    None => m.properties.push((name.clone(), value.clone())),
                }
            }
            merged = m;
            &merged
        };
        let texture_of = |name: &str| match material_props.get(name) {
            Some(owned::PropertyValue::Texture { handle, .. }) => *handle,
            _ => 0,
        };
        // `_MainTex`: the renderer's (a sprite's texture), else the material's.
        let (_, main_default) = resolved.texture("_MainTex");
        let main_handle = if draw.main_texture != 0 {
            draw.main_texture
        } else {
            texture_of("_MainTex")
        };
        let (texture_bg, alpha_only) = self.main_bind_group(main_handle, &main_default, space);
        let screen = [
            tw as f32,
            th as f32,
            1.0 + 1.0 / tw as f32,
            1.0 + 1.0 / th as f32,
        ];
        let offset = self.push_uniform(DrawUniform {
            model: draw.model,
            view,
            proj: projection,
            color: draw.color,
            screen,
            misc: [time, space as f32, if alpha_only { 1.0 } else { 0.0 }, 0.0],
            pad: [0.0; 4],
        });
        // The material's keywords and the global ones.
        let mut keywords = resolved.material.keywords.clone();
        keywords.extend(self.keywords.iter().cloned());
        let globals = self.globals.clone();
        for (pass_index, pass) in passes {
            let layout = self.pipelines.texture_layout.clone();
            let mut drawn = (pass.program.clone(), pass.state, pass.stencil_ref);
            let mut id = self.compiled.variant(
                &device,
                &resolved.shader,
                &pass.program,
                &keywords,
                space == 1,
                &layout,
            );
            if id.is_none() {
                // Unity draws a pass that does not compile with its error shader.
                let error = self.error_program();
                id = self.compiled.variant(
                    &device,
                    crate::materials::ERROR_SHADER_NAME,
                    &error,
                    &[],
                    space == 1,
                    &layout,
                );
                drawn = (error, Default::default(), 0);
            }
            if trace_render() {
                tracing::info!(
                    "[trace] draw mesh {} sub {}: `{}` pass {} ({}){}",
                    draw.mesh,
                    draw.submesh,
                    resolved.shader,
                    pass_index,
                    pass.program.light_mode.as_deref().unwrap_or("-"),
                    if drawn.0.light_mode == pass.program.light_mode && id.is_some() { "" } else { " -> error shader" }
                );
            }
            let Some(id) = id else { continue };
            let (_, mut state, stencil_ref) = drawn;
            if state.gui_ztest {
                // Unity's unity_GUIZTestMode: Always over the screen, LessEqual elsewhere.
                state.ztest = if self.state.overlay { 8 } else { 4 };
            }
            let key = PipelineKey {
                state,
                topology: sub.topology,
                format: target.format,
                depth: target.depth.is_some(),
                samples: target.samples,
            };
            let pipeline = self.compiled.pipeline(&device, id, key);
            let Some(material_bg) = self.program_bind_group(
                draw.material,
                pass_index,
                id,
                space,
                &globals,
                &draw.properties,
            ) else {
                continue;
            };
            let textures = &self.textures;
            let size = |name: &str| {
                let mut handle = if name == "_MainTex" {
                    main_handle
                } else {
                    texture_of(name)
                };
                // `<name>_TexelSize` of a global texture too, as Unity provides it.
                if handle == 0
                    && let Some((_, owned::PropertyValue::Texture { handle: h, .. })) =
                        globals.iter().find(|(n, _)| n == name)
                {
                    handle = *h;
                }
                textures.get(&handle).map(|t| (t.width, t.height))
            };
            let values = compiled::DrawValues {
                model: draw.model,
                view,
                proj: projection,
                screen: [tw as f32, th as f32],
                time,
                linear: space == 1,
                material: material_props,
                lab: &resolved.lab,
                globals: &globals,
                main_alpha_only: alpha_only,
                texture_size: &size,
            };
            if let Ok(filter) = std::env::var("UNBOUND_TRACE_UNIFORMS")
                && resolved.shader.contains(filter.as_str())
            {
                tracing::info!(
                    "[uniforms] `{}` pass {} target {}x{}: {}",
                    resolved.shader,
                    pass_index,
                    tw,
                    th,
                    self.compiled.describe(id, &values)
                );
            }
            let program_offset = self.compiled.push(id, &values);
            planned.push(PlannedDraw {
                pipeline,
                vertex: vertex.clone(),
                index: index.clone(),
                range: sub.start..sub.start + sub.count,
                base_vertex: sub.base_vertex,
                material: material_bg,
                texture: texture_bg.clone(),
                offset,
                stencil_ref,
                viewport,
                scissor,
                program: (id, program_offset),
            });
        }
        planned
    }

    /// Encodes and queues the work planned so far (a buffer change must come after it).
    fn flush_ops(&mut self, ops: &mut Vec<Op>, open: &mut bool) {
        if ops.is_empty() {
            return;
        }
        self.encode(std::mem::take(ops));
        self.uniforms.clear();
        self.compiled.begin_frame();
        *open = false;
    }

    /// A copy between two textures (0: the screen), clipped to both; formats must match.
    fn plan_copy(
        &mut self,
        source: u64,
        [source_slice, source_mip]: [i32; 2],
        rect: [i32; 4],
        destination: u64,
        [destination_slice, destination_mip]: [i32; 2],
        [dx, dy]: [i32; 2],
    ) -> Option<Copy> {
        let texture = |this: &mut Self, handle: u64| -> Option<wgpu::Texture> {
            if handle == 0 {
                Some(this.ensure_screen(this.screen_slot).texture.clone())
            } else {
                this.textures.get(&handle).map(|t| t.texture.clone())
            }
        };
        let (Some(src), Some(dst)) = (texture(self, source), texture(self, destination)) else {
            self.warn_once(format!("copy{source}-{destination}"), || {
                format!("CopyTexture between unknown textures {source} and {destination}: skipped")
            });
            return None;
        };
        if src.format().remove_srgb_suffix() != dst.format().remove_srgb_suffix() {
            self.warn_once(format!("copyfmt{source}-{destination}"), || {
                format!(
                    "CopyTexture from {:?} to {:?}: the formats differ (Unity needs them compatible): skipped",
                    src.format(),
                    dst.format()
                )
            });
            return None;
        }
        let (smip, dmip) = (source_mip.max(0) as u32, destination_mip.max(0) as u32);
        if smip >= src.mip_level_count() || dmip >= dst.mip_level_count() {
            return None;
        }
        let sw = (src.width() >> smip).max(1) as i32;
        let sh = (src.height() >> smip).max(1) as i32;
        let dw = (dst.width() >> dmip).max(1) as i32;
        let dh = (dst.height() >> dmip).max(1) as i32;
        let [mut x, mut y, mut w, mut h] = rect;
        if w < 0 {
            [x, y, w, h] = [0, 0, sw, sh];
        }
        let w = w.min(sw - x).min(dw - dx);
        let h = h.min(sh - y).min(dh - dy);
        if w <= 0 || h <= 0 || x < 0 || y < 0 || dx < 0 || dy < 0 {
            return None;
        }
        Some(Copy {
            source: src,
            source_mip: smip,
            source_origin: wgpu::Origin3d {
                x: x as u32,
                y: y as u32,
                z: source_slice.max(0) as u32,
            },
            destination: dst,
            destination_mip: dmip,
            destination_origin: wgpu::Origin3d {
                x: dx as u32,
                y: dy as u32,
                z: destination_slice.max(0) as u32,
            },
            size: wgpu::Extent3d {
                width: w as u32,
                height: h as u32,
                depth_or_array_layers: 1,
            },
        })
    }

    /// Uploads the planned uniforms, encodes the passes and queues them.
    fn encode(&mut self, passes: Vec<Op>) {
        if passes.is_empty() {
            return;
        }
        let device = self.device.clone();
        let bytes = self.uniforms.len().max(1) as u64 * DRAW_SLOT;
        if self.draw_buffer.as_ref().is_none_or(|b| b.size() < bytes) {
            let buffer = device.create_buffer(&wgpu::BufferDescriptor {
                label: Some("unbound draws"),
                size: bytes.next_power_of_two().max(DRAW_SLOT * 64),
                usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
                mapped_at_creation: false,
            });
            self.draw_bind_group = Some(device.create_bind_group(&wgpu::BindGroupDescriptor {
                label: Some("unbound draws"),
                layout: &self.pipelines.draw_layout,
                entries: &[wgpu::BindGroupEntry {
                    binding: 0,
                    resource: wgpu::BindingResource::Buffer(wgpu::BufferBinding {
                        buffer: &buffer,
                        offset: 0,
                        size: wgpu::BufferSize::new(size_of::<DrawUniform>() as u64),
                    }),
                }],
            }));
            self.draw_buffer = Some(buffer);
        }
        if !self.uniforms.is_empty() {
            let mut data = vec![0u8; self.uniforms.len() * DRAW_SLOT as usize];
            for (i, u) in self.uniforms.iter().enumerate() {
                let at = i * DRAW_SLOT as usize;
                data[at..at + size_of::<DrawUniform>()].copy_from_slice(bytemuck::bytes_of(u));
            }
            self.queue
                .write_buffer(self.draw_buffer.as_ref().unwrap(), 0, &data);
        }
        let programs = self.compiled.upload(
            &device,
            &self.queue,
            self.draw_buffer.as_ref().unwrap(),
            size_of::<DrawUniform>() as u64,
        );
        let draw_bg = self.draw_bind_group.clone().unwrap();
        let mut encoder = device.create_command_encoder(&wgpu::CommandEncoderDescriptor {
            label: Some("unbound commands"),
        });
        for op in &passes {
            let pass = match op {
                Op::Pass(pass) => pass,
                Op::Dispatch(d) => {
                    compute::encode(&mut encoder, d);
                    continue;
                }
                Op::Copy(c) => {
                    encoder.copy_texture_to_texture(
                        wgpu::TexelCopyTextureInfo {
                            texture: &c.source,
                            mip_level: c.source_mip,
                            origin: c.source_origin,
                            aspect: wgpu::TextureAspect::All,
                        },
                        wgpu::TexelCopyTextureInfo {
                            texture: &c.destination,
                            mip_level: c.destination_mip,
                            origin: c.destination_origin,
                            aspect: wgpu::TextureAspect::All,
                        },
                        c.size,
                    );
                    continue;
                }
            };
            let mut rp = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
                label: Some("unbound pass"),
                color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                    view: &pass.target.color,
                    depth_slice: None,
                    resolve_target: pass.target.resolve.as_ref(),
                    ops: wgpu::Operations {
                        load: pass.color_load,
                        store: wgpu::StoreOp::Store,
                    },
                })],
                depth_stencil_attachment: pass.target.depth.as_ref().map(|depth| {
                    wgpu::RenderPassDepthStencilAttachment {
                        view: depth,
                        depth_ops: Some(wgpu::Operations {
                            load: match pass.depth_clear {
                                Some((d, _)) => wgpu::LoadOp::Clear(d),
                                None => wgpu::LoadOp::Load,
                            },
                            store: wgpu::StoreOp::Store,
                        }),
                        stencil_ops: Some(wgpu::Operations {
                            load: match pass.depth_clear {
                                Some((_, s)) => wgpu::LoadOp::Clear(s),
                                None => wgpu::LoadOp::Load,
                            },
                            store: wgpu::StoreOp::Store,
                        }),
                    }
                }),
                timestamp_writes: None,
                occlusion_query_set: None,
                multiview_mask: None,
            });
            let mut current_viewport = None;
            for item in &pass.items {
                let viewport = match item {
                    PassItem::Clear(_, _, v) => {
                        clamp_viewport(*v, pass.target.width, pass.target.height)
                    }
                    PassItem::Draw(d) => d.viewport,
                };
                if viewport[2] <= 0.0 || viewport[3] <= 0.0 {
                    continue;
                }
                if current_viewport != Some(viewport) {
                    let [x, y, w, h] = viewport;
                    rp.set_viewport(x, y, w, h, 0.0, 1.0);
                    current_viewport = Some(viewport);
                }
                match item {
                    PassItem::Clear(pipeline, offset, _) => {
                        let s = clamp_rect(viewport, pass.target.width, pass.target.height);
                        rp.set_pipeline(pipeline);
                        rp.set_bind_group(0, &draw_bg, &[*offset]);
                        rp.set_stencil_reference(0);
                        rp.set_scissor_rect(s[0], s[1], s[2], s[3]);
                        rp.draw(0..3, 0..1);
                    }
                    PassItem::Draw(d) => {
                        let (id, offset) = d.program;
                        let Some(group) = programs.get(&id) else {
                            continue;
                        };
                        rp.set_pipeline(&d.pipeline);
                        rp.set_bind_group(0, group, &[offset, d.offset]);
                        rp.set_bind_group(1, &d.material, &[]);
                        rp.set_bind_group(2, &d.texture, &[]);
                        rp.set_vertex_buffer(0, d.vertex.slice(..));
                        rp.set_index_buffer(d.index.slice(..), wgpu::IndexFormat::Uint32);
                        rp.set_stencil_reference(d.stencil_ref);
                        rp.set_scissor_rect(d.scissor[0], d.scissor[1], d.scissor[2], d.scissor[3]);
                        rp.draw_indexed(d.range.clone(), d.base_vertex, 0..1);
                    }
                }
            }
        }
        self.queue.submit([encoder.finish()]);
    }

    /// The pixels of a rectangle of a texture (0: this frame's screen) after everything queued:
    /// RGBA rows bottom first, 4 bytes per pixel, or 4 `f32`s when `float`.
    pub fn read_pixels(&mut self, texture: u64, rect: [i32; 4], float: bool) -> Option<Vec<u8>> {
        let source = if texture == 0 {
            self.screens[self.screen_slot].as_ref()?.texture.clone()
        } else {
            self.textures.get(&texture)?.texture.clone()
        };
        let format = source.format();
        let (tw, th) = (source.width() as i32, source.height() as i32);
        let x0 = rect[0].clamp(0, tw);
        let y0 = rect[1].clamp(0, th);
        let x1 = (rect[0] + rect[2]).clamp(0, tw);
        let y1 = (rect[1] + rect[3]).clamp(0, th);
        let (w, h) = ((x1 - x0) as u32, (y1 - y0) as u32);
        if w == 0 || h == 0 {
            return Some(Vec::new());
        }
        let texel = format.block_copy_size(None)?;
        let row = (w * texel).next_multiple_of(wgpu::COPY_BYTES_PER_ROW_ALIGNMENT);
        let buffer = self.device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("unbound read pixels"),
            size: row as u64 * h as u64,
            usage: wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        });
        let mut encoder = self
            .device
            .create_command_encoder(&wgpu::CommandEncoderDescriptor {
                label: Some("unbound read pixels"),
            });
        encoder.copy_texture_to_buffer(
            wgpu::TexelCopyTextureInfo {
                texture: &source,
                mip_level: 0,
                origin: wgpu::Origin3d {
                    x: x0 as u32,
                    y: y0 as u32,
                    z: 0,
                },
                aspect: wgpu::TextureAspect::All,
            },
            wgpu::TexelCopyBufferInfo {
                buffer: &buffer,
                layout: wgpu::TexelCopyBufferLayout {
                    offset: 0,
                    bytes_per_row: Some(row),
                    rows_per_image: Some(h),
                },
            },
            wgpu::Extent3d {
                width: w,
                height: h,
                depth_or_array_layers: 1,
            },
        );
        let index = self.queue.submit([encoder.finish()]);
        let slice = buffer.slice(..);
        slice.map_async(wgpu::MapMode::Read, |_| {});
        if let Err(e) = self.device.poll(wgpu::PollType::Wait {
            submission_index: Some(index),
            timeout: None,
        }) {
            tracing::warn!("reading pixels back from the GPU failed: {e}");
            return None;
        }
        let data = match slice.get_mapped_range() {
            Ok(data) => data,
            Err(e) => {
                tracing::warn!("reading pixels back from the GPU failed: {e}");
                return None;
            }
        };
        let mut out = Vec::with_capacity((w * h) as usize * if float { 16 } else { 4 });
        for r in 0..h as usize {
            let line = &data[r * row as usize..r * row as usize + (w * texel) as usize];
            match format {
                wgpu::TextureFormat::Rgba16Float => {
                    for c in line.as_chunks::<2>().0 {
                        let v = resources::f16_to_f32(u16::from_le_bytes(*c));
                        if float {
                            out.extend_from_slice(&v.to_le_bytes());
                        } else {
                            out.push((v.clamp(0.0, 1.0) * 255.0).round() as u8);
                        }
                    }
                }
                _ => {
                    for &b in line {
                        if float {
                            out.extend_from_slice(&(b as f32 / 255.0).to_le_bytes());
                        } else {
                            out.push(b);
                        }
                    }
                }
            }
        }
        Some(out)
    }
}

/// The pass the commands are adding to (the last op is one while a pass is open).
fn last_pass(ops: &mut [Op]) -> &mut Pass {
    match ops.last_mut() {
        Some(Op::Pass(pass)) => pass,
        _ => unreachable!("a pass is open"),
    }
}

const IDENTITY: [f32; 16] = [
    1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0,
];

/// A viewport (x, y, w, h; bottom-up rows) cut to a target: wgpu's must lie inside it.
fn clamp_viewport(v: [f32; 4], w: u32, h: u32) -> [f32; 4] {
    let x0 = v[0].clamp(0.0, w as f32);
    let y0 = v[1].clamp(0.0, h as f32);
    let x1 = (v[0] + v[2]).clamp(0.0, w as f32);
    let y1 = (v[1] + v[3]).clamp(0.0, h as f32);
    [x0, y0, (x1 - x0).max(0.0), (y1 - y0).max(0.0)]
}

/// A pixel rect (x, y, w, h; bottom-up rows) clamped to a target.
fn clamp_rect(r: [f32; 4], w: u32, h: u32) -> [u32; 4] {
    let x0 = r[0].max(0.0).min(w as f32);
    let y0 = r[1].max(0.0).min(h as f32);
    let x1 = (r[0] + r[2]).max(0.0).min(w as f32);
    let y1 = (r[1] + r[3]).max(0.0).min(h as f32);
    let (x0, y0) = (x0.round() as u32, y0.round() as u32);
    let (x1, y1) = (x1.round() as u32, y1.round() as u32);
    [x0, y0, x1.saturating_sub(x0), y1.saturating_sub(y0)]
}

fn intersect(a: [u32; 4], b: [u32; 4]) -> [u32; 4] {
    let x0 = a[0].max(b[0]);
    let y0 = a[1].max(b[1]);
    let x1 = (a[0] + a[2]).min(b[0] + b[2]);
    let y1 = (a[1] + a[3]).min(b[1] + b[3]);
    [x0, y0, x1.saturating_sub(x0), y1.saturating_sub(y0)]
}

/// `UNBOUND_TRACE` includes `render`: the renderer reports what it skips.
pub fn trace_render() -> bool {
    static ON: std::sync::OnceLock<bool> = std::sync::OnceLock::new();
    *ON.get_or_init(|| {
        std::env::var("UNBOUND_TRACE")
            .map(|v| v == "all" || v.split(',').any(|c| c == "render" || c == "render-detail"))
            .unwrap_or(false)
    })
}
