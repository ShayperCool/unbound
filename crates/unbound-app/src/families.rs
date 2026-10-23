//! Shader families: which WGSL port draws a Unity shader, and how a material's properties,
//! keywords and its shader's ShaderLab render states become the uniform block, textures and
//! pipeline state of a draw.
//!
//! Render states always come from ShaderLab: the project's own source when it has one (TMP,
//! Spine and any custom shader), else the snippets below that restate Unity's built-in shaders.
use crate::shaderlab::{PropertyDefault, ShaderIndex, ShaderLab, Value};
use bytemuck::{Pod, Zeroable};
use std::{collections::HashSet, sync::OnceLock};
use unbound_runtime::render_protocol::owned::{Material, PropertyValue};

/// One WGSL port (entry points in shaders/unity.wgsl).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum Family {
    Ui,
    TmpSprite,
    TmpBitmap,
    Sprite,
    Unlit,
    Lit,
    Particle,
    Tmp,
    Spine,
    Generic,
}

impl Family {
    pub fn entry_points(self) -> (&'static str, &'static str) {
        match self {
            Family::Ui => ("vs_main", "fs_ui"),
            Family::TmpSprite => ("vs_main", "fs_tmp_sprite"),
            Family::TmpBitmap => ("vs_main", "fs_tmp_bitmap"),
            Family::Sprite => ("vs_main", "fs_sprite"),
            Family::Unlit => ("vs_main", "fs_unlit"),
            Family::Lit => ("vs_main", "fs_lit"),
            Family::Particle => ("vs_main", "fs_particle"),
            Family::Tmp => ("vs_tmp", "fs_tmp"),
            Family::Spine => ("vs_main", "fs_spine"),
            Family::Generic => ("vs_main", "fs_generic"),
        }
    }
}

/// Keyword bits in `MaterialUniform::flags[0]` (mirrored in unity.wgsl).
pub mod keywords {
    pub const ALPHACLIP: u32 = 1;
    pub const OUTLINE_ON: u32 = 1 << 1;
    pub const UNDERLAY_ON: u32 = 1 << 2;
    pub const UNDERLAY_INNER: u32 = 1 << 3;
    pub const GLOW_ON: u32 = 1 << 4;
    pub const STRAIGHT_ALPHA: u32 = 1 << 5;
    pub const ALPHATEST_ON: u32 = 1 << 6;
    pub const ALPHAPREMULTIPLY_ON: u32 = 1 << 7;
    pub const ALPHABLEND_ON: u32 = 1 << 8;
    pub const EMISSION: u32 = 1 << 9;
    pub const ALPHAMODULATE_ON: u32 = 1 << 10;
    pub const CANVAS_GROUP_COMPATIBLE: u32 = 1 << 11;
    pub const DARK_COLOR_ALPHA_ADDITIVE: u32 = 1 << 12;
    pub const ENABLE_FILL: u32 = 1 << 13;
    pub const PREMULTIPLY_OUTPUT: u32 = 1 << 14;
    pub const VERTEX_COLOR_GAMMA: u32 = 1 << 15;

    pub fn from_name(name: &str) -> u32 {
        match name {
            "UNITY_UI_ALPHACLIP" => ALPHACLIP,
            "OUTLINE_ON" => OUTLINE_ON,
            "UNDERLAY_ON" => UNDERLAY_ON,
            "UNDERLAY_INNER" => UNDERLAY_INNER,
            "GLOW_ON" => GLOW_ON,
            "_STRAIGHT_ALPHA_INPUT" => STRAIGHT_ALPHA,
            "_ALPHATEST_ON" => ALPHATEST_ON,
            "_ALPHAPREMULTIPLY_ON" => ALPHAPREMULTIPLY_ON,
            "_ALPHABLEND_ON" => ALPHABLEND_ON,
            "_EMISSION" => EMISSION,
            "_ALPHAMODULATE_ON" => ALPHAMODULATE_ON,
            "_CANVAS_GROUP_COMPATIBLE" => CANVAS_GROUP_COMPATIBLE,
            "_DARK_COLOR_ALPHA_ADDITIVE" => DARK_COLOR_ALPHA_ADDITIVE,
            _ => 0,
        }
    }
}

/// The material block (`Material` in unity.wgsl); 320 bytes.
#[repr(C)]
#[derive(Debug, Clone, Copy, Pod, Zeroable, PartialEq)]
pub struct MaterialUniform {
    pub color: [f32; 4],
    pub main_st: [f32; 4],
    pub tint: [f32; 4],
    pub sample_add: [f32; 4],
    pub emission: [f32; 4],
    pub color2: [f32; 4],
    pub color3: [f32; 4],
    pub color4: [f32; 4],
    pub st_a: [f32; 4],
    pub st_b: [f32; 4],
    pub p: [[f32; 4]; 8],
    pub flags: [u32; 4],
    pub pad: [f32; 4],
}

