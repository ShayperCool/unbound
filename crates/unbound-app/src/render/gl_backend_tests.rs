//! The OpenGL backend's capabilities the renderer relies on, on a real OpenGL device (skipped
//! without one): views in another format and of one layer, multisampled textures that are also
//! sampled, and the sampler LOD bias (vendor/wgpu-sampler/PATCHES.md, OpenGL device).
use wgpu::util::DeviceExt;

/// A device of wgpu's OpenGL backend, or None (no EGL, no OpenGL 4.5 driver).
fn gl_device() -> Option<(wgpu::Device, wgpu::Queue)> {
    let instance = wgpu::Instance::new(wgpu::InstanceDescriptor {
        backends: wgpu::Backends::GL,
        ..wgpu::InstanceDescriptor::new_without_display_handle_from_env()
    });
    let adapter = bevy::tasks::block_on(instance.enumerate_adapters(wgpu::Backends::GL))
        .into_iter()
        .find(|a| a.get_info().backend == wgpu::Backend::Gl)?;
    if !adapter.get_info().driver_info.starts_with("4.5") {
        eprintln!(
            "skipped: {:?} is not an OpenGL 4.5 context",
            adapter.get_info()
        );
        return None;
    }
    bevy::tasks::block_on(adapter.request_device(&wgpu::DeviceDescriptor {
        label: Some("gl backend test"),
        required_limits: adapter.limits(),
        ..Default::default()
    }))
    .ok()
}

const TARGET: wgpu::TextureFormat = wgpu::TextureFormat::Rgba8Unorm;

fn texture(
    device: &wgpu::Device,
    size: (u32, u32, u32),
    levels: u32,
    samples: u32,
    usage: wgpu::TextureUsages,
    view_formats: &[wgpu::TextureFormat],
) -> wgpu::Texture {
    device.create_texture(&wgpu::TextureDescriptor {
        label: Some("gl backend test"),
        size: wgpu::Extent3d {
            width: size.0,
            height: size.1,
            depth_or_array_layers: size.2,
        },
        mip_level_count: levels,
        sample_count: samples,
        dimension: wgpu::TextureDimension::D2,
        format: TARGET,
        usage,
        view_formats,
    })
}

/// Level 0, layer 0 of a 4x4 RGBA8 texture, row by row.
fn read(device: &wgpu::Device, queue: &wgpu::Queue, texture: &wgpu::Texture) -> Vec<[u8; 4]> {
    let buffer = device.create_buffer(&wgpu::BufferDescriptor {
        label: None,
        size: 256 * 4,
        usage: wgpu::BufferUsages::COPY_DST | wgpu::BufferUsages::MAP_READ,
        mapped_at_creation: false,
    });
    let mut encoder = device.create_command_encoder(&Default::default());
    encoder.copy_texture_to_buffer(
        texture.as_image_copy(),
        wgpu::TexelCopyBufferInfo {
            buffer: &buffer,
            layout: wgpu::TexelCopyBufferLayout {
                offset: 0,
                bytes_per_row: Some(256),
                rows_per_image: None,
            },
        },
        wgpu::Extent3d {
            width: 4,
            height: 4,
            depth_or_array_layers: 1,
        },
    );
    queue.submit([encoder.finish()]);
    buffer
        .slice(..)
        .map_async(wgpu::MapMode::Read, |r| r.unwrap());
    device.poll(wgpu::PollType::wait_indefinitely()).unwrap();
    let data = buffer.slice(..).get_mapped_range().unwrap();
    (0..16)
        .map(|i| {
            let at = (i / 4) * 256 + (i % 4) * 4;
            [data[at], data[at + 1], data[at + 2], data[at + 3]]
        })
        .collect()
}

fn clear(
    encoder: &mut wgpu::CommandEncoder,
    view: &wgpu::TextureView,
    resolve: Option<&wgpu::TextureView>,
    color: wgpu::Color,
) {
    encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
        color_attachments: &[Some(wgpu::RenderPassColorAttachment {
            view,
            depth_slice: None,
            resolve_target: resolve,
            ops: wgpu::Operations {
                load: wgpu::LoadOp::Clear(color),
                store: wgpu::StoreOp::Store,
            },
        })],
        ..Default::default()
    });
}

