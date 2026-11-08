//! GPU mip filtering, independent of a project's shaders and render pipeline.
use std::collections::HashMap;

const FILTER: &str = r#"
@group(0) @binding(0) var source: SOURCE_TYPE<f32>;
struct Vertex { @builtin(position) position: vec4<f32>, @location(0) @interpolate(flat) slice: u32 };
@vertex fn vertex(@builtin(vertex_index) index: u32, @builtin(instance_index) slice: u32) -> Vertex {
    let corners = array<vec2<f32>, 3>(vec2(-1.0, -1.0), vec2(3.0, -1.0), vec2(-1.0, 3.0));
    var out: Vertex;
    out.position = vec4(corners[index], 0.0, 1.0);
    out.slice = slice;
    return out;
}
@fragment fn fragment(input: Vertex) -> @location(0) vec4<f32> {
    let size = SOURCE_SIZE;
    let destination = max(size / 2u, vec3u(1u));
    let pixel = vec3u(vec2u(input.position.xy), input.slice);
    let lower = vec3f(pixel) * vec3f(size) / vec3f(destination);
    let upper = vec3f(pixel + 1u) * vec3f(size) / vec3f(destination);
    let first = vec3u(floor(lower));
    let last = min(vec3u(ceil(upper)), size);
    let extent = upper - lower;
    let normalization = 1.0 / (extent.x * extent.y * extent.z);
    var result = vec4f(0.0);
    for (var z = first.z; z < last.z; z++) {
        for (var y = first.y; y < last.y; y++) {
            for (var x = first.x; x < last.x; x++) {
                let coordinate = vec3u(x, y, z);
                let overlap = max(vec3f(0.0), min(upper, vec3f(coordinate + 1u)) - max(lower, vec3f(coordinate)));
                result += SOURCE_LOAD * (overlap.x * overlap.y * overlap.z * normalization);
            }
        }
    }
    return result;
}
"#;

#[derive(Default)]
pub struct Mipmaps {
    pipelines: HashMap<(wgpu::TextureFormat, bool), wgpu::RenderPipeline>,
    levels: HashMap<
        (wgpu::Texture, wgpu::TextureFormat, u32),
        Vec<(wgpu::BindGroup, wgpu::TextureView)>,
    >,
}