/// Unity's enums as stored in materials: `BlendMode`, `BlendOp`, `CompareFunction`, `CullMode`,
/// `StencilOp`, `ColorWriteMask` bits.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct StencilState {
    pub comp: u8,
    pub pass: u8,
    pub fail: u8,
    pub zfail: u8,
    pub read_mask: u8,
    pub write_mask: u8,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct RenderState {
    /// `[src, dst, src_alpha, dst_alpha]` (`BlendMode`); `None` = Blend Off.
    pub blend: Option<[u8; 4]>,
    pub blend_op: [u8; 2],
    pub zwrite: bool,
    pub ztest: u8,
    pub cull: u8,
    pub color_mask: u8,
    pub stencil: Option<StencilState>,
}

impl Default for RenderState {
    fn default() -> Self {
        RenderState {
            blend: None,
            blend_op: [0, 0],
            zwrite: true,
            ztest: 4,
            cull: 2,
            color_mask: 15,
            stencil: None,
        }
    }
}

/// Which texture a slot samples.
#[derive(Debug, Clone, PartialEq)]
pub struct TextureSlot {
    /// 0: none (use the default).
    pub handle: u64,
    /// `white`, `black`, `gray`, `bump`, ...
    pub default: String,
}

#[derive(Debug, Clone)]
pub struct ResolvedMaterial {
    pub shader: String,
    pub family: Family,
    pub variant: u32,
    pub uniform: MaterialUniform,
    pub state: RenderState,
    pub stencil_ref: u32,
    pub main: TextureSlot,
    pub slot_a: TextureSlot,
    pub slot_b: TextureSlot,
}

impl ResolvedMaterial {
    pub fn textures(&self) -> [u64; 3] {
        [self.main.handle, self.slot_a.handle, self.slot_b.handle]
    }
}

/// The family and variant of a shader name, and the ShaderLab restatement of a built-in shader.
fn classify(name: &str) -> (Family, u32, Option<&'static str>) {
    use Family::*;
    let n = name;
    match n {
        "UI/Default" | "UI/Default Font" | "UI/DefaultETC1" | "UI/Unlit/Transparent"
        | "UI/Unlit/Text" | "UI/Unlit/Detail" | "UI/Unlit/Text Detail" | "UI/Lit/Transparent"
        | "UI/Lit/Detail" | "UI/Lit/Bumped" | "UI/Lit/Refraction" | "UI/Lit/Refraction Detail" => {
            (Ui, 0, Some(UI_DEFAULT))
        }
        "Sprites/Default" | "Sprites/Diffuse" | "Sprites/Default (Fast)" => {
            (Sprite, 0, Some(SPRITES_DEFAULT))
        }
        "Sprites/Mask" | "Hidden/Internal-SpriteMask" | "Hidden/Sprites/Mask" => {
            (Sprite, 1, Some(SPRITES_MASK))
        }
        "Unlit/Texture" | "Mobile/Unlit (Supports Lightmap)" | "Legacy Shaders/Unlit/Texture" => {
            (Unlit, 0, Some(OPAQUE))
        }
        "Unlit/Color" => (Unlit, 1, Some(OPAQUE)),
        "Unlit/Transparent" => (Unlit, 2, Some(TRANSPARENT)),
        "Unlit/Transparent Cutout" => (Unlit, 3, Some(OPAQUE)),
        "Legacy Shaders/Diffuse" | "Legacy Shaders/VertexLit" | "Legacy Shaders/Bumped Diffuse"
        | "Legacy Shaders/Specular" | "Legacy Shaders/Bumped Specular"
        | "Legacy Shaders/Self-Illumin/Diffuse" | "Diffuse" | "VertexLit" => {
            (Lit, 0, Some(OPAQUE))
        }
        "Legacy Shaders/Transparent/Diffuse" | "Legacy Shaders/Transparent/VertexLit"
        | "Legacy Shaders/Transparent/Specular" | "Legacy Shaders/Transparent/Bumped Diffuse" => {
            (Lit, 0, Some(TRANSPARENT))
        }
        "Legacy Shaders/Transparent/Cutout/Diffuse"
        | "Legacy Shaders/Transparent/Cutout/VertexLit"
        | "Legacy Shaders/Transparent/Cutout/Specular"
        | "Legacy Shaders/Transparent/Cutout/Bumped Diffuse" => (Lit, 0, Some(OPAQUE)),
        "Mobile/Diffuse" | "Mobile/VertexLit" | "Mobile/Bumped Diffuse" | "Mobile/Bumped Specular"
        | "Mobile/VertexLit (Only Directional Lights)" => (Lit, 1, Some(OPAQUE)),
        "Standard" | "Standard (Specular setup)" | "Autodesk Interactive" => {
            (Lit, 2, Some(STANDARD))
        }
        "Legacy Shaders/Particles/Additive" | "Particles/Additive" => {
            (Particle, 0, Some(PARTICLES_ADDITIVE))
        }
        "Legacy Shaders/Particles/Alpha Blended" | "Particles/Alpha Blended" => {
            (Particle, 0, Some(PARTICLES_ALPHA_BLENDED))
        }
        "Legacy Shaders/Particles/Additive (Soft)" | "Particles/Additive (Soft)" => {
            (Particle, 1, Some(PARTICLES_ADDITIVE_SOFT))
        }
        "Legacy Shaders/Particles/Alpha Blended Premultiply"
        | "Particles/Alpha Blended Premultiply" => (Particle, 1, Some(PARTICLES_PREMULTIPLY)),
        "Legacy Shaders/Particles/Multiply" | "Particles/Multiply" | "Mobile/Particles/Multiply" => {
            (Particle, 4, Some(PARTICLES_MULTIPLY))
        }
        "Mobile/Particles/Additive" => (Particle, 2, Some(PARTICLES_ADDITIVE)),
        "Mobile/Particles/Alpha Blended" | "Mobile/Particles/VertexLit Blended" => {
            (Particle, 2, Some(PARTICLES_ALPHA_BLENDED))
        }
        "Particles/Standard Unlit" | "Particles/Standard Surface" => {
            (Particle, 3, Some(PARTICLES_STANDARD))
        }
        "TextMeshPro/Sprite" => (TmpSprite, 0, Some(TMP_STATES_STRAIGHT)),
        _ if n.starts_with("TextMeshPro/Mobile/Bitmap") || n.starts_with("TextMeshPro/Bitmap") => {
            (TmpBitmap, 0, Some(TMP_STATES_STRAIGHT))
        }
        _ if n.starts_with("TextMeshPro/Mobile/Distance Field") => (Tmp, 1, Some(TMP_STATES)),
        _ if n.starts_with("TextMeshPro/Distance Field") => (Tmp, 0, Some(TMP_STATES)),
        _ if n.starts_with("Spine/") => {
            let base = n.strip_prefix("Spine/Outline/").map_or(n, |rest| rest);
            let base = base.strip_prefix("Spine/").unwrap_or(base);
            let variant = if base.starts_with("SkeletonGraphic Tint Black") {
                7
            } else if base.starts_with("SkeletonGraphic") {
                6
            } else if base.starts_with("Blend Modes/") {
                1
            } else if base.starts_with("Skeleton Tint Black") {
                3
            } else if base.starts_with("Skeleton Tint") {
                2
            } else if base.starts_with("Skeleton Fill") {
                4
            } else if base.starts_with("Special/Skeleton Grayscale") {
                5
            } else {
                0
            };
            (Spine, variant, Some(SPINE_STATES))
        }
        _ => (Generic, 0, None),
    }
}

