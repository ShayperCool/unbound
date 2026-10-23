//! Exercises the renderer without the managed engine: a synthetic producer submits what the
//! engine would (uploads, materials, cameras and ordered draw lists) through the same backend
//! and frame driver as a game.
//!
//! ```text
//! cargo run -p unbound-app --example protocol_demo -- [--screenshot out.png --frames 5] [--linear]
//! ```
//!
//! The frame: a perspective camera with a lit, rotating cube (Standard); an orthographic camera
//! with overlapping sprites ordered by draw order (Sprites/Default); then a screen-space overlay
//! with UI quads (textured, vertex-colored, alpha-blended), a stencil mask (UI/Default stencil
//! properties, as uGUI's Mask sets them), a RectMask2D-style clipped quad, and SDF text quads
//! (TextMeshPro mobile with outline, TextMeshPro with underlay).
use bevy::math::{Mat4, Quat, Vec3, Vec4};
use std::sync::Arc;
use unbound_app::{AppOptions, FrameDriver, FrameInput, backend::QueuedBackend};
use unbound_runtime::render_protocol::{
    CameraPacket, Color, Color32, DrawPacket, Matrix4x4, Rect, RenderBackend, SubMeshPacket,
    Vector3, Vector4,
    owned::{self, PropertyValue},
};

const CHECKER: u64 = 1;
const CIRCLE: u64 = 2;
const SDF: u64 = 3;
const TARGET: u64 = 4;

const CUBE: u64 = 10;
const QUAD: u64 = 11;
const GRADIENT_QUAD: u64 = 12;

const M_STANDARD: u64 = 100;
const M_SPRITE: u64 = 101;
const M_UI: u64 = 102;
const M_MASK: u64 = 103;
const M_MASKED: u64 = 104;
const M_MASK_POP: u64 = 105;
const M_TMP_MOBILE: u64 = 106;
const M_TMP: u64 = 107;
const M_UI_CLIP: u64 = 108;

struct Demo {
    backend: Arc<QueuedBackend>,
    frame: u32,
    color_space: i32,
}

fn texture(width: i32, height: i32, format: i32, data: Vec<u8>, filter: i32) -> owned::Texture {
    owned::Texture {
        width,
        height,
        format,
        mip_count: 1,
        data,
        filter_mode: filter,
        wrap_u: 1,
        wrap_v: 1,
        aniso_level: 1,
        render_target: false,
        depth_bits: 0,
    }
}

fn v3(x: f32, y: f32, z: f32) -> Vector3 {
    Vector3 { x, y, z }
}

fn v4(x: f32, y: f32, z: f32, w: f32) -> Vector4 {
    Vector4 { x, y, z, w }
}

/// Unity's `Matrix4x4.Perspective` (OpenGL convention).
fn perspective(fov_y: f32, aspect: f32, near: f32, far: f32) -> Mat4 {
    let f = 1.0 / (fov_y * 0.5).tan();
    Mat4::from_cols(
        Vec4::new(f / aspect, 0.0, 0.0, 0.0),
        Vec4::new(0.0, f, 0.0, 0.0),
        Vec4::new(0.0, 0.0, -(far + near) / (far - near), -1.0),
        Vec4::new(0.0, 0.0, -2.0 * far * near / (far - near), 0.0),
    )
}

/// Unity's `Matrix4x4.Ortho`.
fn ortho(left: f32, right: f32, bottom: f32, top: f32, near: f32, far: f32) -> Mat4 {
    Mat4::from_cols(
        Vec4::new(2.0 / (right - left), 0.0, 0.0, 0.0),
        Vec4::new(0.0, 2.0 / (top - bottom), 0.0, 0.0),
        Vec4::new(0.0, 0.0, -2.0 / (far - near), 0.0),
        Vec4::new(
            -(right + left) / (right - left),
            -(top + bottom) / (top - bottom),
            -(far + near) / (far - near),
            1.0,
        ),
    )
}

fn matrix(m: Mat4) -> Matrix4x4 {
    Matrix4x4 {
        m: m.to_cols_array(),
    }
}

