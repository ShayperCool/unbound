//! The rendering command stream (docs/RENDER_PROTOCOL.md): what the managed engine asks the GPU
//! to do, in order. Camera rendering, `CommandBuffer`s, `Graphics.Blit` and the other immediate
//! calls all become these commands; the renderer keeps their state (target, viewport, matrices,
//! global properties and keywords) from one command to the next, across submissions and frames.
//!
//! Encoding (little endian), one command after another: `u8 opcode` and its fields. Strings are
//! `u32 length` and UTF-8 bytes; matrices are 16 `f32`s, column-major as Unity's `Matrix4x4`.
use crate::render_protocol::owned::PropertyValue;

/// A texture handle meaning "none" (no depth attachment).
pub const NO_TEXTURE: u64 = u64::MAX;

/// `Clear` flags.
pub mod clear {
    pub const COLOR: u8 = 1;
    pub const DEPTH: u8 = 2;
    pub const STENCIL: u8 = 4;
}

/// `Camera` flags.
pub mod camera {
    /// The screen-space overlay canvases' pass: `unity_GUIZTestMode` is Always there.
    pub const OVERLAY: u32 = 1;
}

#[derive(Debug, Clone, PartialEq)]
pub struct Draw {
    pub mesh: u64,
    pub submesh: i32,
    pub material: u64,
    /// A pass of the material's shader, or [`FORWARD_PASSES`], [`ALL_PASSES`], [`TAGGED_PASSES`].
    pub pass: i32,
    pub model: [f32; 16],
    /// Multiplies the vertex color (a CanvasRenderer's color and alpha).
    pub color: [f32; 4],
    /// Overrides the material's `_MainTex` (a sprite's or CanvasRenderer's texture); 0: none.
    pub main_texture: u64,
    /// Pixels, origin bottom-left: a scissor rectangle within the viewport.
    pub clip: Option<[f32; 4]>,
    /// Per-draw properties over the material's (`MaterialPropertyBlock`).
    pub properties: Vec<(String, PropertyValue)>,
}

/// Buffer usage flags (GraphicsBuffer.Target's).
pub mod buffer {
    pub const VERTEX: u32 = 1;
    pub const INDEX: u32 = 2;
    pub const STRUCTURED: u32 = 16;
    pub const RAW: u32 = 32;
    pub const APPEND: u32 = 64;
    pub const COUNTER: u32 = 128;
    pub const INDIRECT_ARGUMENTS: u32 = 256;
    pub const CONSTANT: u32 = 512;
}

#[derive(Debug, Clone, PartialEq)]
pub struct Dispatch {
    /// The `.compute` file, as the engine's working directory sees it.
    pub file: String,
    pub kernel: String,
    pub keywords: Vec<String>,
    pub groups: [u32; 3],
    pub properties: Vec<(String, PropertyValue)>,
}

/// [`Draw::pass`]: the passes the built-in forward renderer draws.
pub const FORWARD_PASSES: i32 = -1;
/// [`Draw::pass`]: every pass of the SubShader.
pub const ALL_PASSES: i32 = -2;
/// [`Draw::pass`]: the passes whose `LightMode` is one of the [`Command::PassTags`].
pub const TAGGED_PASSES: i32 = -3;