// ShaderLab restatements of Unity's built-in shaders (states and property defaults).

const UI_DEFAULT: &str = r#"Shader "UI/Default" {
Properties {
    _MainTex ("Sprite Texture", 2D) = "white" {}
    _Color ("Tint", Color) = (1,1,1,1)
    _StencilComp ("Stencil Comparison", Float) = 8
    _Stencil ("Stencil ID", Float) = 0
    _StencilOp ("Stencil Operation", Float) = 0
    _StencilWriteMask ("Stencil Write Mask", Float) = 255
    _StencilReadMask ("Stencil Read Mask", Float) = 255
    _ColorMask ("Color Mask", Float) = 15
}
SubShader {
    Tags { "Queue"="Transparent" }
    Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
    Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
    Blend One OneMinusSrcAlpha
    ColorMask [_ColorMask]
    Pass { }
}}"#;

const SPRITES_DEFAULT: &str = r#"Shader "Sprites/Default" {
Properties {
    _MainTex ("Sprite Texture", 2D) = "white" {}
    _Color ("Tint", Color) = (1,1,1,1)
    _RendererColor ("RendererColor", Color) = (1,1,1,1)
    _AlphaTex ("External Alpha", 2D) = "white" {}
    _EnableExternalAlpha ("Enable External Alpha", Float) = 0
}
SubShader {
    Tags { "Queue"="Transparent" }
    Cull Off Lighting Off ZWrite Off
    Blend One OneMinusSrcAlpha
    Pass { }
}}"#;

const SPRITES_MASK: &str = r#"Shader "Sprites/Mask" {
Properties {
    _MainTex ("Sprite Texture", 2D) = "white" {}
    _Cutoff ("Mask alpha cutoff", Range(0,1)) = 0.0
    _Stencil ("Stencil ID", Float) = 1
    _StencilComp ("Stencil Comparison", Float) = 8
    _StencilOp ("Stencil Operation", Float) = 2
    _StencilReadMask ("Stencil Read Mask", Float) = 255
    _StencilWriteMask ("Stencil Write Mask", Float) = 255
}
SubShader {
    Tags { "Queue"="Transparent" }
    Cull Off Lighting Off ZWrite Off
    ColorMask 0
    Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
    Pass { }
}}"#;

const OPAQUE: &str = r#"Shader "Opaque" {
Properties {
    _MainTex ("Base", 2D) = "white" {}
    _Color ("Main Color", Color) = (1,1,1,1)
    _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
}
SubShader { Tags { "Queue"="Geometry" } Pass { } }}"#;

const TRANSPARENT: &str = r#"Shader "Transparent" {
Properties {
    _MainTex ("Base", 2D) = "white" {}
    _Color ("Main Color", Color) = (1,1,1,1)
}
SubShader { Tags { "Queue"="Transparent" } ZWrite Off Blend SrcAlpha OneMinusSrcAlpha Pass { } }}"#;

