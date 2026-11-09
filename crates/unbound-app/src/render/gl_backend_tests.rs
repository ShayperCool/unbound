//! The OpenGL backend's capabilities the renderer relies on, on a real OpenGL device (skipped
//! without an EGL desktop OpenGL one): its 4.5 context, views in another format and of one layer,
//! textures with view formats that are only drawn into, multisampled textures that are also
//! sampled, the sampler LOD bias and the EGL context a thread had current
//! (vendor/wgpu-sampler/PATCHES.md, OpenGL device).
use wgpu::hal::api::Gles;
use wgpu::util::DeviceExt;

/// A device of wgpu's OpenGL backend on desktop OpenGL, which must be a 4.5 context; None
/// without one (no EGL, OpenGL ES only).
fn gl_device() -> Option<(wgpu::Device, wgpu::Queue)> {
    let instance = wgpu::Instance::new(wgpu::InstanceDescriptor {
        backends: wgpu::Backends::GL,
        ..wgpu::InstanceDescriptor::new_without_display_handle_from_env()
    });
    let adapter = bevy::tasks::block_on(instance.enumerate_adapters(wgpu::Backends::GL))
        .into_iter()
        .find(|a| a.get_info().backend == wgpu::Backend::Gl)?;
    let info = adapter.get_info();
    if info.driver_info.starts_with("OpenGL ES") {
        eprintln!("skipped: {info:?} is OpenGL ES");
        return None;
    }
    let version: Vec<u32> = info
        .driver_info
        .split(|c: char| !c.is_ascii_digit())
        .take(2)
        .map(|n| n.parse().unwrap_or(0))
        .collect();
    assert!(
        version >= vec![4, 5],
        "a desktop OpenGL context below 4.5: {info:?}"
    );
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

/// What a draw's fragment shader reads at binding 0 (and, when it samples, a sampler at 1).
struct Source<'a> {
    view: &'a wgpu::TextureView,
    sample_type: wgpu::TextureSampleType,
    multisampled: bool,
    sampler: Option<&'a wgpu::Sampler>,
}

/// Draws a 4x4 target with `fragment` (WGSL: `fs`, reading `t` and maybe `s`, declared by
/// `declarations`) over `source`.
fn draw(
    device: &wgpu::Device,
    queue: &wgpu::Queue,
    declarations: &str,
    fragment: &str,
    source: Source,
) -> Vec<[u8; 4]> {
    let code = format!(
        "{declarations}\n\
         @vertex fn vs(@builtin(vertex_index) i: u32) -> @builtin(position) vec4<f32> {{\n\
         let uv = vec2<f32>(f32((i << 1u) & 2u), f32(i & 2u));\n\
         return vec4<f32>(uv * 2.0 - 1.0, 0.0, 1.0);\n}}\n{fragment}"
    );
    let module = device.create_shader_module(wgpu::ShaderModuleDescriptor {
        label: None,
        source: wgpu::ShaderSource::Wgsl(code.into()),
    });
    let mut entries = vec![wgpu::BindGroupLayoutEntry {
        binding: 0,
        visibility: wgpu::ShaderStages::FRAGMENT,
        ty: wgpu::BindingType::Texture {
            sample_type: source.sample_type,
            view_dimension: wgpu::TextureViewDimension::D2,
            multisampled: source.multisampled,
        },
        count: None,
    }];
    let mut resources = vec![wgpu::BindGroupEntry {
        binding: 0,
        resource: wgpu::BindingResource::TextureView(source.view),
    }];
    if let Some(sampler) = source.sampler {
        entries.push(wgpu::BindGroupLayoutEntry {
            binding: 1,
            visibility: wgpu::ShaderStages::FRAGMENT,
            ty: wgpu::BindingType::Sampler(wgpu::SamplerBindingType::Filtering),
            count: None,
        });
        resources.push(wgpu::BindGroupEntry {
            binding: 1,
            resource: wgpu::BindingResource::Sampler(sampler),
        });
    }
    let layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
        label: None,
        entries: &entries,
    });
    let pipeline = device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
        label: None,
        layout: Some(
            &device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
                label: None,
                bind_group_layouts: &[Some(&layout)],
                immediate_size: 0,
            }),
        ),
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
        layout: &layout,
        entries: &resources,
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

/// A sampled 2D float texture at 0 and a filtering sampler at 1.
const SAMPLED: &str =
    "@group(0) @binding(0) var t: texture_2d<f32>;\n@group(0) @binding(1) var s: sampler;";

fn sampled<'a>(view: &'a wgpu::TextureView, sampler: &'a wgpu::Sampler) -> Source<'a> {
    Source {
        view,
        sample_type: wgpu::TextureSampleType::Float { filterable: true },
        multisampled: false,
        sampler: Some(sampler),
    }
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
        assert_eq!(
            draw(&device, &queue, SAMPLED, load, sampled(&view, &sampler))[5],
            expected
        );
    }
}