#[derive(Debug, Clone, PartialEq)]
pub enum Command {
    /// A frame starts: the screen's size and the project's color space (0 gamma, 1 linear).
    BeginFrame {
        screen_width: i32,
        screen_height: i32,
        color_space: i32,
    },
    /// The frame is complete: the screen is shown.
    EndFrame,
    /// Draws go into `color` (0: the screen) and `depth` (0: the color target's own depth buffer,
    /// [`NO_TEXTURE`]: none). The viewport becomes the whole target.
    SetTarget {
        color: u64,
        depth: u64,
        mip: i32,
        slice: i32,
    },
    /// Clears the viewport of the target ([`clear`] flags).
    Clear {
        flags: u8,
        color: [f32; 4],
        depth: f32,
        stencil: u32,
    },
    /// Pixels (x, y, width, height), origin bottom-left.
    Viewport([f32; 4]),
    /// The view (`worldToCameraMatrix`) and projection (OpenGL convention) of the draws that
    /// follow; [`camera`] flags.
    Camera {
        view: [f32; 16],
        projection: [f32; 16],
        flags: u32,
    },
    /// A global shader property (`Shader.SetGlobal*`).
    Global(String, PropertyValue),
    /// A global shader keyword on or off.
    Keyword(String, bool),
    /// The active render pipeline's tag (`Shader.globalRenderPipeline`, empty for the built-in
    /// one): materials draw with the first SubShader whose `RenderPipeline` tag is it or absent.
    RenderPipeline(String),
    /// The `LightMode` tags that [`Draw`]s with pass [`TAGGED_PASSES`] draw the passes of, in
    /// order (an untagged pass is `SRPDefaultUnlit`).
    PassTags(Vec<String>),
    Draw(Box<Draw>),
    /// A buffer (GraphicsBuffer, ComputeBuffer) of `size` bytes, zeroed; [`buffer`] usage flags.
    CreateBuffer {
        handle: u64,
        size: u64,
        usage: u32,
    },
    /// Bytes written into a buffer at an offset.
    BufferData {
        handle: u64,
        offset: u64,
        data: Vec<u8>,
    },
    ReleaseBuffer(u64),
    /// Runs a compute kernel of a `.compute` file over thread groups, with the dispatch's
    /// properties (uniform values, buffers, textures, RW texture mips) over the global ones.
    Dispatch(Box<Dispatch>),
    /// Copies texels: a rectangle (x, y, width, height; a negative width: the whole level) of a
    /// slice and mip of `source` to `destination` at (x, y).
    CopyTexture {
        source: u64,
        source_slice: i32,
        source_mip: i32,
        source_rect: [i32; 4],
        destination: u64,
        destination_slice: i32,
        destination_mip: i32,
        destination_x: i32,
        destination_y: i32,
    },
}

mod op {
    pub const BEGIN_FRAME: u8 = 1;
    pub const END_FRAME: u8 = 2;
    pub const SET_TARGET: u8 = 3;
    pub const CLEAR: u8 = 4;
    pub const VIEWPORT: u8 = 5;
    pub const CAMERA: u8 = 6;
    pub const GLOBAL: u8 = 7;
    pub const KEYWORD: u8 = 8;
    pub const DRAW: u8 = 9;
    pub const COPY_TEXTURE: u8 = 10;
    pub const CREATE_BUFFER: u8 = 11;
    pub const BUFFER_DATA: u8 = 12;
    pub const RELEASE_BUFFER: u8 = 13;
    pub const DISPATCH: u8 = 14;
    pub const RENDER_PIPELINE: u8 = 15;
    pub const PASS_TAGS: u8 = 16;
}

/// Property kinds in the stream.
mod kind {
    pub const FLOAT: u8 = 0;
    pub const VECTOR: u8 = 1;
    pub const TEXTURE: u8 = 2;
    pub const INT: u8 = 3;
    pub const MATRIX: u8 = 4;
    pub const BUFFER: u8 = 5;
    pub const ARRAY: u8 = 6;
    pub const IMAGE: u8 = 7;
}

struct Reader<'a> {
    bytes: &'a [u8],
    at: usize,
}

impl<'a> Reader<'a> {
    fn take(&mut self, n: usize) -> Result<&'a [u8], String> {
        let end = self.at.checked_add(n).filter(|&e| e <= self.bytes.len());
        let Some(end) = end else {
            return Err(format!(
                "command stream ends inside a command at byte {}",
                self.at
            ));
        };
        let out = &self.bytes[self.at..end];
        self.at = end;
        Ok(out)
    }

    fn u8(&mut self) -> Result<u8, String> {
        Ok(self.take(1)?[0])
    }

    fn u32(&mut self) -> Result<u32, String> {
        Ok(u32::from_le_bytes(self.take(4)?.try_into().unwrap()))
    }

    fn i32(&mut self) -> Result<i32, String> {
        Ok(self.u32()? as i32)
    }

    fn u64(&mut self) -> Result<u64, String> {
        Ok(u64::from_le_bytes(self.take(8)?.try_into().unwrap()))
    }

    fn f32(&mut self) -> Result<f32, String> {
        Ok(f32::from_bits(self.u32()?))
    }

    fn floats<const N: usize>(&mut self) -> Result<[f32; N], String> {
        let mut out = [0.0; N];
        for v in &mut out {
            *v = self.f32()?;
        }
        Ok(out)
    }

    fn string(&mut self) -> Result<String, String> {
        let n = self.u32()? as usize;
        String::from_utf8(self.take(n)?.to_vec()).map_err(|_| "a string is not UTF-8".into())
    }

    fn property(&mut self) -> Result<(String, PropertyValue), String> {
        let name = self.string()?;
        let value = match self.u8()? {
            kind::FLOAT => PropertyValue::Float(self.f32()?),
            kind::VECTOR => PropertyValue::Vector(self.floats()?),
            kind::TEXTURE => PropertyValue::Texture {
                handle: self.u64()?,
                st: self.floats()?,
            },
            kind::INT => PropertyValue::Int(self.i32()?),
            kind::MATRIX => PropertyValue::Matrix(self.floats()?),
            kind::BUFFER => PropertyValue::Buffer(self.u64()?),
            kind::ARRAY => {
                let components = self.u32()?;
                let n = self.u32()? as usize;
                let mut data = Vec::with_capacity(n.min(1 << 16));
                for _ in 0..n {
                    data.push(self.f32()?);
                }
                PropertyValue::Array { components, data }
            }
            kind::IMAGE => PropertyValue::Image {
                handle: self.u64()?,
                mip: self.i32()?,
            },
            k => return Err(format!("unknown property kind {k}")),
        };
        Ok((name, value))
    }
}