const STANDARD: &str = r#"Shader "Standard" {
Properties {
    _Color ("Color", Color) = (1,1,1,1)
    _MainTex ("Albedo", 2D) = "white" {}
    _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
    _Glossiness ("Smoothness", Range(0.0, 1.0)) = 0.5
    _Metallic ("Metallic", Range(0.0, 1.0)) = 0.0
    _EmissionColor ("Color", Color) = (0,0,0)
    _EmissionMap ("Emission", 2D) = "white" {}
    _Mode ("__mode", Float) = 0.0
    _SrcBlend ("__src", Float) = 1.0
    _DstBlend ("__dst", Float) = 0.0
    _ZWrite ("__zw", Float) = 1.0
}
SubShader {
    Tags { "RenderType"="Opaque" }
    Pass { Name "FORWARD" Tags { "LightMode" = "ForwardBase" } Blend [_SrcBlend] [_DstBlend] ZWrite [_ZWrite] }
}}"#;

const PARTICLES_ADDITIVE: &str = r#"Shader "Legacy Shaders/Particles/Additive" {
Properties { _TintColor ("Tint Color", Color) = (0.5,0.5,0.5,0.5) _MainTex ("Particle Texture", 2D) = "white" {} }
Category {
    Tags { "Queue"="Transparent" }
    Blend SrcAlpha One
    ColorMask RGB
    Cull Off Lighting Off ZWrite Off
    SubShader { Pass { } }
}}"#;

const PARTICLES_ALPHA_BLENDED: &str = r#"Shader "Legacy Shaders/Particles/Alpha Blended" {
Properties { _TintColor ("Tint Color", Color) = (0.5,0.5,0.5,0.5) _MainTex ("Particle Texture", 2D) = "white" {} }
Category {
    Tags { "Queue"="Transparent" }
    Blend SrcAlpha OneMinusSrcAlpha
    ColorMask RGB
    Cull Off Lighting Off ZWrite Off
    SubShader { Pass { } }
}}"#;

const PARTICLES_ADDITIVE_SOFT: &str = r#"Shader "Legacy Shaders/Particles/Additive (Soft)" {
Properties { _MainTex ("Particle Texture", 2D) = "white" {} }
Category {
    Tags { "Queue"="Transparent" }
    Blend One OneMinusSrcColor
    ColorMask RGB
    Cull Off Lighting Off ZWrite Off
    SubShader { Pass { } }
}}"#;

const PARTICLES_PREMULTIPLY: &str = r#"Shader "Legacy Shaders/Particles/Alpha Blended Premultiply" {
Properties { _MainTex ("Particle Texture", 2D) = "white" {} }
Category {
    Tags { "Queue"="Transparent" }
    Blend One OneMinusSrcAlpha
    ColorMask RGB
    Cull Off Lighting Off ZWrite Off
    SubShader { Pass { } }
}}"#;

const PARTICLES_MULTIPLY: &str = r#"Shader "Legacy Shaders/Particles/Multiply" {
Properties { _MainTex ("Particle Texture", 2D) = "white" {} }
Category {
    Tags { "Queue"="Transparent" }
    Blend Zero SrcColor
    Cull Off Lighting Off ZWrite Off
    SubShader { Pass { } }
}}"#;

const PARTICLES_STANDARD: &str = r#"Shader "Particles/Standard Unlit" {
Properties {
    _MainTex ("Albedo", 2D) = "white" {}
    _Color ("Color", Color) = (1,1,1,1)
    _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
    _Mode ("__mode", Float) = 0.0
    _BlendOp ("__blendop", Float) = 0.0
    _SrcBlend ("__src", Float) = 1.0
    _DstBlend ("__dst", Float) = 0.0
    _ZWrite ("__zw", Float) = 1.0
    _Cull ("__cull", Float) = 2.0
}
SubShader {
    Tags { "RenderType"="Opaque" }
    BlendOp [_BlendOp]
    Blend [_SrcBlend] [_DstBlend]
    ZWrite [_ZWrite]
    Cull [_Cull]
    Pass { }
}}"#;

const TMP_STATES: &str = r#"Shader "TextMeshPro" {
Properties {
    _FaceColor ("Face Color", Color) = (1,1,1,1)
    _OutlineColor ("Outline Color", Color) = (0,0,0,1)
    _UnderlayColor ("Border Color", Color) = (0,0,0,0.5)
    _GlowColor ("Color", Color) = (0, 1, 0, 0.5)
    _WeightBold ("Weight Bold", float) = 0.5
    _ScaleRatioA ("Scale RatioA", float) = 1
    _ScaleRatioB ("Scale RatioB", float) = 1
    _ScaleRatioC ("Scale RatioC", float) = 1
    _MainTex ("Font Atlas", 2D) = "white" {}
    _FaceTex ("Face Texture", 2D) = "white" {}
    _OutlineTex ("Outline Texture", 2D) = "white" {}
    _TextureWidth ("Texture Width", float) = 512
    _TextureHeight ("Texture Height", float) = 512
    _GradientScale ("Gradient Scale", float) = 5.0
    _ScaleX ("Scale X", float) = 1.0
    _ScaleY ("Scale Y", float) = 1.0
    _PerspectiveFilter ("Perspective Correction", Range(0, 1)) = 0.875
    _GlowInner ("Inner", Range(0,1)) = 0.05
    _GlowOuter ("Outer", Range(0,1)) = 0.05
    _GlowPower ("Falloff", Range(1, 0)) = 0.75
    _StencilComp ("Stencil Comparison", Float) = 8
    _Stencil ("Stencil ID", Float) = 0
    _StencilOp ("Stencil Operation", Float) = 0
    _StencilWriteMask ("Stencil Write Mask", Float) = 255
    _StencilReadMask ("Stencil Read Mask", Float) = 255
    _CullMode ("Cull Mode", Float) = 0
    _ColorMask ("Color Mask", Float) = 15
}
SubShader {
    Tags { "Queue"="Transparent" }
    Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
    Cull [_CullMode] ZWrite Off ZTest [unity_GUIZTestMode]
    Blend One OneMinusSrcAlpha
    ColorMask [_ColorMask]
    Pass { }
}}"#;

