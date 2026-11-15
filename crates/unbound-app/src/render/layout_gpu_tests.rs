//! Pipeline layouts the renderer makes, on the machine's real GPU device (skipped without one):
//! a program that does not read the main texture group leaves group 2 out of its layout, while
//! the draw still binds the main group there (`compiled.rs`, `executor.rs`).
use wgpu::util::DeviceExt;

/// A device of the machine's primary backend (Metal, Vulkan; OpenGL where it is the only one),
/// none without an adapter.
fn device() -> Option<(wgpu::Device, wgpu::Queue)> {
    let instance = wgpu::Instance::new(wgpu::InstanceDescriptor {
        backends: wgpu::Backends::all(),
        ..wgpu::InstanceDescriptor::new_without_display_handle_from_env()
    });
    let adapter =
        bevy::tasks::block_on(instance.request_adapter(&wgpu::RequestAdapterOptions::default()))
            .ok()?;
    bevy::tasks::block_on(adapter.request_device(&wgpu::DeviceDescriptor {
        label: Some("layout test"),
        required_features: wgpu::Features::empty(),
        required_limits: adapter.limits(),
        ..Default::default()
    }))
    .ok()
}

fn texel(device: &wgpu::Device, queue: &wgpu::Queue, rgba: [u8; 4]) -> wgpu::TextureView {
    device
        .create_texture_with_data(
            queue,
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
                format: wgpu::TextureFormat::Rgba8Unorm,
                usage: wgpu::TextureUsages::TEXTURE_BINDING,
                view_formats: &[],
            },
            wgpu::util::TextureDataOrder::LayerMajor,
            &rgba,
        )
        .create_view(&Default::default())
}

fn entry(binding: u32, ty: wgpu::BindingType) -> wgpu::BindGroupLayoutEntry {
    wgpu::BindGroupLayoutEntry {
        binding,
        visibility: wgpu::ShaderStages::VERTEX_FRAGMENT,
        ty,
        count: None,
    }
}

const TEXTURE: wgpu::BindingType = wgpu::BindingType::Texture {
    sample_type: wgpu::TextureSampleType::Float { filterable: true },
    view_dimension: wgpu::TextureViewDimension::D2,
    multisampled: false,
};
const SAMPLER: wgpu::BindingType = wgpu::BindingType::Sampler(wgpu::SamplerBindingType::Filtering);

/// The shader of both programs: a full-screen triangle, and a color from the uniform (group 0),
/// a texture (group 1) and a storage buffer (group 3), or (`main`) the main texture (group 2).
const SHADER: &str = r#"
@group(0) @binding(0) var<uniform> u: vec4f;
@group(1) @binding(0) var t: texture_2d<f32>;
@group(1) @binding(1) var s: sampler;
@group(2) @binding(0) var main_t: texture_2d<f32>;
@group(2) @binding(1) var main_s: sampler;
@group(3) @binding(0) var<storage, read> b: vec4f;
@vertex fn vs(@builtin(vertex_index) i: u32) -> @builtin(position) vec4f {
    let p = vec2f(f32((i << 1u) & 2u), f32(i & 2u));
    return vec4f(p * 2.0 - 1.0, 0.0, 1.0);
}
@fragment fn without_main() -> @location(0) vec4f {
    return u + textureSample(t, s, vec2f(0.5)) + b;
}
@fragment fn main() -> @location(0) vec4f {
    return textureSample(main_t, main_s, vec2f(0.5));
}
"#;