/// Draws a 4x4 target with `fragment` (WGSL, `fs`, group 0: binding 0 a texture, binding 1 a
/// sampler) over `view` and `sampler`.
fn draw(
    device: &wgpu::Device,
    queue: &wgpu::Queue,
    fragment: &str,
    view: &wgpu::TextureView,
    sampler: &wgpu::Sampler,
) -> Vec<[u8; 4]> {
    let source = format!(
        "@group(0) @binding(0) var t: texture_2d<f32>;\n@group(0) @binding(1) var s: sampler;\n\
         @vertex fn vs(@builtin(vertex_index) i: u32) -> @builtin(position) vec4<f32> {{\n\
         let uv = vec2<f32>(f32((i << 1u) & 2u), f32(i & 2u));\n\
         return vec4<f32>(uv * 2.0 - 1.0, 0.0, 1.0);\n}}\n{fragment}"
    );
    let module = device.create_shader_module(wgpu::ShaderModuleDescriptor {
        label: None,
        source: wgpu::ShaderSource::Wgsl(source.into()),
    });
    let pipeline = device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
        label: None,
        layout: None,
        vertex: wgpu::VertexState {
            module: &module,
            entry_point: Some("vs"),
            compilation_options: Default::default(),
            buffers: &[],
        },
        fragment: Some(wgpu::FragmentState {
            module: &module,
            entry_point: Some("fs"),
            compilation_options: Default::default(),
            targets: &[Some(TARGET.into())],
        }),
        primitive: Default::default(),
        depth_stencil: None,
        multisample: Default::default(),
        multiview_mask: None,
        cache: None,
    });
    let group = device.create_bind_group(&wgpu::BindGroupDescriptor {
        label: None,
        layout: &pipeline.get_bind_group_layout(0),
        entries: &[
            wgpu::BindGroupEntry {
                binding: 0,
                resource: wgpu::BindingResource::TextureView(view),
            },
            wgpu::BindGroupEntry {
                binding: 1,
                resource: wgpu::BindingResource::Sampler(sampler),
            },
        ],
    });
    let target = texture(
        device,
        (4, 4, 1),
        1,
        1,
        wgpu::TextureUsages::RENDER_ATTACHMENT | wgpu::TextureUsages::COPY_SRC,
        &[],
    );
    let target_view = target.create_view(&Default::default());
    let mut encoder = device.create_command_encoder(&Default::default());
    {
        let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
            color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                view: &target_view,
                depth_slice: None,
                resolve_target: None,
                ops: wgpu::Operations {
                    load: wgpu::LoadOp::Clear(wgpu::Color::BLACK),
                    store: wgpu::StoreOp::Store,
                },
            })],
            ..Default::default()
        });
        pass.set_pipeline(&pipeline);
        pass.set_bind_group(0, &group, &[]);
        pass.draw(0..3, 0..1);
    }
    queue.submit([encoder.finish()]);
    read(device, queue, &target)
}

#[test]
fn an_srgb_view_of_a_linear_texture_encodes_what_it_renders() {
    let Some((device, queue)) = gl_device() else {
        return;
    };
    let usage = wgpu::TextureUsages::RENDER_ATTACHMENT | wgpu::TextureUsages::COPY_SRC;
    let srgb = [wgpu::TextureFormat::Rgba8UnormSrgb];
    let encoded = texture(&device, (4, 4, 1), 1, 1, usage, &srgb);
    let plain = texture(&device, (4, 4, 1), 1, 1, usage, &srgb);
    // sRGB 128/255 in linear (alpha is never encoded).
    let gray = wgpu::Color {
        r: 0.215_860_5,
        g: 0.215_860_5,
        b: 0.215_860_5,
        a: 1.0,
    };
    let mut encoder = device.create_command_encoder(&Default::default());
    let srgb_view = encoded.create_view(&wgpu::TextureViewDescriptor {
        format: Some(wgpu::TextureFormat::Rgba8UnormSrgb),
        ..Default::default()
    });
    clear(&mut encoder, &srgb_view, None, gray);
    // The control: the texture's own format stores the value as it is.
    clear(
        &mut encoder,
        &plain.create_view(&Default::default()),
        None,
        gray,
    );
    queue.submit([encoder.finish()]);
    assert_eq!(read(&device, &queue, &encoded)[5], [128, 128, 128, 255]);
    assert_eq!(read(&device, &queue, &plain)[5], [55, 55, 55, 255]);
}