const TMP_STATES_STRAIGHT: &str = r#"Shader "TextMeshPro Straight" {
Properties {
    _MainTex ("Texture", 2D) = "white" {}
    _FaceTex ("Font Texture", 2D) = "white" {}
    _FaceColor ("Text Color", Color) = (1,1,1,1)
    _Color ("Tint", Color) = (1,1,1,1)
    _StencilComp ("Stencil Comparison", Float) = 8
    _Stencil ("Stencil ID", Float) = 0
    _StencilOp ("Stencil Operation", Float) = 0
    _StencilWriteMask ("Stencil Write Mask", Float) = 255
    _StencilReadMask ("Stencil Read Mask", Float) = 255
    _CullMode ("Cull Mode", Float) = 0
    _ColorMask ("Color Mask", Float) = 15
}
SubShader {
    Tags { "Queue"="Transparent" }
    Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
    Cull [_CullMode] ZWrite Off ZTest [unity_GUIZTestMode]
    Blend SrcAlpha OneMinusSrcAlpha
    ColorMask [_ColorMask]
    Pass { }
}}"#;

const SPINE_STATES: &str = r#"Shader "Spine" {
Properties {
    _Cutoff ("Shadow alpha cutoff", Range(0,1)) = 0.1
    _MainTex ("Main Texture", 2D) = "black" {}
    _Color ("Tint Color", Color) = (1,1,1,1)
    _Black ("Dark Color", Color) = (0,0,0,0)
    _FillColor ("FillColor", Color) = (1,1,1,1)
    _FillPhase ("FillPhase", Range(0, 1)) = 0
    _GrayPhase ("Phase", Range(0, 1)) = 1
    _StencilRef ("Stencil Reference", Float) = 1.0
    _StencilComp ("Stencil Comparison", Float) = 8
}
SubShader {
    Tags { "Queue"="Transparent" }
    Cull Off ZWrite Off
    Blend One OneMinusSrcAlpha
    Stencil { Ref [_StencilRef] Comp [_StencilComp] Pass Keep }
    Pass { }
}}"#;

fn builtin(text: &'static str) -> &'static ShaderLab {
    static PARSED: OnceLock<std::sync::Mutex<Vec<(usize, &'static ShaderLab)>>> = OnceLock::new();
    let cache = PARSED.get_or_init(Default::default);
    let mut cache = cache.lock().unwrap();
    let key = text.as_ptr() as usize;
    if let Some((_, lab)) = cache.iter().find(|(k, _)| *k == key) {
        return lab;
    }
    let lab: &'static ShaderLab = Box::leak(Box::new(
        ShaderLab::parse(text).expect("built-in ShaderLab parses"),
    ));
    cache.push((key, lab));
    lab
}

/// Resolves a material for drawing; logs once per shader that falls back to the generic family.
pub struct Resolver {
    pub index: ShaderIndex,
    logged: HashSet<String>,
}

impl Resolver {
    pub fn new(index: ShaderIndex) -> Resolver {
        Resolver {
            index,
            logged: HashSet::new(),
        }
    }

