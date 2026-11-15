//! The renderer. The managed engine's rendering commands execute on the GPU as they are submitted
//! (`executor`, on the game's thread, through `GpuBackend`); the window's render schedule
//! (`UnityFrameGraph`) only shows the latest complete frame.
pub mod compiled;
pub mod device;
pub mod dots;
pub mod executor;
#[cfg(all(test, target_os = "linux"))]
mod gl_backend_tests;
pub mod gl_interop;
pub mod pipelines;
pub mod resources;

use crate::backend::GpuBackend;
use bevy::{
    ecs::schedule::{ScheduleBuildSettings, ScheduleLabel},
    prelude::*,
    render::{
        Render, RenderApp, RenderSystems,
        renderer::{RenderAdapter, RenderContext, RenderDevice, RenderQueue, ViewQuery},
        view::{ExtractedWindow, ViewTarget},
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

/// A capture of the window's image ([`WindowCaptures`]).
struct WindowCapture {
    /// The frame captured: the latest complete one when it was asked for (none before the
    /// first: the window then shows black).
    frame: Option<Presented>,
    /// The PNG file written.
    path: PathBuf,
    /// Called once the file is written (or failed to be).
    written: Box<dyn FnOnce() + Send>,
}

/// Captures of the window's image the main world asks for: each is the latest complete frame
/// when it is asked for, as the window shows it (its size and image format, the fixed Game
/// view's rectangle), drawn apart from the window. A window macOS reports covered gets no image
/// of its own to read (no surface texture is acquired while it is occluded), so a capture of the
/// window's own image would be black there.
#[derive(Resource, Clone, Default)]
pub struct WindowCaptures {
    pending: Arc<Mutex<Vec<WindowCapture>>>,
    /// The latest complete frame (the executor's `Presented`).
    latest: Arc<Mutex<Option<Presented>>>,
}

impl WindowCaptures {
    /// Asks for the image of the latest complete frame as a PNG file at `path`; `written` is
    /// called once it is written or has failed.
    pub fn request(&self, path: PathBuf, written: impl FnOnce() + Send + 'static) {
        let frame = self.latest.lock().unwrap().clone();
        self.pending.lock().unwrap().push(WindowCapture {
            frame,
            path,
            written: Box::new(written),
        });
    }
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
    current: Option<(wgpu::RenderPipeline, wgpu::BindGroup, (u32, u32), bool)>,
    /// The window's size and image format this frame.
    output: Option<((u32, u32), wgpu::TextureFormat)>,
}

impl Plugin for UnityRenderPlugin {
    fn build(&self, app: &mut App) {
        let captures = WindowCaptures::default();
        app.insert_resource(captures.clone());
        let Some(render_app) = app.get_sub_app_mut(RenderApp) else {
            return;
        };
        render_app.insert_resource(captures);
        let mut schedule = Schedule::new(UnityFrameGraph);
        schedule.set_build_settings(ScheduleBuildSettings {
            auto_insert_apply_deferred: false,
            ..Default::default()
        });
        render_app
            .add_schedule(schedule)
            .add_systems(
                Render,
                (prepare_present, capture_window)
                    .chain()
                    .in_set(RenderSystems::PrepareResources),
            )
            .add_systems(UnityFrameGraph, encode_present);
    }

    fn finish(&self, app: &mut App) {
        let Some(render_app) = app.get_sub_app_mut(RenderApp) else {
            return;
        };
        let device = render_app.world().resource::<RenderDevice>().clone();
        let queue = render_app.world().resource::<RenderQueue>().clone();
        let resolver = self.backend.take_resolver(&self.shader_roots);
        let latest = render_app
            .world()
            .resource::<WindowCaptures>()
            .latest
            .clone();
        unbound_runtime::state::set_gpu_bc7(
            device
                .wgpu_device()
                .features()
                .contains(wgpu::Features::TEXTURE_COMPRESSION_BC),
        );
        // What the device does with the normalized 16-bit and 10-10-10-2 formats (its features
        // are the adapter's: WgpuSettings' default priority asks for all of them).
        resources::set_norm_format_caps(resources::NormFormatCaps::query(
            render_app.world().resource::<RenderAdapter>(),
            device.wgpu_device().features(),
        ));
        let wgpu_queue: wgpu::Queue = std::ops::Deref::deref(&queue).clone();
        self.backend.install(UnityRenderer::new(
            (**render_app.world().resource::<RenderAdapter>()).clone(),
            device.wgpu_device().clone(),
            wgpu_queue,
            resolver,
            compiled::CompiledPrograms::new(self.shader_compiler.clone()),
            latest.clone(),
            device::DeviceConventions::of(unbound_runtime::graphics_device::get()),
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
            output: None,
        });
    }
}

/// Picks the frame to show and its present pipeline and bind group, for the window's image
/// format (its surface's, viewed as sRGB as the window's camera views it), known also while the
/// window is covered.
fn prepare_present(
    presenter: Option<ResMut<Presenter>>,
    windows: Query<&ExtractedWindow, With<bevy::window::PrimaryWindow>>,
) {
    let Some(mut presenter) = presenter else {
        return;
    };
    let presenter = &mut *presenter;
    presenter.current = None;
    presenter.output = None;
    let Ok(window) = windows.single() else {
        return;
    };
    let Some(format) = window.swap_chain_texture_view_format.or(window
        .swap_chain_texture_format
        .map(|f| f.add_srgb_suffix()))
    else {
        return;
    };
    presenter.output = Some(((window.physical_width, window.physical_height), format));
    let Some(frame) = presenter.latest.lock().unwrap().clone() else {
        return;
    };
    let device = presenter.device.wgpu_device().clone();
    let pipeline = presenter.pipelines.blit(&device, format);
    let srgb = format.is_srgb();
    let params = presenter.params[usize::from(srgb)]
        .get_or_insert_with(|| present_params(&device))
        .clone();
    presenter.queue.write_buffer(
        &params,
        0,
        bytemuck::cast_slice(&present_mode(&frame, srgb)),
    );
    let bind_group = present_bind_group(presenter, &frame, &params);
    presenter.current = Some((pipeline, bind_group, frame.size, frame.fixed_game_view));
}

/// The present blit's parameters buffer.
fn present_params(device: &wgpu::Device) -> wgpu::Buffer {
    device.create_buffer(&wgpu::BufferDescriptor {
        label: Some("unbound present params"),
        size: 16,
        usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
        mapped_at_creation: false,
    })
}

/// The present blit's parameters for a frame on an sRGB output or not: gamma frames hold gamma
/// values, which the blit linearizes for an sRGB output.
fn present_mode(frame: &Presented, srgb: bool) -> [f32; 4] {
    [
        if frame.color_space == 0 { 1.0 } else { 0.0 },
        if srgb { 1.0 } else { 0.0 },
        if frame.top_down { 1.0 } else { 0.0 },
        0.0,
    ]
}

/// The present blit's bind group for a frame and parameters buffer.
fn present_bind_group(
    presenter: &Presenter,
    frame: &Presented,
    params: &wgpu::Buffer,
) -> wgpu::BindGroup {
    presenter
        .device
        .wgpu_device()
        .create_bind_group(&wgpu::BindGroupDescriptor {
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
        })
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
    let Some((pipeline, bind_group, screen, fixed_game_view)) = &presenter.current else {
        return;
    };
    // Taken as Bevy's output attachment, which marks the window's texture as drawn: Bevy only
    // presents windows whose texture was (otherwise it keeps showing the first frame).
    let Some(out) = target.out_texture_color_attachment(Some(LinearRgba::BLACK)) else {
        return;
    };
    let encoder = ctx.command_encoder();
    let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
        label: Some("unbound present"),
        color_attachments: &[Some(out)],
        depth_stencil_attachment: None,
        timestamp_writes: None,
        occlusion_query_set: None,
        multiview_mask: None,
    });
    let size = target.main_texture().size();
    draw_present(
        &mut pass,
        (pipeline, bind_group),
        (size.width, size.height),
        *screen,
        *fixed_game_view,
    );
}

