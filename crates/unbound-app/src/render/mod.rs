//! The renderer. The managed engine's rendering commands execute on the GPU as they are submitted
//! (`executor`, on the game's thread, through `GpuBackend`); the window's render schedule
//! (`UnityFrameGraph`) only shows the latest complete frame.
pub mod compiled;
pub mod executor;
pub mod pipelines;
pub mod resources;

use crate::{backend::GpuBackend, materials::Resolver, shaderlab::ShaderIndex};
use bevy::{
    ecs::schedule::{ScheduleBuildSettings, ScheduleLabel},
    prelude::*,
    render::{
        Render, RenderApp, RenderSystems,
        renderer::{RenderContext, RenderDevice, RenderQueue, ViewQuery},
        view::ViewTarget,
    },
};
use executor::{Presented, UnityRenderer};
use pipelines::Pipelines;
use std::{
    path::PathBuf,
    sync::{Arc, Mutex},
};

/// The render schedule of the window's camera.
#[derive(ScheduleLabel, Debug, Clone, PartialEq, Eq, Hash, Default)]
pub struct UnityFrameGraph;

pub struct UnityRenderPlugin {
    pub backend: Arc<GpuBackend>,
    /// Where `.shader` files are: Assets and package roots, then Unity's built-in shaders.
    pub shader_roots: Vec<PathBuf>,
    /// How shader programs compile (none: nothing draws).
    pub shader_compiler: Option<unbound_shaders::Settings>,
}

/// Shows the latest complete frame on the window.
#[derive(Resource)]
struct Presenter {
    device: RenderDevice,
    queue: RenderQueue,
    latest: Arc<Mutex<Option<Presented>>>,
    pipelines: Pipelines,
    sampler: wgpu::Sampler,
    /// Per output kind (non-sRGB, sRGB): the present mode parameters.
    params: [Option<wgpu::Buffer>; 2],
    /// What this frame presents.
    current: Option<(wgpu::RenderPipeline, wgpu::BindGroup)>,
}

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
            .add_schedule(schedule)
            .add_systems(
                Render,
                prepare_present.in_set(RenderSystems::PrepareResources),
            )
            .add_systems(UnityFrameGraph, encode_present);
    }

    fn finish(&self, app: &mut App) {
        let Some(render_app) = app.get_sub_app_mut(RenderApp) else {
            return;
        };
        let device = render_app.world().resource::<RenderDevice>().clone();
        let queue = render_app.world().resource::<RenderQueue>().clone();
        let index = ShaderIndex::scan(&self.shader_roots);
        if !self.shader_roots.is_empty() {
            tracing::info!("indexed {} shaders", index.len());
        }
        let latest = Arc::new(Mutex::new(None));
        let wgpu_queue: wgpu::Queue = std::ops::Deref::deref(&queue).clone();
        self.backend.install(UnityRenderer::new(
            device.wgpu_device().clone(),
            wgpu_queue,
            Resolver::new(index),
            compiled::CompiledPrograms::new(self.shader_compiler.clone()),
            latest.clone(),
        ));
        let sampler = device
            .wgpu_device()
            .create_sampler(&wgpu::SamplerDescriptor {
                label: Some("unbound present"),
                mag_filter: wgpu::FilterMode::Linear,
                min_filter: wgpu::FilterMode::Linear,
                ..Default::default()
            });
        let pipelines = Pipelines::new(device.wgpu_device());
        render_app.insert_resource(Presenter {
            device,
            queue,
            latest,
            pipelines,
            sampler,
            params: [None, None],
            current: None,
        });
    }
}

/// Picks the frame to show and its present pipeline and bind group.
fn prepare_present(presenter: Option<ResMut<Presenter>>, views: Query<&ViewTarget>) {
    let Some(mut presenter) = presenter else {
        return;
    };
    let presenter = &mut *presenter;
    presenter.current = None;
    let Some(format) = views
        .iter()
        .next()
        .and_then(ViewTarget::out_texture_view_format)
    else {
        return;
    };
    let Some(frame) = presenter.latest.lock().unwrap().clone() else {
        return;
    };
    let device = presenter.device.wgpu_device().clone();
    let pipeline = presenter.pipelines.blit(&device, format);
    let srgb = format.is_srgb();
    let slot = usize::from(srgb);
    let params = presenter.params[slot]
        .get_or_insert_with(|| {
            device.create_buffer(&wgpu::BufferDescriptor {
                label: Some("unbound present params"),
                size: 16,
                usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
                mapped_at_creation: false,
            })
        })
        .clone();
    // Gamma frames hold gamma values: the blit linearizes them for an sRGB output.
    let mode: [f32; 4] = [
        if frame.color_space == 0 { 1.0 } else { 0.0 },
        if srgb { 1.0 } else { 0.0 },
        0.0,
        0.0,
    ];
    presenter
        .queue
        .write_buffer(&params, 0, bytemuck::cast_slice(&mode));
    let bind_group = device.create_bind_group(&wgpu::BindGroupDescriptor {
        label: Some("unbound present"),
        layout: &presenter.pipelines.blit_layout,
        entries: &[
            wgpu::BindGroupEntry {
                binding: 0,
                resource: wgpu::BindingResource::TextureView(&frame.color),
            },
            wgpu::BindGroupEntry {
                binding: 1,
                resource: wgpu::BindingResource::Sampler(&presenter.sampler),
            },
            wgpu::BindGroupEntry {
                binding: 2,
                resource: params.as_entire_binding(),
            },
        ],
    });
    presenter.current = Some((pipeline, bind_group));
}

fn encode_present(
    view: ViewQuery<&ViewTarget>,
    presenter: Option<Res<Presenter>>,
    mut ctx: RenderContext,
) {
    let Some(presenter) = presenter else {
        return;
    };
    let target = view.into_inner();
    let (Some(out), Some((pipeline, bind_group))) = (target.out_texture(), &presenter.current)
    else {
        return;
    };
    let encoder = ctx.command_encoder();
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
    pass.set_pipeline(pipeline);
    pass.set_bind_group(0, bind_group, &[]);
    pass.draw(0..3, 0..1);
}
