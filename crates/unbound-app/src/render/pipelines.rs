//! Pipelines: a compiled program's per (Unity render state, topology, target), the viewport
//! clear and the present blit. Unity's enums map to wgpu's here.
use super::resources::{Topology, vertex_layout};
use crate::materials::RenderState;
use std::collections::HashMap;

pub const DEPTH_FORMAT: wgpu::TextureFormat = wgpu::TextureFormat::Depth24PlusStencil8;
/// One draw's uniform slot (dynamic offset alignment).
pub const DRAW_SLOT: u64 = 256;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct PipelineKey {
    pub state: RenderState,
    pub topology: Topology,
    pub format: wgpu::TextureFormat,
    pub depth: bool,
    /// The target's MSAA samples.
    pub samples: u32,
    /// The global depth bias added to the pass's `Offset`: units, slope factor (`f32` bits).
    pub depth_bias: [u32; 2],
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct ClearKey {
    pub format: wgpu::TextureFormat,
    pub depth: bool,
    pub color: bool,
    pub clear_depth: bool,
    pub samples: u32,
}

pub struct Pipelines {
    clear_module: wgpu::ShaderModule,
    pub draw_layout: wgpu::BindGroupLayout,
    pub texture_layout: wgpu::BindGroupLayout,
    clear_layout: wgpu::PipelineLayout,
    pub blit_layout: wgpu::BindGroupLayout,
    blit_pipeline_layout: wgpu::PipelineLayout,
    blit_module: wgpu::ShaderModule,
    clears: HashMap<ClearKey, wgpu::RenderPipeline>,
    blits: HashMap<wgpu::TextureFormat, wgpu::RenderPipeline>,
}

fn texture_entry(binding: u32) -> wgpu::BindGroupLayoutEntry {
    wgpu::BindGroupLayoutEntry {
        binding,
        visibility: wgpu::ShaderStages::VERTEX_FRAGMENT,
        ty: wgpu::BindingType::Texture {
            sample_type: wgpu::TextureSampleType::Float { filterable: true },
            view_dimension: wgpu::TextureViewDimension::D2,
            multisampled: false,
        },
        count: None,
    }
}

fn sampler_entry(binding: u32) -> wgpu::BindGroupLayoutEntry {
    wgpu::BindGroupLayoutEntry {
        binding,
        visibility: wgpu::ShaderStages::VERTEX_FRAGMENT,
        ty: wgpu::BindingType::Sampler(wgpu::SamplerBindingType::Filtering),
        count: None,
    }
}

fn uniform_entry(binding: u32, dynamic: bool) -> wgpu::BindGroupLayoutEntry {
    wgpu::BindGroupLayoutEntry {
        binding,
        visibility: wgpu::ShaderStages::VERTEX_FRAGMENT,
        ty: wgpu::BindingType::Buffer {
            ty: wgpu::BufferBindingType::Uniform,
            has_dynamic_offset: dynamic,
            min_binding_size: None,
        },
        count: None,
    }
}

pub fn blend_factor(f: u8) -> wgpu::BlendFactor {
    use wgpu::BlendFactor::*;
    match f {
        0 => Zero,
        1 => One,
        2 => Dst,
        3 => Src,
        4 => OneMinusDst,
        5 => SrcAlpha,
        6 => OneMinusSrc,
        7 => DstAlpha,
        8 => OneMinusDstAlpha,
        9 => SrcAlphaSaturated,
        10 => OneMinusSrcAlpha,
        _ => One,
    }
}

pub fn blend_op(op: u8) -> wgpu::BlendOperation {
    match op {
        1 => wgpu::BlendOperation::Subtract,
        2 => wgpu::BlendOperation::ReverseSubtract,
        3 => wgpu::BlendOperation::Min,
        4 => wgpu::BlendOperation::Max,
        _ => wgpu::BlendOperation::Add,
    }
}

pub fn compare(c: u8) -> wgpu::CompareFunction {
    use wgpu::CompareFunction::*;
    match c {
        1 => Never,
        2 => Less,
        3 => Equal,
        4 => LessEqual,
        5 => Greater,
        6 => NotEqual,
        7 => GreaterEqual,
        _ => Always,
    }
}

pub fn stencil_op(op: u8) -> wgpu::StencilOperation {
    use wgpu::StencilOperation::*;
    match op {
        1 => Zero,
        2 => Replace,
        3 => IncrementClamp,
        4 => DecrementClamp,
        5 => Invert,
        6 => IncrementWrap,
        7 => DecrementWrap,
        _ => Keep,
    }
}

fn color_writes(mask: u8) -> wgpu::ColorWrites {
    let mut w = wgpu::ColorWrites::empty();
    if mask & 8 != 0 {
        w |= wgpu::ColorWrites::RED;
    }
    if mask & 4 != 0 {
        w |= wgpu::ColorWrites::GREEN;
    }
    if mask & 2 != 0 {
        w |= wgpu::ColorWrites::BLUE;
    }
    if mask & 1 != 0 {
        w |= wgpu::ColorWrites::ALPHA;
    }
    w
}

impl Pipelines {
    pub fn new(device: &wgpu::Device) -> Pipelines {
        let clear_module = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("unbound clear"),
            source: wgpu::ShaderSource::Wgsl(include_str!("../shaders/clear.wgsl").into()),
        });
        let blit_module = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("unbound blit"),
            source: wgpu::ShaderSource::Wgsl(include_str!("../shaders/blit.wgsl").into()),
        });
        let draw_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("unbound draw"),
            entries: &[uniform_entry(0, true)],
        });
        let texture_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("unbound main texture"),
            entries: &[texture_entry(0), sampler_entry(1)],
        });
        let clear_layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: Some("unbound clear"),
            bind_group_layouts: &[Some(&draw_layout)],
            immediate_size: 0,
        });
        let blit_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("unbound blit"),
            entries: &[texture_entry(0), sampler_entry(1), uniform_entry(2, false)],
        });
        let blit_pipeline_layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: Some("unbound blit"),
            bind_group_layouts: &[Some(&blit_layout)],
            immediate_size: 0,
        });
        Pipelines {
            clear_module,
            draw_layout,
            texture_layout,
            clear_layout,
            blit_layout,
            blit_pipeline_layout,
            blit_module,
            clears: HashMap::new(),
            blits: HashMap::new(),
        }
    }

    /// Clears a camera's viewport (color, and depth with stencil) by drawing over it.
    pub fn clear(&mut self, device: &wgpu::Device, key: ClearKey) -> wgpu::RenderPipeline {
        if let Some(p) = self.clears.get(&key) {
            return p.clone();
        }
        let pipeline = device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("unbound viewport clear"),
            layout: Some(&self.clear_layout),
            vertex: wgpu::VertexState {
                module: &self.clear_module,
                entry_point: Some("vs_clear"),
                compilation_options: Default::default(),
                buffers: &[],
            },
            primitive: wgpu::PrimitiveState::default(),
            depth_stencil: key.depth.then(|| {
                let face = wgpu::StencilFaceState {
                    compare: wgpu::CompareFunction::Always,
                    fail_op: wgpu::StencilOperation::Replace,
                    depth_fail_op: wgpu::StencilOperation::Replace,
                    pass_op: wgpu::StencilOperation::Replace,
                };
                wgpu::DepthStencilState {
                    format: DEPTH_FORMAT,
                    depth_write_enabled: Some(key.clear_depth),
                    depth_compare: Some(wgpu::CompareFunction::Always),
                    stencil: wgpu::StencilState {
                        front: face,
                        back: face,
                        read_mask: 0xff,
                        write_mask: if key.clear_depth { 0xff } else { 0 },
                    },
                    bias: wgpu::DepthBiasState::default(),
                }
            }),
            multisample: wgpu::MultisampleState {
                count: key.samples,
                ..Default::default()
            },
            fragment: Some(wgpu::FragmentState {
                module: &self.clear_module,
                entry_point: Some("fs_clear"),
                compilation_options: Default::default(),
                targets: &[Some(wgpu::ColorTargetState {
                    format: key.format,
                    blend: None,
                    write_mask: if key.color {
                        wgpu::ColorWrites::ALL
                    } else {
                        wgpu::ColorWrites::empty()
                    },
                })],
            }),
            multiview_mask: None,
            cache: None,
        });
        self.clears.insert(key, pipeline.clone());
        pipeline
    }

    pub fn blit_cached(&self, format: wgpu::TextureFormat) -> Option<wgpu::RenderPipeline> {
        self.blits.get(&format).cloned()
    }

    pub fn blit(
        &mut self,
        device: &wgpu::Device,
        format: wgpu::TextureFormat,
    ) -> wgpu::RenderPipeline {
        if let Some(p) = self.blits.get(&format) {
            return p.clone();
        }
        let pipeline = device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("unbound present"),
            layout: Some(&self.blit_pipeline_layout),
            vertex: wgpu::VertexState {
                module: &self.blit_module,
                entry_point: Some("vs_blit"),
                compilation_options: Default::default(),
                buffers: &[],
            },
            primitive: wgpu::PrimitiveState::default(),
            depth_stencil: None,
            multisample: wgpu::MultisampleState::default(),
            fragment: Some(wgpu::FragmentState {
                module: &self.blit_module,
                entry_point: Some("fs_blit"),
                compilation_options: Default::default(),
                targets: &[Some(wgpu::ColorTargetState {
                    format,
                    blend: None,
                    write_mask: wgpu::ColorWrites::ALL,
                })],
            }),
            multiview_mask: None,
            cache: None,
        });
        self.blits.insert(format, pipeline.clone());
        pipeline
    }
}