/// Draws the presented frame into a pass on an output of `output` pixels: the whole output, or
/// the fixed Game view's rectangle of it.
fn draw_present(
    pass: &mut wgpu::RenderPass,
    (pipeline, bind_group): (&wgpu::RenderPipeline, &wgpu::BindGroup),
    output: (u32, u32),
    screen: (u32, u32),
    fixed_game_view: bool,
) {
    let rect = presentation_rect(output, screen, fixed_game_view);
    let width = output.0 as f32;
    let height = output.1 as f32;
    let x = rect[0].round_ties_even().clamp(0.0, width);
    let y = rect[1].round_ties_even().clamp(0.0, height);
    let w = rect[2].min(width - x);
    let h = rect[3].min(height - y);
    if w <= 0.0 || h <= 0.0 {
        return;
    }
    pass.set_viewport(x, y, w, h, 0.0, 1.0);
    pass.set_pipeline(pipeline);
    pass.set_bind_group(0, bind_group, &[]);
    pass.draw(0..3, 0..1);
}

/// Draws the window captures asked for since the last render ([`WindowCaptures`]): each one's
/// frame, as on the window, into a texture of the window's size and image format, read back and
/// written as PNG off the render thread. One asked for before any frame was complete is the
/// window's image then: black. Before the window's image format is known they wait.
fn capture_window(presenter: Option<ResMut<Presenter>>, captures: Res<WindowCaptures>) {
    let Some(mut presenter) = presenter else {
        return;
    };
    let Some((size, format)) = presenter.output else {
        return;
    };
    let pending = std::mem::take(&mut *captures.pending.lock().unwrap());
    for capture in pending {
        let frame = capture.frame.clone();
        capture_frame(&mut presenter, frame.as_ref(), size, format, capture);
    }
}