#[test]
fn a_view_of_one_layer_samples_that_layer() {
    let Some((device, queue)) = gl_device() else {
        return;
    };
    let layers = texture(
        &device,
        (4, 4, 2),
        1,
        1,
        wgpu::TextureUsages::TEXTURE_BINDING | wgpu::TextureUsages::COPY_DST,
        &[],
    );
    let mut texels = [255u8, 0, 0, 255].repeat(16);
    texels.extend([0u8, 255, 0, 255].repeat(16));
    queue.write_texture(
        layers.as_image_copy(),
        &texels,
        wgpu::TexelCopyBufferLayout {
            offset: 0,
            bytes_per_row: Some(16),
            rows_per_image: Some(4),
        },
        wgpu::Extent3d {
            width: 4,
            height: 4,
            depth_or_array_layers: 2,
        },
    );
    let sampler = device.create_sampler(&Default::default());
    let load = "@fragment fn fs(@builtin(position) p: vec4<f32>) -> @location(0) vec4<f32> {\n\
                return textureLoad(t, vec2<i32>(p.xy), 0) + 0.0 * textureSample(t, s, p.xy);\n}";
    for (layer, expected) in [(1, [0, 255, 0, 255]), (0, [255, 0, 0, 255])] {
        let view = layers.create_view(&wgpu::TextureViewDescriptor {
            dimension: Some(wgpu::TextureViewDimension::D2),
            base_array_layer: layer,
            array_layer_count: Some(1),
            ..Default::default()
        });
        assert_eq!(draw(&device, &queue, load, &view, &sampler)[5], expected);
    }
}

#[test]
fn a_sampled_multisampled_texture_renders_and_resolves() {
    let Some((device, queue)) = gl_device() else {
        return;
    };
    let msaa = texture(
        &device,
        (4, 4, 1),
        1,
        4,
        wgpu::TextureUsages::RENDER_ATTACHMENT | wgpu::TextureUsages::TEXTURE_BINDING,
        &[],
    );
    let resolved = texture(
        &device,
        (4, 4, 1),
        1,
        1,
        wgpu::TextureUsages::RENDER_ATTACHMENT | wgpu::TextureUsages::COPY_SRC,
        &[],
    );
    let mut encoder = device.create_command_encoder(&Default::default());
    clear(
        &mut encoder,
        &msaa.create_view(&Default::default()),
        Some(&resolved.create_view(&Default::default())),
        wgpu::Color::BLUE,
    );
    queue.submit([encoder.finish()]);
    assert_eq!(read(&device, &queue, &resolved)[5], [0, 0, 255, 255]);
}

#[test]
fn the_sampler_lod_bias_selects_a_coarser_level() {
    let Some((device, queue)) = gl_device() else {
        return;
    };
    let limit = device
        .native_sampler_lod_bias_limit()
        .expect("desktop OpenGL has a LOD bias");
    assert!(limit >= 1.0);
    // Level 0 red, level 1 green, level 2 blue.
    let levels = device.create_texture_with_data(
        &queue,
        &wgpu::TextureDescriptor {
            label: None,
            size: wgpu::Extent3d {
                width: 4,
                height: 4,
                depth_or_array_layers: 1,
            },
            mip_level_count: 3,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format: TARGET,
            usage: wgpu::TextureUsages::TEXTURE_BINDING,
            view_formats: &[],
        },
        wgpu::util::TextureDataOrder::LayerMajor,
        &[
            [255u8, 0, 0, 255].repeat(16),
            [0u8, 255, 0, 255].repeat(4),
            vec![0u8, 0, 255, 255],
        ]
        .concat(),
    );
    let view = levels.create_view(&Default::default());
    let nearest = wgpu::SamplerDescriptor {
        mipmap_filter: wgpu::MipmapFilterMode::Nearest,
        ..Default::default()
    };
    // One texel per pixel: level 0 without a bias, level 1 with +1, level 0 again with -1.
    let sample = "@fragment fn fs(@builtin(position) p: vec4<f32>) -> @location(0) vec4<f32> {\n\
                  return textureSample(t, s, p.xy / 4.0);\n}";
    for (bias, expected) in [
        (0.0, [255, 0, 0, 255]),
        (1.0, [0, 255, 0, 255]),
        (-1.0, [255, 0, 0, 255]),
    ] {
        let sampler = device
            .create_sampler_with_lod_bias(&nearest, bias)
            .expect("a biased sampler");
        assert_eq!(draw(&device, &queue, sample, &view, &sampler)[5], expected);
    }
}