/// A pipeline drawing with Unity render state `key.state` (blend, depth, stencil, culling, color
/// mask) through the given layout and entry points.
pub fn create_pipeline(
    device: &wgpu::Device,
    layout: &wgpu::PipelineLayout,
    vertex: (&wgpu::ShaderModule, &str),
    fragment: (&wgpu::ShaderModule, &str),
    key: PipelineKey,
) -> wgpu::RenderPipeline {
    let s = key.state;
    let blend = s.blend.map(|[src, dst, src_a, dst_a]| wgpu::BlendState {
        color: wgpu::BlendComponent {
            src_factor: blend_factor(src),
            dst_factor: blend_factor(dst),
            operation: blend_op(s.blend_op[0]),
        },
        alpha: wgpu::BlendComponent {
            src_factor: blend_factor(src_a),
            dst_factor: blend_factor(dst_a),
            operation: blend_op(s.blend_op[1]),
        },
    });
    // Min/Max ignore the factors, which wgpu requires to be One.
    let blend = blend.map(|mut b| {
        for c in [&mut b.color, &mut b.alpha] {
            if matches!(
                c.operation,
                wgpu::BlendOperation::Min | wgpu::BlendOperation::Max
            ) {
                c.src_factor = wgpu::BlendFactor::One;
                c.dst_factor = wgpu::BlendFactor::One;
            }
        }
        b
    });
    let depth_stencil = key.depth.then(|| {
        let stencil = match s.stencil {
            Some(st) => {
                let face = wgpu::StencilFaceState {
                    compare: compare(st.comp),
                    fail_op: stencil_op(st.fail),
                    depth_fail_op: stencil_op(st.zfail),
                    pass_op: stencil_op(st.pass),
                };
                wgpu::StencilState {
                    front: face,
                    back: face,
                    read_mask: st.read_mask as u32,
                    write_mask: st.write_mask as u32,
                }
            }
            None => wgpu::StencilState::default(),
        };
        wgpu::DepthStencilState {
            format: DEPTH_FORMAT,
            depth_write_enabled: Some(s.zwrite),
            depth_compare: Some(compare(s.ztest)),
            stencil,
            bias: wgpu::DepthBiasState {
                // Constant bias is whole units in wgpu; Unity's are rounded the same way.
                constant: s.offset[1] + f32::from_bits(key.depth_bias[0]).round() as i32,
                slope_scale: s.offset[0] as f32 + f32::from_bits(key.depth_bias[1]),
                clamp: 0.0,
            },
        }
    });
    let triangles = key.topology == Topology::Triangles;
    device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
        label: Some("unbound draw"),
        layout: Some(layout),
        vertex: wgpu::VertexState {
            module: vertex.0,
            entry_point: Some(vertex.1),
            compilation_options: Default::default(),
            buffers: &[Some(vertex_layout())],
        },
        primitive: wgpu::PrimitiveState {
            topology: key.topology.wgpu(),
            strip_index_format: None,
            // Targets are stored bottom-up (clip y negated), which turns Unity's clockwise
            // front faces counter-clockwise.
            front_face: wgpu::FrontFace::Ccw,
            cull_mode: match (triangles, s.cull) {
                (true, 1) => Some(wgpu::Face::Front),
                (true, 2) => Some(wgpu::Face::Back),
                _ => None,
            },
            unclipped_depth: false,
            polygon_mode: wgpu::PolygonMode::Fill,
            conservative: false,
        },
        depth_stencil,
        multisample: wgpu::MultisampleState {
            count: key.samples,
            ..Default::default()
        },
        fragment: Some(wgpu::FragmentState {
            module: fragment.0,
            entry_point: Some(fragment.1),
            compilation_options: Default::default(),
            targets: &[Some(wgpu::ColorTargetState {
                format: key.format,
                blend,
                write_mask: color_writes(s.color_mask),
            })],
        }),
        multiview_mask: None,
        cache: None,
    })
}

#[cfg(test)]
mod tests {
    fn validate(source: &str) {
        let module = naga::front::wgsl::parse_str(source).unwrap_or_else(|e| {
            panic!("{}", e.emit_to_string(source));
        });
        naga::valid::Validator::new(
            naga::valid::ValidationFlags::all(),
            naga::valid::Capabilities::empty(),
        )
        .validate(&module)
        .unwrap_or_else(|e| panic!("{}", e.emit_to_string(source)));
    }

    #[test]
    fn shaders_validate() {
        validate(include_str!("../shaders/clear.wgsl"));
        validate(include_str!("../shaders/blit.wgsl"));
    }
}