/// Decodes a stream; an unknown opcode or a truncated command is an error.
pub fn parse(bytes: &[u8]) -> Result<Vec<Command>, String> {
    let mut r = Reader { bytes, at: 0 };
    let mut out = Vec::new();
    while r.at < bytes.len() {
        let command = match r.u8()? {
            op::BEGIN_FRAME => Command::BeginFrame {
                screen_width: r.i32()?,
                screen_height: r.i32()?,
                color_space: r.i32()?,
            },
            op::END_FRAME => Command::EndFrame,
            op::SET_TARGET => Command::SetTarget {
                color: r.u64()?,
                depth: r.u64()?,
                mip: r.i32()?,
                slice: r.i32()?,
            },
            op::CLEAR => Command::Clear {
                flags: r.u8()?,
                color: r.floats()?,
                depth: r.f32()?,
                stencil: r.u32()?,
            },
            op::VIEWPORT => Command::Viewport(r.floats()?),
            op::CAMERA => Command::Camera {
                view: r.floats()?,
                projection: r.floats()?,
                flags: r.u32()?,
            },
            op::GLOBAL => {
                let (name, value) = r.property()?;
                Command::Global(name, value)
            }
            op::KEYWORD => Command::Keyword(r.string()?, r.u8()? != 0),
            op::RENDER_PIPELINE => Command::RenderPipeline(r.string()?),
            op::PASS_TAGS => {
                let count = r.u32()? as usize;
                let mut tags = Vec::with_capacity(count.min(64));
                for _ in 0..count {
                    tags.push(r.string()?);
                }
                Command::PassTags(tags)
            }
            op::DRAW => {
                let mesh = r.u64()?;
                let submesh = r.i32()?;
                let material = r.u64()?;
                let pass = r.i32()?;
                let model = r.floats()?;
                let color = r.floats()?;
                let main_texture = r.u64()?;
                let clip_enabled = r.u8()? != 0;
                let clip: [f32; 4] = r.floats()?;
                let count = r.u32()? as usize;
                let mut properties = Vec::with_capacity(count.min(64));
                for _ in 0..count {
                    properties.push(r.property()?);
                }
                Command::Draw(Box::new(Draw {
                    mesh,
                    submesh,
                    material,
                    pass,
                    model,
                    color,
                    main_texture,
                    clip: clip_enabled.then_some(clip),
                    properties,
                }))
            }
            op::CREATE_BUFFER => Command::CreateBuffer {
                handle: r.u64()?,
                size: r.u64()?,
                usage: r.u32()?,
            },
            op::BUFFER_DATA => {
                let handle = r.u64()?;
                let offset = r.u64()?;
                let n = r.u32()? as usize;
                Command::BufferData {
                    handle,
                    offset,
                    data: r.take(n)?.to_vec(),
                }
            }
            op::RELEASE_BUFFER => Command::ReleaseBuffer(r.u64()?),
            op::DISPATCH => {
                let file = r.string()?;
                let kernel = r.string()?;
                let keywords = r.string()?.split_whitespace().map(String::from).collect();
                let groups = [r.u32()?, r.u32()?, r.u32()?];
                let count = r.u32()? as usize;
                let mut properties = Vec::with_capacity(count.min(64));
                for _ in 0..count {
                    properties.push(r.property()?);
                }
                Command::Dispatch(Box::new(Dispatch {
                    file,
                    kernel,
                    keywords,
                    groups,
                    properties,
                }))
            }
            op::COPY_TEXTURE => Command::CopyTexture {
                source: r.u64()?,
                source_slice: r.i32()?,
                source_mip: r.i32()?,
                source_rect: [r.i32()?, r.i32()?, r.i32()?, r.i32()?],
                destination: r.u64()?,
                destination_slice: r.i32()?,
                destination_mip: r.i32()?,
                destination_x: r.i32()?,
                destination_y: r.i32()?,
            },
            other => return Err(format!("unknown command {other} at byte {}", r.at - 1)),
        };
        out.push(command);
    }
    Ok(out)
}