    pub fn resolve(&mut self, material: &Material) -> ResolvedMaterial {
        let (family, variant, builtin_text) = classify(&material.shader);
        let project = self.index.get(&material.shader).cloned();
        let lab = match (&project, builtin_text) {
            (Some(lab), _) => lab.clone(),
            (None, Some(text)) => builtin(text).clone(),
            (None, None) => ShaderLab::default(),
        };
        if family == Family::Generic && self.logged.insert(material.shader.clone()) {
            if project.is_some() {
                tracing::info!(
                    "shader `{}`: drawn by the generic family with its ShaderLab render states",
                    material.shader
                );
            } else {
                tracing::warn!(
                    "shader `{}` is unknown (not built in, not in the project): drawn by the generic family with default states",
                    material.shader
                );
            }
        }
        if lab.pass_count > 1 && self.logged.insert(format!("{}#passes", material.shader)) {
            tracing::warn!(
                "shader `{}` has {} passes; only the first is drawn",
                material.shader,
                lab.pass_count
            );
        }
        let props = Props {
            material,
            lab: &lab,
        };
        let state = render_state(&props);
        let stencil_ref = lab
            .states
            .stencil
            .as_ref()
            .and_then(|s| s.reference.as_ref())
            .map(|v| props.number(v, 0.0) as u32 & 0xff)
            .unwrap_or(0);

        let mut flags = 0u32;
        for keyword in &material.keywords {
            flags |= keywords::from_name(keyword);
        }
        if material.shader.contains("Cutout") {
            flags |= keywords::ALPHATEST_ON;
        }
        if family == Family::Spine && variant == 6 && material.shader.contains("Fill") {
            flags |= keywords::ENABLE_FILL;
        }
        if matches!(state.blend, Some([1, 10, ..])) {
            flags |= keywords::PREMULTIPLY_OUTPUT;
        }
        if props.float("_UIVertexColorAlwaysGammaSpace", 0.0) != 0.0 {
            flags |= keywords::VERTEX_COLOR_GAMMA;
        }

        let white = [1.0; 4];
        let mut u = MaterialUniform::zeroed();
        let is_tmp = matches!(family, Family::Tmp | Family::TmpBitmap);
        u.color = if is_tmp {
            props.color("_FaceColor", white)
        } else {
            props.color("_Color", white)
        };
        u.main_st = props.st("_MainTex");
        u.tint = match family {
            Family::Particle => props.color("_TintColor", [0.5; 4]),
            Family::Sprite => props.color("_RendererColor", white),
            Family::Generic => props.color("_TintColor", white),
            _ => white,
        };
        u.sample_add = props.color("_TextureSampleAdd", [0.0; 4]);
        u.emission = props.color("_EmissionColor", [0.0, 0.0, 0.0, 1.0]);
        u.color2 = match (family, variant) {
            (Family::Tmp, _) => props.color("_OutlineColor", [0.0, 0.0, 0.0, 1.0]),
            (Family::Spine, 4) => props.color("_FillColor", white),
            (Family::Spine, 6) if flags & keywords::ENABLE_FILL != 0 => {
                props.color("_FillColor", white)
            }
            (Family::Spine, _) => props.color("_Black", [0.0; 4]),
            _ => [0.0; 4],
        };
        u.color3 = props.color("_UnderlayColor", [0.0, 0.0, 0.0, 0.5]);
        u.color4 = props.color("_GlowColor", [0.0, 1.0, 0.0, 0.5]);
        u.st_a = props.st("_FaceTex");
        u.st_b = props.st("_OutlineTex");
        let f = |name: &str, default: f32| props.float(name, default);
        u.p = if family == Family::Tmp {
            [
                [
                    f("_FaceDilate", 0.0),
                    f("_OutlineWidth", 0.0),
                    f("_OutlineSoftness", 0.0),
                    f("_WeightNormal", 0.0),
                ],
                [
                    f("_WeightBold", 0.5),
                    f("_ScaleRatioA", 1.0),
                    f("_ScaleRatioB", 1.0),
                    f("_ScaleRatioC", 1.0),
                ],
                [
                    f("_GradientScale", 5.0),
                    f("_ScaleX", 1.0),
                    f("_ScaleY", 1.0),
                    f("_PerspectiveFilter", 0.875),
                ],
                [
                    f("_Sharpness", 0.0),
                    f("_TextureWidth", 512.0),
                    f("_TextureHeight", 512.0),
                    f("_VertexOffsetX", 0.0),
                ],
                [
                    f("_VertexOffsetY", 0.0),
                    f("_UnderlayOffsetX", 0.0),
                    f("_UnderlayOffsetY", 0.0),
                    f("_UnderlayDilate", 0.0),
                ],
                [
                    f("_UnderlaySoftness", 0.0),
                    f("_GlowOffset", 0.0),
                    f("_GlowInner", 0.05),
                    f("_GlowOuter", 0.05),
                ],
                [
                    f("_GlowPower", 0.75),
                    f("_FaceUVSpeedX", 0.0),
                    f("_FaceUVSpeedY", 0.0),
                    f("_OutlineUVSpeedX", 0.0),
                ],
                [f("_OutlineUVSpeedY", 0.0), 0.0, 0.0, 0.0],
            ]
        } else {
            let phase = if family == Family::Spine && variant == 5 {
                f("_GrayPhase", 1.0)
            } else {
                f("_FillPhase", 0.0)
            };
            let mut p = [[0.0; 4]; 8];
            p[0] = [
                f("_Cutoff", 0.5),
                f("_Metallic", 0.0),
                phase,
                f("_EnableExternalAlpha", 0.0),
            ];
            p
        };
        u.flags = [flags, variant, 0, 0];

        let slot_a_name = match family {
            Family::Tmp | Family::TmpBitmap => "_FaceTex",
            Family::Lit => "_EmissionMap",
            Family::Sprite => "_AlphaTex",
            _ => "",
        };
        let slot_b_name = if family == Family::Tmp {
            "_OutlineTex"
        } else {
            ""
        };
        ResolvedMaterial {
            shader: material.shader.clone(),
            family,
            variant,
            uniform: u,
            state,
            stencil_ref,
            main: props.texture("_MainTex"),
            slot_a: props.texture(slot_a_name),
            slot_b: props.texture(slot_b_name),
        }
    }
}

struct Props<'a> {
    material: &'a Material,
    lab: &'a ShaderLab,
}