/// A program whose layout has no group 2 draws with the main group bound there, right after a
/// program that reads it, in one pass: its groups 0, 1 and 3 are its own (the backends that
/// number bindings across groups, Metal and OpenGL, number past the gap) and nothing fails
/// validation.
#[test]
fn a_layout_without_the_main_group_draws_beside_one_with_it() {
    let Some((device, queue)) = device() else {
        eprintln!("skipped: no GPU adapter");
        return;
    };
    if device.limits().max_storage_buffers_per_shader_stage == 0 {
        eprintln!("skipped: the device binds no storage buffers");
        return;
    }
    let scope = device.push_error_scope(wgpu::ErrorFilter::Validation);
    let module = device.create_shader_module(wgpu::ShaderModuleDescriptor {
        label: None,
        source: wgpu::ShaderSource::Wgsl(SHADER.into()),
    });
    let uniform_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
        label: None,
        entries: &[entry(
            0,
            wgpu::BindingType::Buffer {
                ty: wgpu::BufferBindingType::Uniform,
                has_dynamic_offset: false,
                min_binding_size: None,
            },
        )],
    });
    let texture_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
        label: None,
        entries: &[entry(0, TEXTURE), entry(1, SAMPLER)],
    });
    // Read by the fragment stage only (a vertex stage's storage buffers are a downlevel feature).
    let buffer_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
        label: None,
        entries: &[wgpu::BindGroupLayoutEntry {
            visibility: wgpu::ShaderStages::FRAGMENT,
            ..entry(
                0,
                wgpu::BindingType::Buffer {
                    ty: wgpu::BufferBindingType::Storage { read_only: true },
                    has_dynamic_offset: false,
                    min_binding_size: None,
                },
            )
        }],
    });
    let pipeline = |entry_point: &str, groups: &[Option<&wgpu::BindGroupLayout>]| {
        let layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: None,
            bind_group_layouts: groups,
            immediate_size: 0,
        });
        device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: None,
            layout: Some(&layout),
            vertex: wgpu::VertexState {
                module: &module,
                entry_point: Some("vs"),
                compilation_options: Default::default(),
                buffers: &[],
            },
            fragment: Some(wgpu::FragmentState {
                module: &module,
                entry_point: Some(entry_point),
                compilation_options: Default::default(),
                targets: &[Some(wgpu::TextureFormat::Rgba8Unorm.into())],
            }),
            primitive: Default::default(),
            depth_stencil: None,
            multisample: Default::default(),
            multiview_mask: None,
            cache: None,
        })
    };
    // The program reading the main group, and one whose layout leaves it out.
    let with_main = pipeline(
        "main",
        &[
            Some(&uniform_layout),
            Some(&texture_layout),
            Some(&texture_layout),
            Some(&buffer_layout),
        ],
    );
    let without_main = pipeline(
        "without_main",
        &[
            Some(&uniform_layout),
            Some(&texture_layout),
            None,
            Some(&buffer_layout),
        ],
    );
    let sampler = device.create_sampler(&Default::default());
    let buffer = |usage, data: [f32; 4]| {
        device.create_buffer_init(&wgpu::util::BufferInitDescriptor {
            label: None,
            contents: bytemuck::cast_slice(&data),
            usage,
        })
    };
    let uniform = buffer(wgpu::BufferUsages::UNIFORM, [0.25, 0.0, 0.0, 0.0]);
    let storage = buffer(wgpu::BufferUsages::STORAGE, [0.0, 0.0, 0.75, 1.0]);
    let bind = |layout: &wgpu::BindGroupLayout, entries: &[wgpu::BindingResource]| {
        let entries: Vec<_> = entries
            .iter()
            .enumerate()
            .map(|(k, resource)| wgpu::BindGroupEntry {
                binding: k as u32,
                resource: resource.clone(),
            })
            .collect();
        device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: None,
            layout,
            entries: &entries,
        })
    };
    let own_texture = texel(&device, &queue, [0, 128, 0, 0]);
    let main_texture = texel(&device, &queue, [255, 0, 255, 255]);
    let uniform_group = bind(&uniform_layout, &[uniform.as_entire_binding()]);
    let texture_group = bind(
        &texture_layout,
        &[
            wgpu::BindingResource::TextureView(&own_texture),
            wgpu::BindingResource::Sampler(&sampler),
        ],
    );
    let main_group = bind(
        &texture_layout,
        &[
            wgpu::BindingResource::TextureView(&main_texture),
            wgpu::BindingResource::Sampler(&sampler),
        ],
    );
    let buffer_group = bind(&buffer_layout, &[storage.as_entire_binding()]);
    let target = device.create_texture(&wgpu::TextureDescriptor {
        label: None,
        size: wgpu::Extent3d {
            width: 2,
            height: 1,
            depth_or_array_layers: 1,
        },
        mip_level_count: 1,
        sample_count: 1,
        dimension: wgpu::TextureDimension::D2,
        format: wgpu::TextureFormat::Rgba8Unorm,
        usage: wgpu::TextureUsages::RENDER_ATTACHMENT | wgpu::TextureUsages::COPY_SRC,
        view_formats: &[],
    });
    let view = target.create_view(&Default::default());
    let mut encoder = device.create_command_encoder(&Default::default());
    {
        let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
            label: None,
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
        // As the executor binds them: every group for every draw, the main group at 2.
        for (x, pipeline) in [(0.0, &with_main), (1.0, &without_main)] {
            pass.set_viewport(x, 0.0, 1.0, 1.0, 0.0, 1.0);
            pass.set_pipeline(pipeline);
            pass.set_bind_group(0, &uniform_group, &[]);
            pass.set_bind_group(1, &texture_group, &[]);
            pass.set_bind_group(2, &main_group, &[]);
            pass.set_bind_group(3, &buffer_group, &[]);
            pass.draw(0..3, 0..1);
        }
    }
    let readback = device.create_buffer(&wgpu::BufferDescriptor {
        label: None,
        size: 256,
        usage: wgpu::BufferUsages::COPY_DST | wgpu::BufferUsages::MAP_READ,
        mapped_at_creation: false,
    });
    encoder.copy_texture_to_buffer(
        target.as_image_copy(),
        wgpu::TexelCopyBufferInfo {
            buffer: &readback,
            layout: wgpu::TexelCopyBufferLayout {
                offset: 0,
                bytes_per_row: Some(256),
                rows_per_image: None,
            },
        },
        wgpu::Extent3d {
            width: 2,
            height: 1,
            depth_or_array_layers: 1,
        },
    );
    queue.submit([encoder.finish()]);
    readback
        .slice(..)
        .map_async(wgpu::MapMode::Read, |r| r.unwrap());
    device.poll(wgpu::PollType::wait_indefinitely()).unwrap();
    if let Some(error) = bevy::tasks::block_on(scope.pop()) {
        panic!("{error}");
    }
    let data = readback.slice(..).get_mapped_range().unwrap();
    // The main texture where its program drew; the uniform, own texture and buffer where the
    // program without the main group drew.
    assert_eq!(data[0..4], [255, 0, 255, 255]);
    let drawn = &data[4..8];
    let expected = [64, 128, 191, 255];
    assert!(
        drawn
            .iter()
            .zip(expected)
            .all(|(&d, e)| (d as i32 - e).abs() <= 1),
        "{drawn:?}"
    );
}