#[test]
fn a_sampled_multisampled_texture_renders_resolves_and_is_read() {
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
    // Its depth, also sampled: the depth copies read it as an unfilterable float texture.
    let depth = device.create_texture(&wgpu::TextureDescriptor {
        label: None,
        size: wgpu::Extent3d {
            width: 4,
            height: 4,
            depth_or_array_layers: 1,
        },
        mip_level_count: 1,
        sample_count: 4,
        dimension: wgpu::TextureDimension::D2,
        format: wgpu::TextureFormat::Depth24PlusStencil8,
        usage: wgpu::TextureUsages::RENDER_ATTACHMENT | wgpu::TextureUsages::TEXTURE_BINDING,
        view_formats: &[],
    });
    let msaa_view = msaa.create_view(&Default::default());
    let mut encoder = device.create_command_encoder(&Default::default());
    encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
        color_attachments: &[Some(wgpu::RenderPassColorAttachment {
            view: &msaa_view,
            depth_slice: None,
            resolve_target: Some(&resolved.create_view(&Default::default())),
            ops: wgpu::Operations {
                load: wgpu::LoadOp::Clear(wgpu::Color::BLUE),
                store: wgpu::StoreOp::Store,
            },
        })],
        depth_stencil_attachment: Some(wgpu::RenderPassDepthStencilAttachment {
            view: &depth.create_view(&Default::default()),
            depth_ops: Some(wgpu::Operations {
                load: wgpu::LoadOp::Clear(0.25),
                store: wgpu::StoreOp::Store,
            }),
            stencil_ops: None,
        }),
        ..Default::default()
    });
    queue.submit([encoder.finish()]);
    assert_eq!(read(&device, &queue, &resolved)[5], [0, 0, 255, 255]);
    let load = "@fragment fn fs(@builtin(position) p: vec4<f32>) -> @location(0) vec4<f32> {\n\
                return textureLoad(t, vec2<i32>(p.xy), 3);\n}";
    let multisampled = "@group(0) @binding(0) var t: texture_multisampled_2d<f32>;";
    let unfilterable = |view| Source {
        view,
        sample_type: wgpu::TextureSampleType::Float { filterable: false },
        multisampled: true,
        sampler: None,
    };
    assert_eq!(
        draw(
            &device,
            &queue,
            multisampled,
            load,
            unfilterable(&msaa_view)
        )[5],
        [0, 0, 255, 255]
    );
    let depth_only = depth.create_view(&wgpu::TextureViewDescriptor {
        aspect: wgpu::TextureAspect::DepthOnly,
        ..Default::default()
    });
    // 0.25 in red; green, blue and alpha as a depth texture reads them (0, 0, 1).
    assert_eq!(
        draw(
            &device,
            &queue,
            multisampled,
            load,
            unfilterable(&depth_only)
        )[5],
        [64, 0, 0, 255]
    );
}

#[test]
fn a_drawn_only_texture_with_view_formats_is_a_texture() {
    let Some((device, _queue)) = gl_device() else {
        return;
    };
    let with_views = texture(
        &device,
        (4, 4, 1),
        1,
        1,
        wgpu::TextureUsages::RENDER_ATTACHMENT,
        &[wgpu::TextureFormat::Rgba8UnormSrgb],
    );
    // The control: without view formats it stays a renderbuffer.
    let without = texture(
        &device,
        (4, 4, 1),
        1,
        1,
        wgpu::TextureUsages::RENDER_ATTACHMENT,
        &[],
    );
    let kind = |t: &wgpu::Texture| {
        let hal = unsafe { t.as_hal::<Gles>() }.expect("an OpenGL texture");
        matches!(hal.inner, wgpu::hal::gles::TextureInner::Texture { .. })
    };
    assert!(kind(&with_views));
    assert!(!kind(&without));
}

#[test]
fn a_context_current_before_the_backend_works_is_current_after() {
    let Some((device, _queue)) = gl_device() else {
        return;
    };
    // The same libEGL, loaded again (its handles are the process's).
    let egl = unsafe { khronos_egl::DynamicInstance::<khronos_egl::EGL1_4>::load_required() }
        .expect("libEGL");
    let (display, ours) = {
        let hal = unsafe { device.as_hal::<Gles>() }.expect("an OpenGL device");
        let context = hal.context();
        let display = *context.raw_display().expect("an EGL display");
        let theirs = unsafe { khronos_egl::Context::from_ptr(context.raw_context()) };
        let id = egl
            .query_context(display, theirs, khronos_egl::CONFIG_ID)
            .expect("the context's config");
        let config = egl
            .choose_first_config(display, &[khronos_egl::CONFIG_ID, id, khronos_egl::NONE])
            .unwrap()
            .expect("the context's config");
        egl.bind_api(khronos_egl::OPENGL_API).unwrap();
        // A shared context has the backend's robustness (wgpu asks for robust access first).
        let attributes = |robust: bool| {
            let mut a = vec![
                khronos_egl::CONTEXT_MAJOR_VERSION,
                4,
                khronos_egl::CONTEXT_MINOR_VERSION,
                5,
            ];
            if robust {
                a.extend([
                    khronos_egl::CONTEXT_OPENGL_ROBUST_ACCESS,
                    khronos_egl::TRUE as i32,
                ]);
            }
            a.push(khronos_egl::NONE);
            a
        };
        let ours = egl
            .create_context(display, config, Some(theirs), &attributes(true))
            .or_else(|_| egl.create_context(display, config, Some(theirs), &attributes(false)))
            .expect("a context sharing the backend's");
        (display, ours)
    };
    egl.make_current(display, None, None, Some(ours)).unwrap();
    // Any backend call takes its context and gives the thread's back.
    device.create_buffer(&wgpu::BufferDescriptor {
        label: None,
        size: 16,
        usage: wgpu::BufferUsages::UNIFORM,
        mapped_at_creation: false,
    });
    let current = egl.get_current_context();
    egl.make_current(display, None, None, None).unwrap();
    egl.destroy_context(display, ours).unwrap();
    assert_eq!(current, Some(ours));
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
        assert_eq!(
            draw(&device, &queue, SAMPLED, sample, sampled(&view, &sampler))[5],
            expected
        );
    }
}