impl Props<'_> {
    fn float(&self, name: &str, default: f32) -> f32 {
        match self.material.get(name) {
            Some(PropertyValue::Float(v)) => return *v,
            Some(PropertyValue::Int(v)) => return *v as f32,
            Some(PropertyValue::Vector(v)) => return v[0],
            _ => {}
        }
        match self.lab.property(name) {
            Some(PropertyDefault::Float(v)) => *v,
            Some(PropertyDefault::Vector(v)) => v[0],
            _ => default,
        }
    }

    fn color(&self, name: &str, default: [f32; 4]) -> [f32; 4] {
        match self.material.get(name) {
            Some(PropertyValue::Vector(v)) => return *v,
            Some(PropertyValue::Float(v)) => return [*v; 4],
            _ => {}
        }
        match self.lab.property(name) {
            Some(PropertyDefault::Vector(v)) => *v,
            _ => default,
        }
    }

    fn st(&self, name: &str) -> [f32; 4] {
        match self.material.get(name) {
            Some(PropertyValue::Texture { st, .. }) if *st != [0.0; 4] => *st,
            _ => match self.material.get(&format!("{name}_ST")) {
                Some(PropertyValue::Vector(v)) => *v,
                _ => [1.0, 1.0, 0.0, 0.0],
            },
        }
    }

    fn texture(&self, name: &str) -> TextureSlot {
        let handle = match self.material.get(name) {
            Some(PropertyValue::Texture { handle, .. }) => *handle,
            _ => 0,
        };
        let default = match self.lab.property(name) {
            Some(PropertyDefault::Texture(t)) if !t.is_empty() => t.clone(),
            _ => "white".to_string(),
        };
        TextureSlot { handle, default }
    }

    /// A state value: a literal (resolved by `word`) or a property.
    fn number(&self, value: &Value, default: f32) -> f32 {
        match value {
            Value::Num(n) => *n,
            Value::Prop(p) if p == "unity_GUIZTestMode" => self.float(p, 4.0),
            Value::Prop(p) => self.float(p, default),
            Value::Word(_) => default,
        }
    }
}

fn blend_factor(v: &Value, props: &Props, default: u8) -> u8 {
    match v {
        Value::Word(w) => match w.as_str() {
            "Zero" => 0,
            "One" => 1,
            "DstColor" => 2,
            "SrcColor" => 3,
            "OneMinusDstColor" => 4,
            "SrcAlpha" => 5,
            "OneMinusSrcColor" => 6,
            "DstAlpha" => 7,
            "OneMinusDstAlpha" => 8,
            "SrcAlphaSaturate" => 9,
            "OneMinusSrcAlpha" => 10,
            _ => default,
        },
        other => props.number(other, default as f32) as u8,
    }
}

fn blend_op(v: &Value, props: &Props) -> u8 {
    match v {
        Value::Word(w) => match w.as_str() {
            "Sub" => 1,
            "RevSub" => 2,
            "Min" => 3,
            "Max" => 4,
            _ => 0,
        },
        other => props.number(other, 0.0) as u8,
    }
}

fn compare(v: &Value, props: &Props, default: u8) -> u8 {
    let n = match v {
        Value::Word(w) => match w.as_str() {
            "Never" => 1,
            "Less" => 2,
            "Equal" => 3,
            "LEqual" => 4,
            "Greater" => 5,
            "NotEqual" => 6,
            "GEqual" => 7,
            "Always" | "Off" | "Disabled" => 8,
            _ => default,
        },
        other => props.number(other, default as f32) as u8,
    };
    // 0 (Disabled) means no test.
    if n == 0 || n > 8 { 8 } else { n }
}

fn stencil_op(v: &Value, props: &Props, default: u8) -> u8 {
    match v {
        Value::Word(w) => match w.as_str() {
            "Keep" => 0,
            "Zero" => 1,
            "Replace" => 2,
            "IncrSat" => 3,
            "DecrSat" => 4,
            "Invert" => 5,
            "IncrWrap" => 6,
            "DecrWrap" => 7,
            _ => default,
        },
        other => (props.number(other, default as f32) as u8).min(7),
    }
}