/// Unity's Quad: normal -Z, clockwise front faces seen from -Z, uv0.w = `scale` (TMP's SDF
/// scale; unused by other shaders).
fn quad(colors: [Color32; 4], scale: f32) -> owned::Mesh {
    owned::Mesh {
        positions: vec![
            v3(-0.5, -0.5, 0.0),
            v3(0.5, -0.5, 0.0),
            v3(-0.5, 0.5, 0.0),
            v3(0.5, 0.5, 0.0),
        ],
        normals: vec![v3(0.0, 0.0, -1.0); 4],
        colors: colors.to_vec(),
        uv: [
            vec![
                v4(0.0, 0.0, 0.0, scale),
                v4(1.0, 0.0, 0.0, scale),
                v4(0.0, 1.0, 0.0, scale),
                v4(1.0, 1.0, 0.0, scale),
            ],
            vec![
                v4(0.0, 0.0, 0.0, 0.0),
                v4(1.0, 0.0, 0.0, 0.0),
                v4(0.0, 1.0, 0.0, 0.0),
                v4(1.0, 1.0, 0.0, 0.0),
            ],
            vec![],
            vec![],
        ],
        indices: vec![0, 3, 1, 3, 0, 2],
        submeshes: vec![SubMeshPacket {
            index_start: 0,
            index_count: 6,
            topology: 0,
            base_vertex: 0,
        }],
        ..Default::default()
    }
}

/// A unit cube with Unity's winding (clockwise seen from outside).
fn cube() -> owned::Mesh {
    let mut mesh = owned::Mesh::default();
    let faces = [
        (Vec3::Z, Vec3::Y),
        (-Vec3::Z, Vec3::Y),
        (Vec3::X, Vec3::Y),
        (-Vec3::X, Vec3::Y),
        (Vec3::Y, Vec3::Z),
        (-Vec3::Y, Vec3::Z),
    ];
    for (n, up) in faces {
        let forward = -n;
        let right = up.cross(forward);
        let base = mesh.positions.len() as u32;
        for (s, t) in [(-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0), (1.0, 1.0)] {
            let p = n * 0.5 + right * (0.5 * s) + up * (0.5 * t);
            mesh.positions.push(v3(p.x, p.y, p.z));
            mesh.normals.push(v3(n.x, n.y, n.z));
            mesh.uv[0].push(v4((s + 1.0) * 0.5, (t + 1.0) * 0.5, 0.0, 0.0));
        }
        mesh.indices
            .extend([0, 3, 1, 3, 0, 2].iter().map(|i| base + i));
    }
    mesh.submeshes.push(SubMeshPacket {
        index_start: 0,
        index_count: mesh.indices.len() as i32,
        topology: 0,
        base_vertex: 0,
    });
    mesh
}

fn material(shader: &str, keywords: &[&str], props: Vec<(&str, PropertyValue)>) -> owned::Material {
    owned::Material {
        shader: shader.into(),
        render_queue: 3000,
        keywords: keywords.iter().map(|s| s.to_string()).collect(),
        properties: props.into_iter().map(|(n, v)| (n.to_string(), v)).collect(),
    }
}

fn tex(handle: u64) -> PropertyValue {
    PropertyValue::Texture {
        handle,
        st: [1.0, 1.0, 0.0, 0.0],
    }
}

fn color(r: f32, g: f32, b: f32, a: f32) -> PropertyValue {
    PropertyValue::Vector([r, g, b, a])
}

fn c32(r: u8, g: u8, b: u8, a: u8) -> Color32 {
    Color32 { r, g, b, a }
}

