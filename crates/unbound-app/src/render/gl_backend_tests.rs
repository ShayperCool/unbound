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
    // The adapter's features and limits, as the renderer's device has them.
    bevy::tasks::block_on(adapter.request_device(&wgpu::DeviceDescriptor {
        label: Some("gl backend test"),
        required_features: adapter.features(),
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
    read_layer(device, queue, texture, 0)
}

/// Level 0 of a layer of a 4x4 RGBA8 texture, row by row.
fn read_layer(
    device: &wgpu::Device,
    queue: &wgpu::Queue,
    texture: &wgpu::Texture,
    layer: u32,
) -> Vec<[u8; 4]> {
    let buffer = device.create_buffer(&wgpu::BufferDescriptor {
        label: None,
        size: 256 * 4,
        usage: wgpu::BufferUsages::COPY_DST | wgpu::BufferUsages::MAP_READ,
        mapped_at_creation: false,
    });
    let mut encoder = device.create_command_encoder(&Default::default());
    encoder.copy_texture_to_buffer(
        wgpu::TexelCopyTextureInfo {
            origin: wgpu::Origin3d {
                x: 0,
                y: 0,
                z: layer,
            },
            ..texture.as_image_copy()
        },
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
    dimension: wgpu::TextureViewDimension,
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
            view_dimension: source.dimension,
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
        dimension: wgpu::TextureViewDimension::D2,
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

/// A cube texture (six square layers) is drawn into face by face and read back face by face:
/// `Camera.RenderToCubemap`, reflection probes and `Cubemap.GetPixels` do both.
#[test]
fn each_face_of_a_cube_is_drawn_and_read() {
    let Some((device, queue)) = gl_device() else {
        return;
    };
    let cube = texture(
        &device,
        (4, 4, 6),
        1,
        1,
        wgpu::TextureUsages::RENDER_ATTACHMENT
            | wgpu::TextureUsages::TEXTURE_BINDING
            | wgpu::TextureUsages::COPY_SRC,
        &[],
    );
    let shade = |face: u32| (40 * (face + 1)) as u8;
    let mut encoder = device.create_command_encoder(&Default::default());
    for face in 0..6 {
        let view = cube.create_view(&wgpu::TextureViewDescriptor {
            dimension: Some(wgpu::TextureViewDimension::D2),
            base_array_layer: face,
            array_layer_count: Some(1),
            ..Default::default()
        });
        let value = f64::from(shade(face)) / 255.0;
        clear(
            &mut encoder,
            &view,
            None,
            wgpu::Color {
                r: value,
                g: value,
                b: value,
                a: 1.0,
            },
        );
    }
    queue.submit([encoder.finish()]);
    for face in 0..6 {
        let s = shade(face);
        assert_eq!(
            read_layer(&device, &queue, &cube, face)[5],
            [s, s, s, 255],
            "face {face}"
        );
    }
    // A face is copied out into a 2D texture, and a 2D texture into a face.
    let flat = |usage| texture(&device, (4, 4, 1), 1, 1, usage, &[]);
    let out = flat(wgpu::TextureUsages::COPY_DST | wgpu::TextureUsages::COPY_SRC);
    let whole_level = wgpu::Extent3d {
        width: 4,
        height: 4,
        depth_or_array_layers: 1,
    };
    let face = |texture, z| wgpu::TexelCopyTextureInfo {
        origin: wgpu::Origin3d { x: 0, y: 0, z },
        ..wgpu::Texture::as_image_copy(texture)
    };
    let mut encoder = device.create_command_encoder(&Default::default());
    encoder.copy_texture_to_texture(face(&cube, 3), out.as_image_copy(), whole_level);
    queue.submit([encoder.finish()]);
    let s = shade(3);
    assert_eq!(read(&device, &queue, &out)[5], [s, s, s, 255]);
    let into = texture(
        &device,
        (4, 4, 6),
        1,
        1,
        wgpu::TextureUsages::COPY_DST | wgpu::TextureUsages::COPY_SRC,
        &[],
    );
    let mut encoder = device.create_command_encoder(&Default::default());
    encoder.copy_texture_to_texture(out.as_image_copy(), face(&into, 4), whole_level);
    // Several faces in one copy.
    encoder.copy_texture_to_texture(
        face(&cube, 0),
        face(&into, 0),
        wgpu::Extent3d {
            depth_or_array_layers: 2,
            ..whole_level
        },
    );
    queue.submit([encoder.finish()]);
    for (layer, shade) in [(4, shade(3)), (0, shade(0)), (1, shade(1)), (5, 0)] {
        let alpha = if shade == 0 { 0 } else { 255 };
        assert_eq!(
            read_layer(&device, &queue, &into, layer)[5],
            [shade, shade, shade, alpha],
            "copied face {layer}"
        );
    }
    // The cube samples each face in its direction.
    let whole = cube.create_view(&wgpu::TextureViewDescriptor {
        dimension: Some(wgpu::TextureViewDimension::Cube),
        ..Default::default()
    });
    let sampler = device.create_sampler(&wgpu::SamplerDescriptor {
        mag_filter: wgpu::FilterMode::Linear,
        min_filter: wgpu::FilterMode::Linear,
        ..Default::default()
    });
    let declarations = "@group(0) @binding(0) var t: texture_cube<f32>;\n\
                        @group(0) @binding(1) var s: sampler;\n";
    let sample = |direction: &str| {
        let fragment = format!(
            "@fragment fn fs() -> @location(0) vec4<f32> {{\n\
             return textureSample(t, s, vec3<f32>({direction}));\n}}"
        );
        let source = Source {
            view: &whole,
            sample_type: wgpu::TextureSampleType::Float { filterable: true },
            dimension: wgpu::TextureViewDimension::Cube,
            multisampled: false,
            sampler: Some(&sampler),
        };
        draw(&device, &queue, declarations, &fragment, source)[5]
    };
    for (face, direction) in [
        "1.0, 0.0, 0.0",
        "-1.0, 0.0, 0.0",
        "0.0, 1.0, 0.0",
        "0.0, -1.0, 0.0",
        "0.0, 0.0, 1.0",
        "0.0, 0.0, -1.0",
    ]
    .into_iter()
    .enumerate()
    {
        let s = shade(face as u32);
        assert_eq!(
            sample(direction),
            [s, s, s, 255],
            "direction of face {face}"
        );
    }
    // On the edge two faces share, their texels are filtered together (seamless cube maps), as
    // Unity's devices filter them: halfway between the +X and the +Z face.
    let edge = sample("1.0, 0.0, 1.0")[0];
    let halfway = (u32::from(shade(0)) + u32::from(shade(4))) / 2;
    assert!(
        u32::from(edge).abs_diff(halfway) <= 1,
        "the +X/+Z edge samples {edge}, not {halfway}"
    );
}

/// A volume's slices are read back from any first slice (`AsyncGPUReadback` of a Texture3D's
/// depth range): the first texel of each slice read, in the buffer's order.
#[test]
fn slices_of_a_volume_are_read_from_any_first_slice() {
    let Some((device, queue)) = gl_device() else {
        return;
    };
    let volume = device.create_texture(&wgpu::TextureDescriptor {
        label: None,
        size: wgpu::Extent3d {
            width: 4,
            height: 4,
            depth_or_array_layers: 4,
        },
        mip_level_count: 1,
        sample_count: 1,
        dimension: wgpu::TextureDimension::D3,
        format: TARGET,
        usage: wgpu::TextureUsages::COPY_DST | wgpu::TextureUsages::COPY_SRC,
        view_formats: &[],
    });
    let texels: Vec<u8> = (0..4u8)
        .flat_map(|slice| [50 * (slice + 1), 0, 0, 255].repeat(16))
        .collect();
    queue.write_texture(
        volume.as_image_copy(),
        &texels,
        wgpu::TexelCopyBufferLayout {
            offset: 0,
            bytes_per_row: Some(16),
            rows_per_image: Some(4),
        },
        volume.size(),
    );
    let read_slices = |first: u32, count: u32| -> Vec<u8> {
        // Rows of 256 bytes, 4 rows a slice.
        let buffer = device.create_buffer(&wgpu::BufferDescriptor {
            label: None,
            size: 256 * 4 * 4,
            usage: wgpu::BufferUsages::COPY_DST | wgpu::BufferUsages::MAP_READ,
            mapped_at_creation: false,
        });
        let mut encoder = device.create_command_encoder(&Default::default());
        encoder.copy_texture_to_buffer(
            wgpu::TexelCopyTextureInfo {
                origin: wgpu::Origin3d {
                    x: 0,
                    y: 0,
                    z: first,
                },
                ..volume.as_image_copy()
            },
            wgpu::TexelCopyBufferInfo {
                buffer: &buffer,
                layout: wgpu::TexelCopyBufferLayout {
                    offset: 0,
                    bytes_per_row: Some(256),
                    rows_per_image: Some(4),
                },
            },
            wgpu::Extent3d {
                width: 4,
                height: 4,
                depth_or_array_layers: count,
            },
        );
        queue.submit([encoder.finish()]);
        buffer
            .slice(..)
            .map_async(wgpu::MapMode::Read, |r| r.unwrap());
        device.poll(wgpu::PollType::wait_indefinitely()).unwrap();
        let data = buffer.slice(..).get_mapped_range().unwrap();
        (0..count as usize).map(|i| data[i * 256 * 4]).collect()
    };
    assert_eq!(read_slices(0, 4), [50, 100, 150, 200]);
    assert_eq!(read_slices(1, 2), [100, 150]);
    assert_eq!(read_slices(3, 1), [200]);
}

/// Kernels write textures of desktop OpenGL's image formats in those formats (RW render
/// textures of integer and 10-10-10-2 formats): the texel a kernel stored, as the format keeps
/// it.
#[test]
fn kernels_store_into_desktop_opengl_image_formats() {
    let Some((device, queue)) = gl_device() else {
        return;
    };
    let store = |format: wgpu::TextureFormat, name: &str, value: &str| -> Vec<u8> {
        let texture = device.create_texture(&wgpu::TextureDescriptor {
            label: None,
            size: wgpu::Extent3d {
                width: 4,
                height: 4,
                depth_or_array_layers: 1,
            },
            mip_level_count: 1,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format,
            usage: wgpu::TextureUsages::STORAGE_BINDING | wgpu::TextureUsages::COPY_SRC,
            view_formats: &[],
        });
        let module = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: None,
            source: wgpu::ShaderSource::Wgsl(
                format!(
                    "@group(0) @binding(0) var t: texture_storage_2d<{name}, write>;\n\
                     @compute @workgroup_size(1) fn main() {{\n\
                     textureStore(t, vec2<i32>(1, 1), {value});\n}}"
                )
                .into(),
            ),
        });
        let pipeline = device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
            label: None,
            layout: None,
            module: &module,
            entry_point: Some("main"),
            compilation_options: Default::default(),
            cache: None,
        });
        let group = device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: None,
            layout: &pipeline.get_bind_group_layout(0),
            entries: &[wgpu::BindGroupEntry {
                binding: 0,
                resource: wgpu::BindingResource::TextureView(
                    &texture.create_view(&Default::default()),
                ),
            }],
        });
        let buffer = device.create_buffer(&wgpu::BufferDescriptor {
            label: None,
            size: 256 * 4,
            usage: wgpu::BufferUsages::COPY_DST | wgpu::BufferUsages::MAP_READ,
            mapped_at_creation: false,
        });
        let mut encoder = device.create_command_encoder(&Default::default());
        {
            let mut pass = encoder.begin_compute_pass(&Default::default());
            pass.set_pipeline(&pipeline);
            pass.set_bind_group(0, &group, &[]);
            pass.dispatch_workgroups(1, 1, 1);
        }
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
            texture.size(),
        );
        queue.submit([encoder.finish()]);
        buffer
            .slice(..)
            .map_async(wgpu::MapMode::Read, |r| r.unwrap());
        device.poll(wgpu::PollType::wait_indefinitely()).unwrap();
        let data = buffer.slice(..).get_mapped_range().unwrap();
        let texel = format.block_copy_size(None).unwrap() as usize;
        // The texel at (1, 1).
        data[256 + texel..256 + 2 * texel].to_vec()
    };
    // One 16-bit channel: the others are not kept.
    assert_eq!(
        store(
            wgpu::TextureFormat::R16Sint,
            "r16sint",
            "vec4<i32>(-5, 7, 9, 11)"
        ),
        (-5i16).to_le_bytes()
    );
    assert_eq!(
        store(
            wgpu::TextureFormat::Rg8Uint,
            "rg8uint",
            "vec4<u32>(200u, 7u, 9u, 11u)"
        ),
        [200, 7]
    );
    // Ten bits a color and two of alpha: 0.678 is stored as two thirds.
    let packed = store(
        wgpu::TextureFormat::Rgb10a2Unorm,
        "rgb10a2unorm",
        "vec4<f32>(1.0, 0.0, 0.0, 0.678)",
    );
    let packed = u32::from_le_bytes([packed[0], packed[1], packed[2], packed[3]]);
    assert_eq!((packed & 0x3ff, packed >> 30), (1023, 2));
}