/// Draws one capture of `frame` as the window shows it on an output of `size` and `format`
/// (black without a frame).
fn capture_frame(
    presenter: &mut Presenter,
    frame: Option<&Presented>,
    size: (u32, u32),
    format: wgpu::TextureFormat,
    capture: WindowCapture,
) {
    let device = presenter.device.wgpu_device().clone();
    let (width, height) = (size.0.max(1), size.1.max(1));
    let extent = wgpu::Extent3d {
        width,
        height,
        depth_or_array_layers: 1,
    };
    let texture = device.create_texture(&wgpu::TextureDescriptor {
        label: Some("unbound window capture"),
        size: extent,
        mip_level_count: 1,
        sample_count: 1,
        dimension: wgpu::TextureDimension::D2,
        format,
        usage: wgpu::TextureUsages::RENDER_ATTACHMENT | wgpu::TextureUsages::COPY_SRC,
        view_formats: &[],
    });
    let row_bytes = (width * 4).next_multiple_of(wgpu::COPY_BYTES_PER_ROW_ALIGNMENT);
    let buffer = device.create_buffer(&wgpu::BufferDescriptor {
        label: Some("unbound window capture"),
        size: u64::from(row_bytes) * u64::from(height),
        usage: wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST,
        mapped_at_creation: false,
    });
    // Parameters of its own: the window's present this render keeps its own.
    let blit = frame.map(|frame| {
        let params = present_params(&device);
        presenter.queue.write_buffer(
            &params,
            0,
            bytemuck::cast_slice(&present_mode(frame, format.is_srgb())),
        );
        let pipeline = presenter.pipelines.blit(&device, format);
        (
            pipeline,
            present_bind_group(presenter, frame, &params),
            frame,
        )
    });
    let mut encoder = device.create_command_encoder(&wgpu::CommandEncoderDescriptor {
        label: Some("unbound window capture"),
    });
    {
        let view = texture.create_view(&wgpu::TextureViewDescriptor::default());
        let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
            label: Some("unbound window capture"),
            color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                view: &view,
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
        if let Some((pipeline, bind_group, frame)) = &blit {
            draw_present(
                &mut pass,
                (pipeline, bind_group),
                (width, height),
                frame.size,
                frame.fixed_game_view,
            );
        }
    }
    encoder.copy_texture_to_buffer(
        texture.as_image_copy(),
        wgpu::TexelCopyBufferInfo {
            buffer: &buffer,
            layout: wgpu::TexelCopyBufferLayout {
                offset: 0,
                bytes_per_row: Some(row_bytes),
                rows_per_image: Some(height),
            },
        },
        extent,
    );
    presenter.queue.submit([encoder.finish()]);
    let mapped = buffer.clone();
    buffer
        .slice(..)
        .map_async(wgpu::MapMode::Read, move |result| {
            // Encoding a PNG is no work for the thread that polls the device.
            std::thread::spawn(move || {
                let written = result.map_err(anyhow::Error::from).and_then(|()| {
                    let data = mapped
                        .slice(..)
                        .get_mapped_range()
                        .map_err(|e| anyhow::anyhow!("{e}"))?;
                    write_png(
                        &rgba_rows(&data, width, height, row_bytes, format)?,
                        &capture.path,
                    )
                });
                match written {
                    Ok(()) => tracing::info!("screenshot written to {}", capture.path.display()),
                    Err(e) => {
                        tracing::error!("writing the screenshot {}: {e:#}", capture.path.display())
                    }
                }
                (capture.written)();
            });
        });
}