fn render_state(props: &Props) -> RenderState {
    let s = &props.lab.states;
    let mut state = RenderState::default();
    if let Some(values) = &s.blend {
        let off = values.first() == Some(&Value::Word("Off".into())) || values.len() < 2;
        if !off {
            let src = blend_factor(&values[0], props, 1);
            let dst = blend_factor(&values[1], props, 0);
            let (src_a, dst_a) = if values.len() >= 4 {
                (
                    blend_factor(&values[2], props, 1),
                    blend_factor(&values[3], props, 0),
                )
            } else {
                (src, dst)
            };
            // Blend One Zero is no blending.
            if !(src == 1 && dst == 0 && src_a == 1 && dst_a == 0) {
                state.blend = Some([src, dst, src_a, dst_a]);
            }
        }
    }
    if let Some(ops) = &s.blend_op {
        let color = ops.first().map(|v| blend_op(v, props)).unwrap_or(0);
        let alpha = ops.get(1).map(|v| blend_op(v, props)).unwrap_or(color);
        state.blend_op = [color, alpha];
    }
    if let Some(v) = &s.zwrite {
        state.zwrite = match v {
            Value::Word(w) => w == "On",
            other => props.number(other, 1.0) != 0.0,
        };
    }
    if let Some(v) = &s.ztest {
        state.ztest = compare(v, props, 4);
    }
    if let Some(v) = &s.cull {
        state.cull = match v {
            Value::Word(w) => match w.as_str() {
                "Off" => 0,
                "Front" => 1,
                _ => 2,
            },
            other => (props.number(other, 2.0) as u8).min(2),
        };
    }
    if let Some(v) = &s.color_mask {
        state.color_mask = match v {
            Value::Num(n) => *n as u8 & 15,
            Value::Word(w) => w.chars().fold(0, |m, c| {
                m | match c {
                    'R' => 8,
                    'G' => 4,
                    'B' => 2,
                    'A' => 1,
                    _ => 0,
                }
            }),
            other => props.number(other, 15.0) as u8 & 15,
        };
    }
    if let Some(st) = &s.stencil {
        let comp = st
            .comp
            .as_ref()
            .map(|v| compare(v, props, 8))
            .unwrap_or(8);
        let op = |v: &Option<Value>| v.as_ref().map(|v| stencil_op(v, props, 0)).unwrap_or(0);
        let mask = |v: &Option<Value>| {
            v.as_ref()
                .map(|v| props.number(v, 255.0) as u32 as u8)
                .unwrap_or(255)
        };
        let stencil = StencilState {
            comp,
            pass: op(&st.pass),
            fail: op(&st.fail),
            zfail: op(&st.zfail),
            read_mask: mask(&st.read_mask),
            write_mask: mask(&st.write_mask),
        };
        // Always/Keep everywhere is no stencil test.
        if !(stencil.comp == 8 && stencil.pass == 0 && stencil.fail == 0 && stencil.zfail == 0) {
            state.stencil = Some(stencil);
        }
    }
    state
}

#[cfg(test)]
mod tests {
    use super::*;

    fn material(shader: &str, props: Vec<(&str, PropertyValue)>, keywords: &[&str]) -> Material {
        Material {
            shader: shader.into(),
            render_queue: 3000,
            keywords: keywords.iter().map(|s| s.to_string()).collect(),
            properties: props.into_iter().map(|(n, v)| (n.to_string(), v)).collect(),
        }
    }

    #[test]
    fn ui_default_states_follow_properties() {
        let mut r = Resolver::new(ShaderIndex::default());
        let m = r.resolve(&material(
            "UI/Default",
            vec![
                ("_Stencil", PropertyValue::Float(1.0)),
                ("_StencilComp", PropertyValue::Float(3.0)),
                ("_ColorMask", PropertyValue::Float(0.0)),
            ],
            &["UNITY_UI_ALPHACLIP"],
        ));
        assert_eq!(m.family, Family::Ui);
        assert_eq!(m.state.blend, Some([1, 10, 1, 10]));
        assert!(!m.state.zwrite);
        assert_eq!(m.state.cull, 0);
        assert_eq!(m.state.ztest, 4);
        assert_eq!(m.state.color_mask, 0);
        assert_eq!(m.stencil_ref, 1);
        assert_eq!(m.state.stencil.unwrap().comp, 3);
        assert!(m.uniform.flags[0] & keywords::ALPHACLIP != 0);
        assert!(m.uniform.flags[0] & keywords::PREMULTIPLY_OUTPUT != 0);
        // Default UI material: no stencil test.
        let plain = r.resolve(&material("UI/Default", vec![], &[]));
        assert_eq!(plain.state.stencil, None);
        assert_eq!(plain.state.color_mask, 15);
    }

    #[test]
    fn standard_reads_blend_properties() {
        let mut r = Resolver::new(ShaderIndex::default());
        let m = r.resolve(&material(
            "Standard",
            vec![
                ("_SrcBlend", PropertyValue::Float(5.0)),
                ("_DstBlend", PropertyValue::Float(10.0)),
                ("_ZWrite", PropertyValue::Float(0.0)),
            ],
            &["_ALPHABLEND_ON"],
        ));
        assert_eq!((m.family, m.variant), (Family::Lit, 2));
        assert_eq!(m.state.blend, Some([5, 10, 5, 10]));
        assert!(!m.state.zwrite);
        assert_eq!(m.state.cull, 2);
        let opaque = r.resolve(&material("Standard", vec![], &[]));
        assert_eq!(opaque.state.blend, None);
        assert!(opaque.state.zwrite);
    }

    #[test]
    fn particles_and_spine() {
        let mut r = Resolver::new(ShaderIndex::default());
        let m = r.resolve(&material("Legacy Shaders/Particles/Additive", vec![], &[]));
        assert_eq!(m.state.blend, Some([5, 1, 5, 1]));
        assert_eq!(m.state.color_mask, 14);
        assert_eq!(m.uniform.tint, [0.5; 4]);
        let s = r.resolve(&material("Spine/SkeletonGraphic Fill", vec![], &[]));
        assert_eq!((s.family, s.variant), (Family::Spine, 6));
        assert!(s.uniform.flags[0] & keywords::ENABLE_FILL != 0);
        assert_eq!(s.main.default, "black");
    }
}