/// Signed normalized targets are drawn into where the driver draws into them (URP's normals
/// texture is `R8G8B8A8_SNorm`): negative values are kept.
#[test]
fn a_signed_normalized_target_keeps_negative_values() {
    let Some((device, queue)) = gl_device() else {
        return;
    };
    let format = wgpu::TextureFormat::Rgba8Snorm;
    let usage = wgpu::TextureUsages::RENDER_ATTACHMENT | wgpu::TextureUsages::COPY_SRC;
    let features = device.adapter_info();
    // Creating the target fails validation where the driver has no such framebuffers; the
    // measured NVIDIA driver has them.
    let scope = device.push_error_scope(wgpu::ErrorFilter::Validation);
    let target = device.create_texture(&wgpu::TextureDescriptor {
        label: None,
        size: wgpu::Extent3d {
            width: 4,
            height: 4,
            depth_or_array_layers: 1,
        },
        mip_level_count: 1,
        sample_count: 1,
        dimension: wgpu::TextureDimension::D2,
        format,
        usage,
        view_formats: &[],
    });
    if let Some(error) = bevy::tasks::block_on(scope.pop()) {
        assert_ne!(features.vendor, 0x10de, "{error}");
        eprintln!("skipped: {features:?} does not draw into {format:?}");
        return;
    }
    let mut encoder = device.create_command_encoder(&Default::default());
    clear(
        &mut encoder,
        &target.create_view(&Default::default()),
        None,
        wgpu::Color {
            r: -0.5,
            g: 0.5,
            b: -1.0,
            a: 1.0,
        },
    );
    queue.submit([encoder.finish()]);
    let texel = read(&device, &queue, &target)[5].map(|byte| byte as i8);
    assert!(
        (-64..=-63).contains(&texel[0]) && (63..=64).contains(&texel[1]),
        "{texel:?}"
    );
    assert_eq!(texel[2..], [-127, 127]);
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
        dimension: wgpu::TextureViewDimension::D2,
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
    let _buffer = device.create_buffer(&wgpu::BufferDescriptor {
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

#[test]
fn a_stage_binds_the_contexts_texture_units() {
    let Some((device, queue)) = gl_device() else {
        return;
    };
    // The measured driver gives each stage 32 units (and binds 192 at once): the backend binds
    // 32 textures and samplers, as URP's Lit with probe volumes needs more than 16.
    let limits = device.limits();
    assert_eq!(limits.max_sampled_textures_per_shader_stage, 32);
    assert_eq!(limits.max_samplers_per_shader_stage, 32);
    const COUNT: u32 = 32;
    let sampler = device.create_sampler(&Default::default());
    let views: Vec<wgpu::TextureView> = (0..COUNT)
        .map(|k| {
            let texture = device.create_texture_with_data(
                &queue,
                &wgpu::TextureDescriptor {
                    label: None,
                    size: wgpu::Extent3d {
                        width: 1,
                        height: 1,
                        depth_or_array_layers: 1,
                    },
                    mip_level_count: 1,
                    sample_count: 1,
                    dimension: wgpu::TextureDimension::D2,
                    format: TARGET,
                    usage: wgpu::TextureUsages::TEXTURE_BINDING,
                    view_formats: &[],
                },
                wgpu::util::TextureDataOrder::LayerMajor,
                &[(k * 8) as u8, 0, 0, 255],
            );
            texture.create_view(&Default::default())
        })
        .collect();
    let mut entries = Vec::new();
    let mut resources = Vec::new();
    let mut declarations = String::new();
    for k in 0..COUNT {
        entries.push(wgpu::BindGroupLayoutEntry {
            binding: 2 * k,
            visibility: wgpu::ShaderStages::FRAGMENT,
            ty: wgpu::BindingType::Texture {
                sample_type: wgpu::TextureSampleType::Float { filterable: true },
                view_dimension: wgpu::TextureViewDimension::D2,
                multisampled: false,
            },
            count: None,
        });
        entries.push(wgpu::BindGroupLayoutEntry {
            binding: 2 * k + 1,
            visibility: wgpu::ShaderStages::FRAGMENT,
            ty: wgpu::BindingType::Sampler(wgpu::SamplerBindingType::Filtering),
            count: None,
        });
        resources.push(wgpu::BindGroupEntry {
            binding: 2 * k,
            resource: wgpu::BindingResource::TextureView(&views[k as usize]),
        });
        resources.push(wgpu::BindGroupEntry {
            binding: 2 * k + 1,
            resource: wgpu::BindingResource::Sampler(&sampler),
        });
        declarations.push_str(&format!(
            "@group(0) @binding({}) var t{k}: texture_2d<f32>;\n@group(0) @binding({}) var s{k}: sampler;\n",
            2 * k,
            2 * k + 1
        ));
    }
    // Every texture is read; the first, the 17th and the last land in the channels.
    let reads: Vec<String> = (0..COUNT)
        .map(|k| format!("textureSampleLevel(t{k}, s{k}, vec2<f32>(0.5), 0.0).r"))
        .collect();
    let code = format!(
        "{declarations}\
         @vertex fn vs(@builtin(vertex_index) i: u32) -> @builtin(position) vec4<f32> {{\n\
         let uv = vec2<f32>(f32((i << 1u) & 2u), f32(i & 2u));\n\
         return vec4<f32>(uv * 2.0 - 1.0, 0.0, 1.0);\n}}\n\
         @fragment fn fs() -> @location(0) vec4<f32> {{\n\
         let all = {};\n\
         return vec4<f32>(textureSampleLevel(t{last}, s{last}, vec2<f32>(0.5), 0.0).r, \
         textureSampleLevel(t16, s16, vec2<f32>(0.5), 0.0).r, all / {COUNT}.0, 1.0);\n}}",
        reads.join(" + "),
        last = COUNT - 1
    );
    let module = device.create_shader_module(wgpu::ShaderModuleDescriptor {
        label: None,
        source: wgpu::ShaderSource::Wgsl(code.into()),
    });
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
        &device,
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
    // Texture k holds 8k: the last 248, the 17th 128, the mean of 0..248 is 124.
    assert_eq!(read(&device, &queue, &target)[5], [248, 128, 124, 255]);
}

/// The Vulkan-only native opt-in must fail validation on OpenGL, while ordinary sampler
/// creation and the zero-option native path keep working on that same device.
#[test]
fn native_mirror_clamp_is_rejected_on_opengl() {
    let Some((device, _queue)) = gl_device() else {
        return;
    };
    assert_eq!(device.native_sampler_mirror_clamp(), None);
    let scope = device.push_error_scope(wgpu::ErrorFilter::Validation);
    let rejected =
        device.create_sampler_with_native_options(&Default::default(), 0.0, [true, false, false]);
    assert!(rejected.is_some());
    let error = bevy::tasks::block_on(scope.pop()).expect("unsupported native option must fail");
    assert!(
        error.to_string().contains("mirror-clamp-to-edge"),
        "{error}"
    );
    let scope = device.push_error_scope(wgpu::ErrorFilter::Validation);
    let _ordinary = device.create_sampler(&Default::default());
    let _native = device.create_sampler_with_native_options(&Default::default(), 0.0, [false; 3]);
    assert!(bevy::tasks::block_on(scope.pop()).is_none());
}