/// A window image read back (rows `row_bytes` apart, RGBA or BGRA, 8 bits) as an RGBA image.
fn rgba_rows(
    data: &[u8],
    width: u32,
    height: u32,
    row_bytes: u32,
    format: wgpu::TextureFormat,
) -> anyhow::Result<image::RgbaImage> {
    use wgpu::TextureFormat as F;
    let bgra = match format {
        F::Bgra8Unorm | F::Bgra8UnormSrgb => true,
        F::Rgba8Unorm | F::Rgba8UnormSrgb => false,
        other => anyhow::bail!("unsupported window image format {other:?}"),
    };
    let mut rgba = Vec::with_capacity((width * height * 4) as usize);
    for row in data.chunks(row_bytes as usize).take(height as usize) {
        for px in row[..(width * 4) as usize].as_chunks::<4>().0 {
            let [r, g, b] = if bgra {
                [px[2], px[1], px[0]]
            } else {
                [px[0], px[1], px[2]]
            };
            rgba.extend_from_slice(&[r, g, b, 255]);
        }
    }
    image::RgbaImage::from_raw(width, height, rgba)
        .ok_or_else(|| anyhow::anyhow!("the window image's size does not match its data"))
}

fn write_png(image: &image::RgbaImage, path: &std::path::Path) -> anyhow::Result<()> {
    if let Some(parent) = path.parent()
        && !parent.as_os_str().is_empty()
    {
        std::fs::create_dir_all(parent)?;
    }
    image.save_with_format(path, image::ImageFormat::Png)?;
    Ok(())
}

/// Presentation follows host resizes even when rendering retains an older screen frame.
/// The fixed-view rule matches Screen.PresentationRect, in physical pixels on both sides.
fn presentation_rect(output: (u32, u32), screen: (u32, u32), fixed: bool) -> [f32; 4] {
    let (width, height) = (output.0 as f32, output.1 as f32);
    if !fixed {
        return [0.0, 0.0, width, height];
    }
    let (sw, sh) = (screen.0.max(1) as f32, screen.1.max(1) as f32);
    let scale = (width / sw).min(height / sh);
    let scale = if scale > 1.0 { scale.floor() } else { scale };
    let (w, h) = (sw * scale, sh * scale);
    [(width - w) * 0.5, (height - h) * 0.5, w, h]
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A read-back window image is its rows without their padding, BGRA or RGBA as RGBA, opaque;
    /// another format is refused.
    #[test]
    fn window_images_read_back_as_opaque_rgba_rows() {
        // Two rows of two pixels, each row padded to eight bytes beyond its pixels.
        let rows: Vec<u8> = [
            [1, 2, 3, 4, 5, 6, 7, 8],
            [9, 9, 9, 9, 9, 9, 9, 9],
            [11, 12, 13, 14, 15, 16, 17, 18],
            [9, 9, 9, 9, 9, 9, 9, 9],
        ]
        .concat();
        let bgra = rgba_rows(&rows, 2, 2, 16, wgpu::TextureFormat::Bgra8UnormSrgb).unwrap();
        assert_eq!(
            bgra.into_raw(),
            [3, 2, 1, 255, 7, 6, 5, 255, 13, 12, 11, 255, 17, 16, 15, 255]
        );
        let rgba = rgba_rows(&rows, 2, 2, 16, wgpu::TextureFormat::Rgba8Unorm).unwrap();
        assert_eq!(
            rgba.into_raw(),
            [1, 2, 3, 255, 5, 6, 7, 255, 11, 12, 13, 255, 15, 16, 17, 255]
        );
        assert!(rgba_rows(&rows, 2, 2, 16, wgpu::TextureFormat::Rgba16Float).is_err());
    }
}
