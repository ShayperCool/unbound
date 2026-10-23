//! The renderer: executes the managed engine's frame (render_protocol) on the window's view.
//!
//! Main world → render world: the queued protocol calls are taken at extraction. Prepare applies
//! them (uploads), resolves materials, fills the per-draw uniforms and plans every camera's passes
//! (pipelines, bind groups, ranges). The camera's render schedule (`UnityFrameGraph`) encodes the
//! plan: cameras in order, each draw list in the given order, then the present blit.
pub mod pipelines;
pub mod resources;

use crate::{
    backend::{Command, QueuedBackend},
    families::{ResolvedMaterial, Resolver},
    shaderlab::ShaderIndex,
};
use bevy::{
    ecs::schedule::{ScheduleBuildSettings, ScheduleLabel},
    prelude::*,
    render::{
        ExtractSchedule, Render, RenderApp, RenderSystems,
        renderer::{RenderContext, RenderDevice, RenderQueue, ViewQuery},
        view::ViewTarget,
    },
};
use bytemuck::{Pod, Zeroable};
use pipelines::{ClearKey, DEPTH_FORMAT, DRAW_SLOT, PipelineKey, Pipelines};
use resources::{GpuMesh, GpuTexture, SamplerKey};
use std::{
    collections::{HashMap, HashSet},
    ops::Range,
    path::PathBuf,
    sync::Arc,
    time::Instant,
};
use unbound_runtime::render_protocol::{CameraPacket, DrawPacket, owned};

/// The render schedule of the window's camera.
#[derive(ScheduleLabel, Debug, Clone, PartialEq, Eq, Hash, Default)]
pub struct UnityFrameGraph;

pub struct UnityRenderPlugin {
    pub backend: Arc<QueuedBackend>,
    /// Where the project's `.shader` files are (Assets and package roots).
    pub shader_roots: Vec<PathBuf>,
}

#[derive(Resource, Clone)]
struct SharedBackend(Arc<QueuedBackend>);

impl Plugin for UnityRenderPlugin {
    fn build(&self, app: &mut App) {
        let Some(render_app) = app.get_sub_app_mut(RenderApp) else {
            return;
        };
        let mut schedule = Schedule::new(UnityFrameGraph);
        schedule.set_build_settings(ScheduleBuildSettings {
            auto_insert_apply_deferred: false,
            ..Default::default()
        });
        render_app
            .insert_resource(SharedBackend(self.backend.clone()))
            .add_schedule(schedule)
            .add_systems(ExtractSchedule, extract_protocol)
            .add_systems(
                Render,
                prepare_frame.in_set(RenderSystems::PrepareResources),
            )
            .add_systems(UnityFrameGraph, encode_frame);
    }