/// Encodes commands as the managed engine does (for native producers and tests).
#[derive(Default)]
pub struct Writer {
    pub bytes: Vec<u8>,
}

impl Writer {
    fn u32(&mut self, v: u32) {
        self.bytes.extend_from_slice(&v.to_le_bytes());
    }

    fn u64(&mut self, v: u64) {
        self.bytes.extend_from_slice(&v.to_le_bytes());
    }

    fn floats(&mut self, v: &[f32]) {
        for f in v {
            self.u32(f.to_bits());
        }
    }

    fn string(&mut self, s: &str) {
        self.u32(s.len() as u32);
        self.bytes.extend_from_slice(s.as_bytes());
    }

    fn property(&mut self, name: &str, value: &PropertyValue) {
        self.string(name);
        match value {
            PropertyValue::Float(f) => {
                self.bytes.push(kind::FLOAT);
                self.floats(&[*f]);
            }
            PropertyValue::Vector(v) => {
                self.bytes.push(kind::VECTOR);
                self.floats(v);
            }
            PropertyValue::Texture { handle, st } => {
                self.bytes.push(kind::TEXTURE);
                self.u64(*handle);
                self.floats(st);
            }
            PropertyValue::Int(i) => {
                self.bytes.push(kind::INT);
                self.u32(*i as u32);
            }
            PropertyValue::Matrix(m) => {
                self.bytes.push(kind::MATRIX);
                self.floats(m);
            }
            PropertyValue::Buffer(handle) => {
                self.bytes.push(kind::BUFFER);
                self.u64(*handle);
            }
            PropertyValue::Array { components, data } => {
                self.bytes.push(kind::ARRAY);
                self.u32(*components);
                self.u32(data.len() as u32);
                self.floats(data);
            }
            PropertyValue::Image { handle, mip } => {
                self.bytes.push(kind::IMAGE);
                self.u64(*handle);
                self.u32(*mip as u32);
            }
        }
    }