impl Demo {
    fn upload(&self) {
        let b = &self.backend;
        // An 8x8 checker, 64x64 texels.
        let mut checker = Vec::new();
        for y in 0..64 {
            for x in 0..64 {
                let on = ((x / 8) + (y / 8)) % 2 == 0;
                let v = if on { 235 } else { 60 };
                checker.extend_from_slice(&[v, v, v, 255]);
            }
        }
        b.upload_texture(CHECKER, texture(64, 64, 0, checker, 0));
        // A soft white disc.
        let mut circle = Vec::new();
        for y in 0..64 {
            for x in 0..64 {
                let d = ((x as f32 - 31.5).powi(2) + (y as f32 - 31.5).powi(2)).sqrt();
                let a = ((30.0 - d) / 2.0).clamp(0.0, 1.0);
                circle.extend_from_slice(&[255, 255, 255, (a * 255.0) as u8]);
            }
        }
        b.upload_texture(CIRCLE, texture(64, 64, 0, circle, 1));
        // An SDF "O" glyph (Alpha8 like TMP atlases): alpha 0.5 on the edge, ±5 texels spread.
        let mut sdf = Vec::new();
        for y in 0..64 {
            for x in 0..64 {
                let r = ((x as f32 - 31.5).powi(2) + (y as f32 - 31.5).powi(2)).sqrt();
                let d = (r - 24.0).max(13.0 - r);
                let a = (0.5 - d / 10.0).clamp(0.0, 1.0);
                sdf.push((a * 255.0) as u8);
            }
        }
        b.upload_texture(SDF, texture(64, 64, 3, sdf, 1));
        // A RenderTexture with depth and stencil.
        b.upload_texture(
            TARGET,
            owned::Texture {
                render_target: true,
                depth_bits: 24,
                ..texture(160, 120, 0, Vec::new(), 1)
            },
        );

        b.upload_mesh(CUBE, cube());
        let white = c32(255, 255, 255, 255);
        b.upload_mesh(
            QUAD,
            quad(
                [
                    c32(255, 60, 60, 220),
                    c32(60, 255, 60, 220),
                    c32(60, 60, 255, 220),
                    white,
                ],
                2.83,
            ),
        );
        b.upload_mesh(
            GRADIENT_QUAD,
            quad(
                [
                    c32(255, 200, 0, 255),
                    c32(255, 0, 120, 255),
                    c32(255, 200, 0, 255),
                    c32(255, 0, 120, 255),
                ],
                2.83,
            ),
        );

        b.update_material(
            M_STANDARD,
            material(
                "Standard",
                &[],
                vec![
                    ("_Color", color(1.0, 0.55, 0.15, 1.0)),
                    ("_MainTex", tex(CHECKER)),
                ],
            ),
        );
        b.update_material(
            M_SPRITE,
            material("Sprites/Default", &[], vec![("_MainTex", tex(CIRCLE))]),
        );
        b.update_material(M_UI, material("UI/Default", &[], vec![]));
        b.update_material(M_UI_CLIP, material("UI/Default", &[], vec![]));
        // uGUI's Mask: the mask graphic writes 1, children test Equal 1, then the mask pops.
        b.update_material(
            M_MASK,
            material(
                "UI/Default",
                &["UNITY_UI_ALPHACLIP"],
                vec![
                    ("_Stencil", PropertyValue::Float(1.0)),
                    ("_StencilComp", PropertyValue::Float(8.0)),
                    ("_StencilOp", PropertyValue::Float(2.0)),
                    ("_StencilReadMask", PropertyValue::Float(255.0)),
                    ("_StencilWriteMask", PropertyValue::Float(255.0)),
                    ("_ColorMask", PropertyValue::Float(0.0)),
                ],
            ),
        );
        b.update_material(
            M_MASKED,
            material(
                "UI/Default",
                &[],
                vec![
                    ("_Stencil", PropertyValue::Float(1.0)),
                    ("_StencilComp", PropertyValue::Float(3.0)),
                    ("_StencilOp", PropertyValue::Float(0.0)),
                    ("_StencilReadMask", PropertyValue::Float(1.0)),
                    ("_StencilWriteMask", PropertyValue::Float(0.0)),
                ],
            ),
        );
        b.update_material(
            M_MASK_POP,
            material(
                "UI/Default",
                &["UNITY_UI_ALPHACLIP"],
                vec![
                    ("_Stencil", PropertyValue::Float(1.0)),
                    ("_StencilComp", PropertyValue::Float(8.0)),
                    ("_StencilOp", PropertyValue::Float(1.0)),
                    ("_StencilReadMask", PropertyValue::Float(255.0)),
                    ("_StencilWriteMask", PropertyValue::Float(255.0)),
                    ("_ColorMask", PropertyValue::Float(0.0)),
                ],
            ),
        );
        let tmp_common = |extra: Vec<(&'static str, PropertyValue)>| {
            let mut v = vec![
                ("_MainTex", tex(SDF)),
                ("_FaceColor", color(1.0, 1.0, 1.0, 1.0)),
                ("_GradientScale", PropertyValue::Float(5.0)),
                ("_TextureWidth", PropertyValue::Float(64.0)),
                ("_TextureHeight", PropertyValue::Float(64.0)),
                ("_ScaleRatioA", PropertyValue::Float(1.0)),
                ("_ScaleRatioC", PropertyValue::Float(1.0)),
            ];
            v.extend(extra);
            v
        };
        b.update_material(
            M_TMP_MOBILE,
            material(
                "TextMeshPro/Mobile/Distance Field",
                &["OUTLINE_ON"],
                tmp_common(vec![
                    ("_OutlineColor", color(0.1, 0.2, 0.9, 1.0)),
                    ("_OutlineWidth", PropertyValue::Float(0.25)),
                ]),
            ),
        );
        b.update_material(
            M_TMP,
            material(
                "TextMeshPro/Distance Field",
                &["UNDERLAY_ON"],
                tmp_common(vec![
                    ("_FaceColor", color(1.0, 0.85, 0.2, 1.0)),
                    ("_UnderlayColor", color(0.0, 0.0, 0.0, 0.8)),
                    ("_UnderlayOffsetX", PropertyValue::Float(0.6)),
                    ("_UnderlayOffsetY", PropertyValue::Float(-0.6)),
                    ("_UnderlaySoftness", PropertyValue::Float(0.3)),
                ]),
            ),
        );
    }