    fn finish(&self, app: &mut App) {
        let Some(render_app) = app.get_sub_app_mut(RenderApp) else {
            return;
        };
        let device = render_app.world().resource::<RenderDevice>().clone();
        let queue = render_app.world().resource::<RenderQueue>().clone();
        let index = ShaderIndex::scan(&self.shader_roots);
        if !self.shader_roots.is_empty() {
            tracing::info!("indexed {} project shaders", index.len());
        }
        render_app.insert_resource(UnityRenderer::new(device, queue, Resolver::new(index)));
    }
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

struct GpuMaterial {
    resolved: ResolvedMaterial,
    buffer: wgpu::Buffer,
    /// The bind group and what it was built from: (color space, slot texture generations).
    bind_group: Option<(wgpu::BindGroup, (i32, u64, u64))>,
}

struct ScreenTarget {
    width: u32,
    height: u32,
    format: wgpu::TextureFormat,
    color: wgpu::TextureView,
    depth: wgpu::TextureView,
    blit_bind_group: Option<(wgpu::TextureFormat, wgpu::BindGroup)>,
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
    scissor: [u32; 4],
}

struct PlannedCamera {
    color: wgpu::TextureView,
    depth: Option<wgpu::TextureView>,
    color_load: wgpu::LoadOp<wgpu::Color>,
    depth_load: bool,
    viewport: [f32; 4],
    /// A clear drawn over a partial viewport: pipeline and uniform offset.
    clear: Option<(wgpu::RenderPipeline, u32)>,
    draws: Vec<PlannedDraw>,
}

struct Plan {
    cameras: Vec<PlannedCamera>,
    present: Option<wgpu::BindGroup>,
}

#[derive(Resource)]
pub struct UnityRenderer {
    device: RenderDevice,
    queue: RenderQueue,
    resolver: Resolver,
    pipelines: Pipelines,
    meshes: HashMap<u64, GpuMesh>,
    textures: HashMap<u64, GpuTexture>,
    defaults: HashMap<&'static str, GpuTexture>,
    samplers: HashMap<SamplerKey, wgpu::Sampler>,
    materials: HashMap<u64, GpuMaterial>,
    commands: Vec<Command>,
    frame: Option<owned::Frame>,
    generation: u64,
    draw_buffer: Option<wgpu::Buffer>,
    draw_bind_group: Option<wgpu::BindGroup>,
    uniforms: Vec<DrawUniform>,
    screen: Option<ScreenTarget>,
    blit_params: [Option<wgpu::Buffer>; 2],
    blit_sampler: wgpu::Sampler,
    plan: Option<Plan>,
    started: Instant,
    warned: HashSet<String>,
    traced_frames: u64,
}

impl UnityRenderer {
    fn new(device: RenderDevice, queue: RenderQueue, resolver: Resolver) -> Self {
        let d = device.wgpu_device();
        let pipelines = Pipelines::new(d);
        let defaults = resources::default_textures(d, &queue);
        let blit_sampler = d.create_sampler(&wgpu::SamplerDescriptor {
            label: Some("unbound present"),
            mag_filter: wgpu::FilterMode::Linear,
            min_filter: wgpu::FilterMode::Linear,
            ..Default::default()
        });
        UnityRenderer {
            device,
            queue,
            resolver,
            pipelines,
            meshes: HashMap::new(),
            textures: HashMap::new(),
            defaults,
            samplers: HashMap::new(),
            materials: HashMap::new(),
            commands: Vec::new(),
            frame: None,
            generation: 1,
            draw_buffer: None,
            draw_bind_group: None,
            uniforms: Vec::new(),
            screen: None,
            blit_params: [None, None],
            blit_sampler,
            plan: None,
            started: Instant::now(),
            warned: HashSet::new(),
            traced_frames: 0,
        }
    }

    fn warn_once(&mut self, key: String, message: impl FnOnce() -> String) {
        if self.warned.insert(key) {
            tracing::warn!("{}", message());
        }
    }