    pub fn write(&mut self, command: &Command) {
        match command {
            Command::BeginFrame {
                screen_width,
                screen_height,
                color_space,
            } => {
                self.bytes.push(op::BEGIN_FRAME);
                self.u32(*screen_width as u32);
                self.u32(*screen_height as u32);
                self.u32(*color_space as u32);
            }
            Command::EndFrame => self.bytes.push(op::END_FRAME),
            Command::SetTarget {
                color,
                depth,
                mip,
                slice,
            } => {
                self.bytes.push(op::SET_TARGET);
                self.u64(*color);
                self.u64(*depth);
                self.u32(*mip as u32);
                self.u32(*slice as u32);
            }
            Command::Clear {
                flags,
                color,
                depth,
                stencil,
            } => {
                self.bytes.push(op::CLEAR);
                self.bytes.push(*flags);
                self.floats(color);
                self.floats(&[*depth]);
                self.u32(*stencil);
            }
            Command::Viewport(v) => {
                self.bytes.push(op::VIEWPORT);
                self.floats(v);
            }
            Command::Camera {
                view,
                projection,
                flags,
            } => {
                self.bytes.push(op::CAMERA);
                self.floats(view);
                self.floats(projection);
                self.u32(*flags);
            }
            Command::Global(name, value) => {
                self.bytes.push(op::GLOBAL);
                self.property(name, value);
            }
            Command::Keyword(name, on) => {
                self.bytes.push(op::KEYWORD);
                self.string(name);
                self.bytes.push(*on as u8);
            }
            Command::RenderPipeline(tag) => {
                self.bytes.push(op::RENDER_PIPELINE);
                self.string(tag);
            }
            Command::PassTags(tags) => {
                self.bytes.push(op::PASS_TAGS);
                self.u32(tags.len() as u32);
                for tag in tags {
                    self.string(tag);
                }
            }
            Command::CreateBuffer {
                handle,
                size,
                usage,
            } => {
                self.bytes.push(op::CREATE_BUFFER);
                self.u64(*handle);
                self.u64(*size);
                self.u32(*usage);
            }
            Command::BufferData {
                handle,
                offset,
                data,
            } => {
                self.bytes.push(op::BUFFER_DATA);
                self.u64(*handle);
                self.u64(*offset);
                self.u32(data.len() as u32);
                self.bytes.extend_from_slice(data);
            }
            Command::ReleaseBuffer(handle) => {
                self.bytes.push(op::RELEASE_BUFFER);
                self.u64(*handle);
            }
            Command::Dispatch(d) => {
                self.bytes.push(op::DISPATCH);
                self.string(&d.file);
                self.string(&d.kernel);
                self.string(&d.keywords.join(" "));
                for g in d.groups {
                    self.u32(g);
                }
                self.u32(d.properties.len() as u32);
                for (name, value) in &d.properties {
                    self.property(name, value);
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
                self.bytes.push(op::COPY_TEXTURE);
                self.u64(*source);
                self.u32(*source_slice as u32);
                self.u32(*source_mip as u32);
                for v in source_rect {
                    self.u32(*v as u32);
                }
                self.u64(*destination);
                self.u32(*destination_slice as u32);
                self.u32(*destination_mip as u32);
                self.u32(*destination_x as u32);
                self.u32(*destination_y as u32);
            }
            Command::Draw(d) => {
                self.bytes.push(op::DRAW);
                self.u64(d.mesh);
                self.u32(d.submesh as u32);
                self.u64(d.material);
                self.u32(d.pass as u32);
                self.floats(&d.model);
                self.floats(&d.color);
                self.u64(d.main_texture);
                self.bytes.push(d.clip.is_some() as u8);
                self.floats(&d.clip.unwrap_or_default());
                self.u32(d.properties.len() as u32);
                for (name, value) in &d.properties {
                    self.property(name, value);
                }
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn round_trip() {
        let commands = vec![
            Command::BeginFrame {
                screen_width: 960,
                screen_height: 540,
                color_space: 1,
            },
            Command::SetTarget {
                color: 0,
                depth: 0,
                mip: 0,
                slice: 0,
            },
            Command::Clear {
                flags: clear::COLOR | clear::DEPTH,
                color: [0.1, 0.2, 0.3, 1.0],
                depth: 1.0,
                stencil: 0,
            },
            Command::Viewport([0.0, 0.0, 480.0, 540.0]),
            Command::Camera {
                view: [1.0; 16],
                projection: [2.0; 16],
                flags: camera::OVERLAY,
            },
            Command::Global("_Tint".into(), PropertyValue::Vector([1.0, 0.5, 0.25, 1.0])),
            Command::Keyword("FOG_LINEAR".into(), true),
            Command::RenderPipeline("UniversalPipeline".into()),
            Command::PassTags(vec!["UniversalForward".into(), "SRPDefaultUnlit".into()]),
            Command::Draw(Box::new(Draw {
                mesh: 7,
                submesh: 1,
                material: 9,
                pass: -1,
                model: [3.0; 16],
                color: [1.0; 4],
                main_texture: 0,
                clip: Some([1.0, 2.0, 3.0, 4.0]),
                properties: vec![
                    ("_Glow".into(), PropertyValue::Float(2.0)),
                    ("_M".into(), PropertyValue::Matrix([4.0; 16])),
                    (
                        "_Tex".into(),
                        PropertyValue::Texture {
                            handle: 5,
                            st: [1.0, 1.0, 0.0, 0.0],
                        },
                    ),
                ],
            })),
            Command::CopyTexture {
                source: 3,
                source_slice: 0,
                source_mip: 1,
                source_rect: [1, 2, -1, 4],
                destination: 4,
                destination_slice: 2,
                destination_mip: 0,
                destination_x: 5,
                destination_y: 6,
            },
            Command::CreateBuffer {
                handle: 8,
                size: 64,
                usage: buffer::STRUCTURED,
            },
            Command::BufferData {
                handle: 8,
                offset: 4,
                data: vec![1, 2, 3, 4],
            },
            Command::Dispatch(Box::new(Dispatch {
                file: "Assets/A.compute".into(),
                kernel: "CSMain".into(),
                keywords: vec!["ON".into()],
                groups: [2, 3, 1],
                properties: vec![
                    ("_Values".into(), PropertyValue::Buffer(8)),
                    (
                        "_W".into(),
                        PropertyValue::Array {
                            components: 4,
                            data: vec![1.0; 8],
                        },
                    ),
                    ("_Out".into(), PropertyValue::Image { handle: 3, mip: 1 }),
                ],
            })),
            Command::ReleaseBuffer(8),
            Command::EndFrame,
        ];
        let mut w = Writer::default();
        for c in &commands {
            w.write(c);
        }
        assert_eq!(parse(&w.bytes).unwrap(), commands);
        assert!(parse(&w.bytes[..w.bytes.len() - 2]).is_err());
    }
}