    fn submit(&self, width: i32, height: i32) {
        let (w, h) = (width as f32, height as f32);
        let t = self.frame as f32 / 60.0;
        let mut cameras = Vec::new();
        let mut draws: Vec<DrawPacket> = Vec::new();
        let white = Color {
            r: 1.0,
            g: 1.0,
            b: 1.0,
            a: 1.0,
        };
        let no_clip = Rect::default();
        let draw = |mesh: u64, material: u64, model: Mat4| DrawPacket {
            mesh,
            submesh: 0,
            material,
            model: matrix(model),
            color: white,
            main_texture: 0,
            clip: no_clip,
            clip_enabled: 0,
            flags: 0,
        };

        // 0. A camera rendering the cube into a RenderTexture (shown by the overlay below).
        let camera_to_world = Mat4::from_rotation_translation(
            Quat::from_rotation_x(20f32.to_radians()),
            Vec3::new(0.0, 2.0, -5.0),
        );
        let cube_view = Mat4::from_scale(Vec3::new(1.0, 1.0, -1.0)) * camera_to_world.inverse();
        let spin = Quat::from_rotation_y(t * 0.8 + 0.6) * Quat::from_rotation_x(0.4);
        let cube_model = Mat4::from_rotation_translation(spin, Vec3::new(0.0, 0.6, 0.0))
            * Mat4::from_scale(Vec3::splat(1.6));
        let first = draws.len() as i32;
        draws.push(draw(CUBE, M_STANDARD, cube_model));
        cameras.push(CameraPacket {
            view: matrix(cube_view),
            projection: matrix(perspective(60f32.to_radians(), 160.0 / 120.0, 0.3, 1000.0)),
            viewport: Rect {
                x: 0.0,
                y: 0.0,
                width: 160.0,
                height: 120.0,
            },
            clear_flags: 2,
            clear_color: Color {
                r: 0.35,
                g: 0.1,
                b: 0.1,
                a: 1.0,
            },
            first_draw: first,
            draw_count: 1,
            target_texture: TARGET,
            overlay: 0,
        });

        // 1. Perspective camera at (0, 2, -5), pitched down 20 degrees: a lit cube.
        let camera_to_world = Mat4::from_rotation_translation(
            Quat::from_rotation_x(20f32.to_radians()),
            Vec3::new(0.0, 2.0, -5.0),
        );
        let view = Mat4::from_scale(Vec3::new(1.0, 1.0, -1.0)) * camera_to_world.inverse();
        let projection = perspective(60f32.to_radians(), w / h, 0.3, 1000.0);
        let first = draws.len() as i32;
        let spin = Quat::from_rotation_y(t * 0.8 + 0.6) * Quat::from_rotation_x(0.4);
        draws.push(draw(
            CUBE,
            M_STANDARD,
            Mat4::from_rotation_translation(spin, Vec3::new(0.0, 0.6, 0.0))
                * Mat4::from_scale(Vec3::splat(1.6)),
        ));
        cameras.push(CameraPacket {
            view: matrix(view),
            projection: matrix(projection),
            viewport: Rect {
                x: 0.0,
                y: 0.0,
                width: w,
                height: h,
            },
            clear_flags: 2,
            clear_color: Color {
                r: 0.19,
                g: 0.30,
                b: 0.47,
                a: 0.0,
            },
            first_draw: first,
            draw_count: draws.len() as i32 - first,
            target_texture: 0,
            overlay: 0,
        });

        // 1b. A second view of the cube in a partial viewport (bottom right), cleared inside it.
        let first = draws.len() as i32;
        draws.push(draw(CUBE, M_STANDARD, cube_model));
        cameras.push(CameraPacket {
            view: matrix(cube_view),
            projection: matrix(perspective(60f32.to_radians(), 1.0, 0.3, 1000.0)),
            viewport: Rect {
                x: w - 170.0,
                y: 10.0,
                width: 160.0,
                height: 160.0,
            },
            clear_flags: 2,
            clear_color: Color {
                r: 0.1,
                g: 0.35,
                b: 0.1,
                a: 1.0,
            },
            first_draw: first,
            draw_count: 1,
            target_texture: 0,
            overlay: 0,
        });

        // 2. Orthographic camera (size 5) clearing depth: sprites. The red disc is nearer the
        // camera but drawn first: the draw order decides, so the blue one covers it.
        let size = 5.0;
        let view = Mat4::from_scale(Vec3::new(1.0, 1.0, -1.0))
            * Mat4::from_translation(Vec3::new(0.0, 0.0, 10.0));
        let projection = ortho(-size * w / h, size * w / h, -size, size, 0.3, 1000.0);
        let first = draws.len() as i32;
        let mut red = draw(
            QUAD,
            M_SPRITE,
            Mat4::from_translation(Vec3::new(-0.6, -2.4, -1.0))
                * Mat4::from_scale(Vec3::splat(2.0)),
        );
        // Sprites carry their color in the vertices; this mesh has a vertex gradient, the draw
        // color tints it.
        red.color = Color {
            r: 1.0,
            g: 0.3,
            b: 0.3,
            a: 1.0,
        };
        draws.push(red);
        let mut blue = draw(
            QUAD,
            M_SPRITE,
            Mat4::from_translation(Vec3::new(0.6, -2.4, 1.0)) * Mat4::from_scale(Vec3::splat(2.0)),
        );
        blue.color = Color {
            r: 0.3,
            g: 0.5,
            b: 1.0,
            a: 0.8,
        };
        draws.push(blue);
        cameras.push(CameraPacket {
            view: matrix(view),
            projection: matrix(projection),
            viewport: Rect {
                x: 0.0,
                y: 0.0,
                width: w,
                height: h,
            },
            clear_flags: 3,
            clear_color: Color::default(),
            first_draw: first,
            draw_count: draws.len() as i32 - first,
            target_texture: 0,
            overlay: 0,
        });

        // 3. Screen-space overlay: pixels, origin bottom-left.
        let px = |x: f32, y: f32, sx: f32, sy: f32| {
            Mat4::from_translation(Vec3::new(x, y, 0.0)) * Mat4::from_scale(Vec3::new(sx, sy, 1.0))
        };
        let first = draws.len() as i32;
        // A textured, vertex-colored, translucent panel.
        let mut panel = draw(QUAD, M_UI, px(w * 0.5, h - 140.0, w - 60.0, 200.0));
        panel.main_texture = CHECKER;
        draws.push(panel);
        // Overlapping alpha-blended quads with CanvasRenderer alpha.
        for (i, c) in [(1.0, 0.2, 0.2), (0.2, 1.0, 0.2), (0.2, 0.4, 1.0)]
            .iter()
            .enumerate()
        {
            let mut q = draw(
                GRADIENT_QUAD,
                M_UI,
                px(
                    140.0 + 60.0 * i as f32,
                    h - 330.0 - 30.0 * i as f32,
                    120.0,
                    120.0,
                ),
            );
            q.main_texture = 0;
            q.color = Color {
                r: c.0,
                g: c.1,
                b: c.2,
                a: 0.6,
            };
            draws.push(q);
        }
        // A Mask: circle writes stencil 1 (not shown), the checker child shows through it only.
        let mut mask = draw(QUAD, M_MASK, px(w - 140.0, h - 360.0, 180.0, 180.0));
        mask.main_texture = CIRCLE;
        draws.push(mask);
        let mut masked = draw(
            GRADIENT_QUAD,
            M_MASKED,
            px(w - 140.0 + 40.0 * (t * 2.0).sin(), h - 360.0, 260.0, 260.0),
        );
        masked.main_texture = CHECKER;
        draws.push(masked);
        let mut pop = draw(QUAD, M_MASK_POP, px(w - 140.0, h - 360.0, 180.0, 180.0));
        pop.main_texture = CIRCLE;
        draws.push(pop);
        // RectMask2D: clipped to the left half of the quad.
        let mut clipped = draw(GRADIENT_QUAD, M_UI_CLIP, px(w * 0.5, 120.0, w - 80.0, 80.0));
        clipped.clip = Rect {
            x: 40.0,
            y: 60.0,
            width: (w - 80.0) * 0.5,
            height: 120.0,
        };
        clipped.clip_enabled = 1;
        draws.push(clipped);
        // The RenderTexture, upright.
        let mut rt = draw(QUAD, M_UI, px(100.0, 230.0, 160.0, 120.0));
        rt.main_texture = TARGET;
        draws.push(rt);
        // SDF text: TMP mobile with outline, TMP with underlay.
        draws.push(draw(QUAD, M_TMP_MOBILE, px(150.0, 300.0, 128.0, 128.0)));
        draws.push(draw(QUAD, M_TMP, px(w - 150.0, 300.0, 128.0, 128.0)));
        cameras.push(CameraPacket {
            view: Matrix4x4::default(),
            projection: matrix(ortho(0.0, w, 0.0, h, -100.0, 100.0)),
            viewport: Rect {
                x: 0.0,
                y: 0.0,
                width: w,
                height: h,
            },
            clear_flags: 3,
            clear_color: Color::default(),
            first_draw: first,
            draw_count: draws.len() as i32 - first,
            target_texture: 0,
            overlay: 0,
        });

        self.backend.submit_frame(owned::Frame {
            screen_width: width,
            screen_height: height,
            color_space: self.color_space,
            cameras,
            draws,
            ..Default::default()
        });
    }
}