    fn apply(&mut self, command: Command) {
        let device = self.device.wgpu_device().clone();
        match command {
            Command::UploadMesh(handle, mesh) => {
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
            Command::ReleaseMesh(handle) => {
                self.meshes.remove(&handle);
            }
            Command::UploadTexture(handle, texture) => {
                self.generation += 1;
                let old = self.textures.remove(&handle);
                let gpu =
                    resources::upload_texture(&device, &self.queue, old, &texture, self.generation);
                self.textures.insert(handle, gpu);
            }
            Command::ReleaseTexture(handle) => {
                self.generation += 1;
                self.textures.remove(&handle);
            }
            Command::UpdateMaterial(handle, material) => {
                let resolved = self.resolver.resolve(&material);
                let bytes = bytemuck::bytes_of(&resolved.uniform);
                let buffer = match self.materials.remove(&handle) {
                    Some(m) => m.buffer,
                    None => device.create_buffer(&wgpu::BufferDescriptor {
                        label: Some("unbound material"),
                        size: bytes.len() as u64,
                        usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
                        mapped_at_creation: false,
                    }),
                };
                self.queue.write_buffer(&buffer, 0, bytes);
                self.materials.insert(
                    handle,
                    GpuMaterial {
                        resolved,
                        buffer,
                        bind_group: None,
                    },
                );
            }
            Command::ReleaseMaterial(handle) => {
                self.materials.remove(&handle);
            }
        }
    }

    fn sampler(&mut self, key: SamplerKey) -> wgpu::Sampler {
        let device = self.device.wgpu_device();
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
        let device = self.device.wgpu_device().clone();
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

    fn material_bind_group(&mut self, handle: u64, space: i32) -> Option<wgpu::BindGroup> {
        let (a, b) = {
            let m = self.materials.get(&handle)?;
            (m.resolved.slot_a.clone(), m.resolved.slot_b.clone())
        };
        let ta = self.texture_or_default(a.handle, &a.default);
        let tb = self.texture_or_default(b.handle, &b.default);
        let stamp = (space, ta.generation, tb.generation);
        if let Some((bg, built)) = &self.materials[&handle].bind_group
            && *built == stamp
        {
            return Some(bg.clone());
        }
        let (ka, kb) = (ta.sampler_key, tb.sampler_key);
        let sa = self.sampler(ka);
        let sb = self.sampler(kb);
        let ta = self.texture_or_default(a.handle, &a.default);
        let tb = self.texture_or_default(b.handle, &b.default);
        let m = &self.materials[&handle];
        let bg = self
            .device
            .wgpu_device()
            .create_bind_group(&wgpu::BindGroupDescriptor {
                label: Some("unbound material"),
                layout: &self.pipelines.material_layout,
                entries: &[
                    wgpu::BindGroupEntry {
                        binding: 0,
                        resource: m.buffer.as_entire_binding(),
                    },
                    wgpu::BindGroupEntry {
                        binding: 1,
                        resource: wgpu::BindingResource::TextureView(ta.view_for(space)),
                    },
                    wgpu::BindGroupEntry {
                        binding: 2,
                        resource: wgpu::BindingResource::Sampler(&sa),
                    },
                    wgpu::BindGroupEntry {
                        binding: 3,
                        resource: wgpu::BindingResource::TextureView(tb.view_for(space)),
                    },
                    wgpu::BindGroupEntry {
                        binding: 4,
                        resource: wgpu::BindingResource::Sampler(&sb),
                    },
                ],
            });
        self.materials.get_mut(&handle).unwrap().bind_group = Some((bg.clone(), stamp));
        Some(bg)
    }

    fn ensure_screen(&mut self, width: u32, height: u32, space: i32) {
        let format = if space == 1 {
            wgpu::TextureFormat::Rgba16Float
        } else {
            wgpu::TextureFormat::Rgba8Unorm
        };
        if let Some(s) = &self.screen
            && s.width == width
            && s.height == height
            && s.format == format
        {
            return;
        }
        let device = self.device.wgpu_device();
        let size = wgpu::Extent3d {
            width,
            height,
            depth_or_array_layers: 1,
        };
        let color = device
            .create_texture(&wgpu::TextureDescriptor {
                label: Some("unbound screen"),
                size,
                mip_level_count: 1,
                sample_count: 1,
                dimension: wgpu::TextureDimension::D2,
                format,
                usage: wgpu::TextureUsages::RENDER_ATTACHMENT
                    | wgpu::TextureUsages::TEXTURE_BINDING
                    | wgpu::TextureUsages::COPY_SRC,
                view_formats: &[],
            })
            .create_view(&Default::default());
        let depth = device
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
        self.screen = Some(ScreenTarget {
            width,
            height,
            format,
            color,
            depth,
            blit_bind_group: None,
        });
    }

    fn present_bind_group(
        &mut self,
        out_format: wgpu::TextureFormat,
        space: i32,
    ) -> Option<wgpu::BindGroup> {
        let srgb = out_format.is_srgb();
        let slot = usize::from(srgb);
        if self.blit_params[slot].is_none() {
            // Per output kind: the mode depends on the frame's color space too, so it is
            // rewritten below.
            self.blit_params[slot] = Some(self.device.wgpu_device().create_buffer(
                &wgpu::BufferDescriptor {
                    label: Some("unbound present params"),
                    size: 16,
                    usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
                    mapped_at_creation: false,
                },
            ));
        }
        let params = self.blit_params[slot].clone().unwrap();
        let mode: [f32; 4] = [
            if space == 0 { 1.0 } else { 0.0 },
            if srgb { 1.0 } else { 0.0 },
            0.0,
            0.0,
        ];
        self.queue
            .write_buffer(&params, 0, bytemuck::cast_slice(&mode));
        let screen = self.screen.as_mut()?;
        if let Some((format, bg)) = &screen.blit_bind_group
            && *format == out_format
        {
            return Some(bg.clone());
        }
        let bg = self
            .device
            .wgpu_device()
            .create_bind_group(&wgpu::BindGroupDescriptor {
                label: Some("unbound present"),
                layout: &self.pipelines.blit_layout,
                entries: &[
                    wgpu::BindGroupEntry {
                        binding: 0,
                        resource: wgpu::BindingResource::TextureView(&screen.color),
                    },
                    wgpu::BindGroupEntry {
                        binding: 1,
                        resource: wgpu::BindingResource::Sampler(&self.blit_sampler),
                    },
                    wgpu::BindGroupEntry {
                        binding: 2,
                        resource: params.as_entire_binding(),
                    },
                ],
            });
        screen.blit_bind_group = Some((out_format, bg.clone()));
        Some(bg)
    }

    /// Plans the latest frame.
    fn plan(&mut self, out_format: Option<wgpu::TextureFormat>) {
        let Some(frame) = self.frame.take() else {
            self.plan = None;
            return;
        };
        let space = frame.color_space.clamp(0, 1);
        let (sw, sh) = (
            frame.screen_width.max(1) as u32,
            frame.screen_height.max(1) as u32,
        );
        self.ensure_screen(sw, sh, space);
        self.uniforms.clear();
        let time = self.started.elapsed().as_secs_f32();
        let mut cameras = Vec::with_capacity(frame.cameras.len());
        let mut screen_touched = false;
        for camera in &frame.cameras {
            if let Some(planned) =
                self.plan_camera(&frame, camera, space, time, &mut screen_touched)
            {
                cameras.push(planned);
            }
        }
        if trace_render() {
            self.traced_frames += 1;
            if self.traced_frames % 60 == 1 {
                tracing::info!(
                    "[trace] renderer: {} cameras received, {} planned ({} draws), screen {}x{}, touched {}",
                    frame.cameras.len(),
                    cameras.len(),
                    cameras.iter().map(|c| c.draws.len()).sum::<usize>(),
                    sw,
                    sh,
                    screen_touched
                );
            }
        }
        if !screen_touched {
            // Nothing drew on the screen: present black.
            let screen = self.screen.as_ref().unwrap();
            cameras.push(PlannedCamera {
                color: screen.color.clone(),
                depth: Some(screen.depth.clone()),
                color_load: wgpu::LoadOp::Clear(wgpu::Color::BLACK),
                depth_load: false,
                viewport: [0.0, 0.0, sw as f32, sh as f32],
                clear: None,
                draws: Vec::new(),
            });
        }
        // Upload the frame's per-draw uniforms.
        let bytes = self.uniforms.len().max(1) as u64 * DRAW_SLOT;
        let device = self.device.wgpu_device().clone();
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
        if let Some(format) = out_format {
            self.pipelines.blit(&device, format);
        }
        let present = out_format.and_then(|f| self.present_bind_group(f, space));
        self.plan = Some(Plan { cameras, present });
        self.frame = Some(frame);
    }

    fn push_uniform(&mut self, u: DrawUniform) -> u32 {
        self.uniforms.push(u);
        ((self.uniforms.len() - 1) as u64 * DRAW_SLOT) as u32
    }

    fn plan_camera(
        &mut self,
        frame: &owned::Frame,
        camera: &CameraPacket,
        space: i32,
        time: f32,
        screen_touched: &mut bool,
    ) -> Option<PlannedCamera> {
        // The target: the screen or a render texture.
        let (color, depth, format, tw, th, first_use) = if camera.target_texture == 0 {
            let s = self.screen.as_ref().unwrap();
            let first = !*screen_touched;
            *screen_touched = true;
            (
                s.color.clone(),
                Some(s.depth.clone()),
                s.format,
                s.width,
                s.height,
                first,
            )
        } else {
            let Some(t) = self
                .textures
                .get(&camera.target_texture)
                .filter(|t| t.render_target)
            else {
                self.warn_once(format!("rt{}", camera.target_texture), || {
                    format!(
                        "a camera renders into texture {} which is not a render target; skipped",
                        camera.target_texture
                    )
                });
                return None;
            };
            let view = if space == 1 && t.srgb_view.is_some() {
                t.srgb_view.clone().unwrap()
            } else {
                t.view.clone()
            };
            (
                view,
                t.depth.clone(),
                t.target_format(space),
                t.width,
                t.height,
                false,
            )
        };
        let has_depth = depth.is_some();
        let vp = camera.viewport;
        let viewport = [vp.x, vp.y, vp.width.max(0.0), vp.height.max(0.0)];
        let full = vp.x <= 0.0
            && vp.y <= 0.0
            && vp.x + vp.width >= tw as f32
            && vp.y + vp.height >= th as f32;
        // CameraClearFlags: 1 skybox, 2 solid color, 3 depth, 4 nothing.
        let clear_color = matches!(camera.clear_flags, 1 | 2);
        let clear_depth = matches!(camera.clear_flags, 1..=3);
        let c = camera.clear_color;
        let clear_value = wgpu::Color {
            r: c.r as f64,
            g: c.g as f64,
            b: c.b as f64,
            a: c.a as f64,
        };
        let device = self.device.wgpu_device().clone();
        let mut color_load = wgpu::LoadOp::Load;
        let mut depth_load = true;
        let mut clear = None;
        if full {
            if clear_color {
                color_load = wgpu::LoadOp::Clear(clear_value);
            }
            if clear_depth {
                depth_load = false;
            }
        } else if clear_color || clear_depth {
            let pipeline = self.pipelines.clear(
                &device,
                ClearKey {
                    format,
                    depth: has_depth,
                    color: clear_color,
                    clear_depth,
                },
            );
            let offset = self.push_uniform(DrawUniform {
                color: [c.r, c.g, c.b, c.a],
                ..Zeroable::zeroed()
            });
            clear = Some((pipeline, offset));
        }
        if first_use {
            // The screen's previous content is undefined in Unity; start from black.
            if matches!(color_load, wgpu::LoadOp::Load) {
                color_load = wgpu::LoadOp::Clear(wgpu::Color::BLACK);
            }
            depth_load = false;
        }

        let screen = [
            tw as f32,
            th as f32,
            1.0 + 1.0 / tw as f32,
            1.0 + 1.0 / th as f32,
        ];
        let first = camera.first_draw.max(0) as usize;
        let count = camera.draw_count.max(0) as usize;
        let draws: &[DrawPacket] = frame
            .draws
            .get(first..(first + count).min(frame.draws.len()))
            .unwrap_or(&[]);
        let viewport_scissor = clamp_rect(viewport, tw, th);
        let mut planned = Vec::with_capacity(draws.len());
        for draw in draws {
            let Some(material) = self.materials.get(&draw.material) else {
                self.warn_once(format!("mat{}", draw.material), || {
                    format!("a draw uses unknown material {}; skipped", draw.material)
                });
                continue;
            };
            let (family, state, stencil_ref, main) = {
                let r = &material.resolved;
                (r.family, r.state, r.stencil_ref, r.main.clone())
            };
            let Some(mesh) = self.meshes.get(&draw.mesh) else {
                self.warn_once(format!("mesh{}", draw.mesh), || {
                    format!("a draw uses unknown mesh {}; skipped", draw.mesh)
                });
                continue;
            };
            let Some(sub) = mesh.submeshes.get(draw.submesh.max(0) as usize).cloned() else {
                self.warn_once(format!("sub{}/{}", draw.mesh, draw.submesh), || {
                    format!("a draw uses missing submesh {} of mesh {}; skipped", draw.submesh, draw.mesh)
                });
                continue;
            };
            if sub.count == 0 {
                continue;
            }
            let (vertex, index) = (mesh.vertex.clone(), mesh.index.clone());
            let scissor = if draw.clip_enabled != 0 {
                let clip = clamp_rect(
                    [draw.clip.x, draw.clip.y, draw.clip.width, draw.clip.height],
                    tw,
                    th,
                );
                intersect(clip, viewport_scissor)
            } else {
                viewport_scissor
            };
            if scissor[2] == 0 || scissor[3] == 0 {
                if trace_render() {
                    tracing::info!("[trace] draw of mesh {} clipped away (scissor {scissor:?})", draw.mesh);
                }
                continue;
            }
            let pipeline = self.pipelines.get(
                &device,
                PipelineKey {
                    family,
                    state,
                    topology: sub.topology,
                    format,
                    depth: has_depth,
                },
            );
            let Some(material_bg) = self.material_bind_group(draw.material, space) else {
                continue;
            };
            let main_handle = if draw.main_texture != 0 {
                draw.main_texture
            } else {
                main.handle
            };
            let (texture_bg, alpha_only) = self.main_bind_group(main_handle, &main.default, space);
            let offset = self.push_uniform(DrawUniform {
                model: draw.model.m,
                view: camera.view.m,
                proj: camera.projection.m,
                color: [draw.color.r, draw.color.g, draw.color.b, draw.color.a],
                screen,
                misc: [time, space as f32, if alpha_only { 1.0 } else { 0.0 }, 0.0],
                pad: [0.0; 4],
            });
            planned.push(PlannedDraw {
                pipeline,
                vertex,
                index,
                range: sub.start..sub.start + sub.count,
                base_vertex: sub.base_vertex,
                material: material_bg,
                texture: texture_bg,
                offset,
                stencil_ref,
                scissor,
            });
        }
        Some(PlannedCamera {
            color,
            depth,
            color_load,
            depth_load,
            viewport,
            clear,
            draws: planned,
        })
    }
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

fn extract_protocol(shared: Res<SharedBackend>, renderer: Option<ResMut<UnityRenderer>>) {
    let Some(mut renderer) = renderer else {
        return;
    };
    let (commands, frame) = shared.0.take();
    renderer.commands.extend(commands);
    if frame.is_some() {
        renderer.frame = frame;
    }
}

fn prepare_frame(renderer: Option<ResMut<UnityRenderer>>, views: Query<&ViewTarget>) {
    let Some(mut renderer) = renderer else {
        return;
    };
    let commands = std::mem::take(&mut renderer.commands);
    for command in commands {
        renderer.apply(command);
    }
    let out_format = views
        .iter()
        .next()
        .and_then(ViewTarget::out_texture_view_format);
    renderer.plan(out_format);
}

fn encode_frame(
    view: ViewQuery<&ViewTarget>,
    renderer: Option<Res<UnityRenderer>>,
    mut ctx: RenderContext,
) {
    let Some(renderer) = renderer else {
        return;
    };
    let target = view.into_inner();
    let Some(plan) = &renderer.plan else {
        return;
    };
    let Some(draw_bg) = &renderer.draw_bind_group else {
        return;
    };
    let encoder = ctx.command_encoder();
    for camera in &plan.cameras {
        let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
            label: Some("unbound camera"),
            color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                view: &camera.color,
                depth_slice: None,
                resolve_target: None,
                ops: wgpu::Operations {
                    load: camera.color_load,
                    store: wgpu::StoreOp::Store,
                },
            })],
            depth_stencil_attachment: camera.depth.as_ref().map(|depth| {
                wgpu::RenderPassDepthStencilAttachment {
                    view: depth,
                    depth_ops: Some(wgpu::Operations {
                        load: if camera.depth_load {
                            wgpu::LoadOp::Load
                        } else {
                            wgpu::LoadOp::Clear(1.0)
                        },
                        store: wgpu::StoreOp::Store,
                    }),
                    stencil_ops: Some(wgpu::Operations {
                        load: if camera.depth_load {
                            wgpu::LoadOp::Load
                        } else {
                            wgpu::LoadOp::Clear(0)
                        },
                        store: wgpu::StoreOp::Store,
                    }),
                }
            }),
            timestamp_writes: None,
            occlusion_query_set: None,
            multiview_mask: None,
        });
        let [vx, vy, vw, vh] = camera.viewport;
        if vw <= 0.0 || vh <= 0.0 {
            continue;
        }
        pass.set_viewport(vx, vy, vw, vh, 0.0, 1.0);
        if let Some((pipeline, offset)) = &camera.clear {
            pass.set_pipeline(pipeline);
            pass.set_bind_group(0, draw_bg, &[*offset]);
            pass.set_stencil_reference(0);
            pass.draw(0..3, 0..1);
        }
        for d in &camera.draws {
            pass.set_pipeline(&d.pipeline);
            pass.set_bind_group(0, draw_bg, &[d.offset]);
            pass.set_bind_group(1, &d.material, &[]);
            pass.set_bind_group(2, &d.texture, &[]);
            pass.set_vertex_buffer(0, d.vertex.slice(..));
            pass.set_index_buffer(d.index.slice(..), wgpu::IndexFormat::Uint32);
            pass.set_stencil_reference(d.stencil_ref);
            pass.set_scissor_rect(d.scissor[0], d.scissor[1], d.scissor[2], d.scissor[3]);
            pass.draw_indexed(d.range.clone(), d.base_vertex, 0..1);
        }
    }
    // Present on the window.
    let (Some(out), Some(format), Some(bind_group)) = (
        target.out_texture(),
        target.out_texture_view_format(),
        &plan.present,
    ) else {
        return;
    };
    let Some(pipeline) = renderer.pipelines.blit_cached(format) else {
        return;
    };
    let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
        label: Some("unbound present"),
        color_attachments: &[Some(wgpu::RenderPassColorAttachment {
            view: out,
            depth_slice: None,
            resolve_target: None,
            ops: wgpu::Operations {
                load: wgpu::LoadOp::Clear(wgpu::Color::BLACK),
                store: wgpu::StoreOp::Store,
            },
        })],
        depth_stencil_attachment: None,
        timestamp_writes: None,
        occlusion_query_set: None,
        multiview_mask: None,
    });
    pass.set_pipeline(&pipeline);
    pass.set_bind_group(0, bind_group, &[]);
    pass.draw(0..3, 0..1);
}

/// `UNBOUND_TRACE` includes `render`: the renderer reports what it skips.
fn trace_render() -> bool {
    static ON: std::sync::OnceLock<bool> = std::sync::OnceLock::new();
    *ON.get_or_init(|| {
        std::env::var("UNBOUND_TRACE")
            .map(|v| v == "all" || v.split(',').any(|c| c == "render" || c == "render-detail"))
            .unwrap_or(false)
    })
}