impl Mipmaps {
    pub fn prepare(
        &mut self,
        device: &wgpu::Device,
        texture: &wgpu::Texture,
        format: wgpu::TextureFormat,
    ) {
        if texture.mip_level_count() <= 1 {
            return;
        }
        let volume = texture.dimension() == wgpu::TextureDimension::D3;
        let pipeline = self.pipelines.entry((format, volume)).or_insert_with(|| {
            // Area filtering uses texel loads, including non-filterable float storage.
            let bindings = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
                label: Some("unbound mip filter"),
                entries: &[wgpu::BindGroupLayoutEntry {
                    binding: 0,
                    visibility: wgpu::ShaderStages::FRAGMENT,
                    ty: wgpu::BindingType::Texture {
                        sample_type: wgpu::TextureSampleType::Float { filterable: false },
                        view_dimension: if volume {
                            wgpu::TextureViewDimension::D3
                        } else {
                            wgpu::TextureViewDimension::D2
                        },
                        multisampled: false,
                    },
                    count: None,
                }],
            });
            let layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
                label: Some("unbound mip filter"),
                bind_group_layouts: &[Some(&bindings)],
                immediate_size: 0,
            });
            let shader = device.create_shader_module(wgpu::ShaderModuleDescriptor {
                label: Some("unbound mip filter"),
                source: wgpu::ShaderSource::Wgsl(
                    FILTER
                        .replace(
                            "SOURCE_TYPE",
                            if volume { "texture_3d" } else { "texture_2d" },
                        )
                        .replace(
                            "SOURCE_SIZE",
                            if volume {
                                "textureDimensions(source)"
                            } else {
                                "vec3u(textureDimensions(source), 1u)"
                            },
                        )
                        .replace(
                            "SOURCE_LOAD",
                            if volume {
                                "textureLoad(source, vec3i(coordinate), 0)"
                            } else {
                                "textureLoad(source, vec2i(coordinate.xy), 0)"
                            },
                        )
                        .into(),
                ),
            });
            device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
                label: Some("unbound mip filter"),
                layout: Some(&layout),
                vertex: wgpu::VertexState {
                    module: &shader,
                    entry_point: Some("vertex"),
                    compilation_options: Default::default(),
                    buffers: &[],
                },
                fragment: Some(wgpu::FragmentState {
                    module: &shader,
                    entry_point: Some("fragment"),
                    compilation_options: Default::default(),
                    targets: &[Some(wgpu::ColorTargetState {
                        format,
                        blend: None,
                        write_mask: wgpu::ColorWrites::ALL,
                    })],
                }),
                primitive: Default::default(),
                depth_stencil: None,
                multisample: Default::default(),
                multiview_mask: None,
                cache: None,
            })
        });
        let layers = if volume {
            1
        } else {
            texture.depth_or_array_layers()
        };
        for layer in 0..layers {
            let key = (texture.clone(), format, layer);
            if self.levels.contains_key(&key) {
                continue;
            }
            let mut levels = Vec::new();
            for mip in 1..texture.mip_level_count() {
                let view = |level| {
                    texture.create_view(&wgpu::TextureViewDescriptor {
                        label: Some("unbound mip level"),
                        format: Some(format),
                        usage: super::resources::view_usage(texture, Some(format)),
                        dimension: Some(if volume {
                            wgpu::TextureViewDimension::D3
                        } else {
                            wgpu::TextureViewDimension::D2
                        }),
                        base_mip_level: level,
                        mip_level_count: Some(1),
                        base_array_layer: layer,
                        array_layer_count: (!volume).then_some(1),
                        ..Default::default()
                    })
                };
                let source = view(mip - 1);
                let target = view(mip);
                let group = device.create_bind_group(&wgpu::BindGroupDescriptor {
                    label: Some("unbound mip filter"),
                    layout: &pipeline.get_bind_group_layout(0),
                    entries: &[wgpu::BindGroupEntry {
                        binding: 0,
                        resource: wgpu::BindingResource::TextureView(&source),
                    }],
                });
                levels.push((group, target));
            }
            self.levels.insert(key, levels);
        }
    }

    pub fn release(&mut self, texture: &wgpu::Texture) {
        self.levels.retain(|(source, _, _), _| source != texture);
    }

    pub fn encode(
        &self,
        encoder: &mut wgpu::CommandEncoder,
        texture: &wgpu::Texture,
        format: wgpu::TextureFormat,
        first_layer: u32,
        layers: u32,
    ) {
        if texture.mip_level_count() <= 1 {
            return;
        }
        let volume = texture.dimension() == wgpu::TextureDimension::D3;
        let pipeline = &self.pipelines[&(format, volume)];
        let range = if volume {
            0..1
        } else {
            first_layer..first_layer + layers
        };
        for layer in range {
            for (level, (group, target)) in self.levels[&(texture.clone(), format, layer)]
                .iter()
                .enumerate()
            {
                let depth = if volume {
                    (texture.depth_or_array_layers() >> (level + 1)).max(1)
                } else {
                    1
                };
                for slice in 0..depth {
                    let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
                        label: Some("unbound mip filter"),
                        color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                            view: target,
                            depth_slice: volume.then_some(slice),
                            resolve_target: None,
                            ops: wgpu::Operations {
                                load: wgpu::LoadOp::Load,
                                store: wgpu::StoreOp::Store,
                            },
                        })],
                        depth_stencil_attachment: None,
                        timestamp_writes: None,
                        occlusion_query_set: None,
                        multiview_mask: None,
                    });
                    pass.set_pipeline(pipeline);
                    pass.set_bind_group(0, group, &[]);
                    pass.draw(0..3, slice..slice + 1);
                }
            }
        }
    }
}