impl FrameDriver for Demo {
    fn frame(&mut self, input: &FrameInput) -> bool {
        if self.frame == 0 {
            self.upload();
        }
        self.submit(input.screen_width, input.screen_height);
        self.frame += 1;
        // Escape quits, like a game calling Application.Quit.
        let keys = unsafe { std::slice::from_raw_parts(input.keys, 64) };
        keys[27 / 8] & (1 << (27 % 8)) == 0
    }

    fn shutdown(&mut self) {}
}

fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_env("UNBOUND_LOG")
                .unwrap_or_else(|_| "info,wgpu=warn,naga=warn".into()),
        )
        .init();
    let mut options = AppOptions {
        title: "Unbound protocol demo".into(),
        ..Default::default()
    };
    let mut linear = false;
    let mut args = std::env::args().skip(1);
    while let Some(arg) = args.next() {
        match arg.as_str() {
            "--screenshot" => options.screenshot = args.next().map(Into::into),
            "--frames" => options.frames = args.next().and_then(|n| n.parse().ok()),
            "--linear" => linear = true,
            "--size" => {
                if let Some((w, h)) = args.next().as_deref().and_then(|s| s.split_once('x')) {
                    options.width = w.parse()?;
                    options.height = h.parse()?;
                }
            }
            other => anyhow::bail!("unknown argument {other}"),
        }
    }
    if options.screenshot.is_some() && options.frames.is_none() {
        options.frames = Some(5);
    }
    let backend = Arc::new(QueuedBackend::new());
    let demo = Demo {
        backend: backend.clone(),
        frame: 0,
        color_space: i32::from(linear),
    };
    unbound_app::run(Box::new(demo), backend, options)
}
