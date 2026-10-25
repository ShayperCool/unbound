//! How a material draws: the forward passes of its shader (the project's, else Unity's built-in
//! one, compiled from Unity's sources), each with its program and its render state under the
//! material's properties. A shader that is missing, or has no SubShader the built-in render
//! pipeline runs and no fallback that has one, draws with Unity's error shader.
use crate::shaderlab::{PassInfo, PropertyDefault, ShaderIndex, ShaderLab, Value};
use std::{collections::HashSet, sync::Arc};
use unbound_runtime::render_protocol::owned::{Material, PropertyValue};

/// Unity's error shader, used when the built-in sources (which hold the real one) are missing.
const ERROR_SHADER: &str = r#"Shader "Hidden/InternalErrorShader" {
SubShader {
    Pass {
        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #include "UnityCG.cginc"
        float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
        fixed4 frag() : SV_Target { return fixed4(1, 0, 1, 1); }
        ENDCG
    }
}}"#;

pub const ERROR_SHADER_NAME: &str = "Hidden/InternalErrorShader";

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
    /// The depth test is `unity_GUIZTestMode`, which Unity sets per canvas: Always for
    /// screen-space overlay canvases, LessEqual otherwise.
    pub gui_ztest: bool,
    pub cull: u8,
    pub color_mask: u8,
    pub stencil: Option<StencilState>,
    /// `Offset factor, units`, as integers (factor is rounded; units are what Unity's are).
    pub offset: [i32; 2],
}

impl Default for RenderState {
    fn default() -> Self {
        RenderState {
            blend: None,
            blend_op: [0, 0],
            zwrite: true,
            ztest: 4,
            gui_ztest: false,
            cull: 2,
            color_mask: 15,
            stencil: None,
            offset: [0, 0],
        }
    }
}

/// One pass a material draws.
#[derive(Debug, Clone)]
pub struct ResolvedPass {
    pub program: unbound_shaders::Program,
    pub state: RenderState,
    pub stencil_ref: u32,
}

#[derive(Debug, Clone)]
pub struct ResolvedMaterial {
    /// The shader drawn (the error shader's name when it stands in).
    pub shader: String,
    pub lab: Arc<ShaderLab>,
    pub material: Material,
    /// The passes the built-in forward renderer draws.
    pub passes: Vec<ResolvedPass>,
    /// Every pass of the SubShader by index (`Material.SetPass`, `Graphics.Blit`, command
    /// buffers): `Err` says why one cannot draw.
    pub all: Vec<Result<ResolvedPass, String>>,
}

impl ResolvedMaterial {
    /// A texture property's texture handle (0: none) and default texture name.
    pub fn texture(&self, name: &str) -> (u64, String) {
        let handle = match self.material.get(name) {
            Some(PropertyValue::Texture { handle, .. }) => *handle,
            _ => 0,
        };
        // Unity: an empty or unknown default is gray.
        let default = match self.lab.property(name) {
            Some(PropertyDefault::Texture(t)) if !t.is_empty() => t.clone(),
            _ => "gray".to_string(),
        };
        (handle, default)
    }
}

/// Resolves materials; logs once per shader that cannot draw as written.
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

    fn log_once(&mut self, key: String, message: impl FnOnce() -> String) {
        if self.logged.insert(key) {
            tracing::warn!("{}", message());
        }
    }

    /// The shader that draws `name`: itself, else the first in its fallback chain with a
    /// SubShader the built-in render pipeline runs.
    fn shader(&mut self, name: &str) -> Option<ShaderLab> {
        let mut current = name.to_string();
        for _ in 0..8 {
            let lab = self.index.get(&current)?.clone();
            if lab.has_subshader {
                return Some(lab);
            }
            current = lab.fallback?;
        }
        None
    }

    /// The active render pipeline's tag; true when it changed (materials must be resolved again).
    pub fn set_pipeline(&mut self, tag: &str) -> bool {
        self.index.set_pipeline(tag)
    }

    /// The SubShader a shader draws with under the active pipeline (after its fallbacks; the
    /// error shader's when none), as JSON: `{ shader, found, tags, passes: [{ name, tags }] }`.
    pub fn describe(&mut self, name: &str) -> String {
        let (found, lab) = match self.shader(name) {
            Some(lab) => (true, lab),
            None => (false, self.error_shader()),
        };
        let passes: Vec<serde_json::Value> = lab
            .passes
            .iter()
            .map(|p| serde_json::json!({ "name": p.name, "tags": p.states.tags }))
            .collect();
        serde_json::json!({
            "shader": lab.name,
            "found": found,
            "tags": lab.tags,
            "passes": passes,
        })
        .to_string()
    }

    /// Unity's error shader: the built-in one, else the restatement above.
    pub fn error_shader(&mut self) -> ShaderLab {
        self.index
            .get(ERROR_SHADER_NAME)
            .cloned()
            .unwrap_or_else(|| ShaderLab::parse(ERROR_SHADER).expect("error shader parses"))
    }

    pub fn resolve(&mut self, material: &Material) -> ResolvedMaterial {
        let found = self.shader(&material.shader);
        let pipeline = self.index.pipeline().to_string();
        let lab = match found {
            Some(lab) => lab,
            None => {
                let known = self.index.get(&material.shader).is_some();
                let key = format!("{}@{pipeline}", material.shader);
                self.log_once(key, || {
                    if known {
                        let pipeline = match pipeline.as_str() {
                            "" => "the built-in render pipeline".to_string(),
                            tag => format!("the `{tag}` render pipeline"),
                        };
                        format!(
                            "shader `{}` has no SubShader {pipeline} runs (nor does its fallback): drawn with the error shader",
                            material.shader
                        )
                    } else {
                        format!(
                            "shader `{}` is unknown (not in the project nor built in): drawn with the error shader",
                            material.shader
                        )
                    }
                });
                self.error_shader()
            }
        };
        let props = Props {
            material,
            lab: &lab,
        };
        let all: Vec<Result<ResolvedPass, String>> = lab
            .passes
            .iter()
            .map(|pass| {
                let program = self.pass_program(&lab, pass)?;
                let (state, stencil_ref) = render_state(&props, pass);
                Ok(ResolvedPass {
                    program,
                    state,
                    stencil_ref,
                })
            })
            .collect();
        let mut passes = Vec::new();
        for index in lab.forward_passes() {
            let pass = &lab.passes[index];
            match &all[index] {
                Ok(resolved) => passes.push(resolved.clone()),
                Err(reason) => self.log_once(format!("{}#{index}", lab.name), || {
                    format!(
                        "shader `{}`: pass {index}{} is not drawn: {reason}",
                        lab.name,
                        if pass.name.is_empty() {
                            String::new()
                        } else {
                            format!(" (`{}`)", pass.name)
                        }
                    )
                }),
            }
        }
        ResolvedMaterial {
            shader: lab.name.clone(),
            lab: Arc::new(lab),
            material: material.clone(),
            passes,
            all,
        }
    }

    /// The program a pass draws with: its own, or its fixed-function setup's.
    fn pass_program(
        &self,
        lab: &ShaderLab,
        pass: &PassInfo,
    ) -> Result<unbound_shaders::Program, String> {
        if pass.grab {
            return Err("GrabPass is not supported".into());
        }
        if pass.glsl {
            return Err("GLSLPROGRAM passes are not supported".into());
        }
        if pass.use_pass.is_some() && pass.program.is_none() && pass.states.fixed.stages.is_empty()
        {
            return Err(format!(
                "UsePass \"{}\" names no pass Unbound found",
                pass.use_pass.as_deref().unwrap_or_default()
            ));
        }
        let light_mode = pass.states.tags.get("LightMode").cloned();
        Ok(match &pass.program {
            Some(p) => unbound_shaders::Program {
                file: p.file.clone(),
                source: p.code.clone(),
                cg: p.cg,
                light_mode: if p.surface {
                    light_mode.or_else(|| Some("ForwardBase".into()))
                } else {
                    light_mode
                },
            },
            None => unbound_shaders::Program {
                file: lab.path.clone(),
                source: unbound_shaders::fixed::generate(&pass.states.fixed_function())
                    .map_err(|e| format!("{e:#}"))?,
                cg: true,
                light_mode,
            },
        })
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

    /// A state value: a literal (resolved by the caller) or a property.
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

/// A pass's render state and stencil reference under a material's properties.
fn render_state(props: &Props, pass: &PassInfo) -> (RenderState, u32) {
    let s = &pass.states;
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
            Value::Word(w) => w.eq_ignore_ascii_case("on"),
            other => props.number(other, 1.0) != 0.0,
        };
    }
    if let Some(v) = &s.ztest {
        state.ztest = compare(v, props, 4);
        state.gui_ztest = matches!(v, Value::Prop(p) if p == "unity_GUIZTestMode");
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
    if let Some([factor, units]) = &s.offset {
        state.offset = [
            props.number(factor, 0.0).round() as i32,
            props.number(units, 0.0).round() as i32,
        ];
    }
    let mut stencil_ref = 0;
    if let Some(st) = &s.stencil {
        let comp = st.comp.as_ref().map(|v| compare(v, props, 8)).unwrap_or(8);
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
        stencil_ref = st
            .reference
            .as_ref()
            .map(|v| props.number(v, 0.0) as u32 & 0xff)
            .unwrap_or(0);
    }
    (state, stencil_ref)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn material(shader: &str, props: Vec<(&str, PropertyValue)>) -> Material {
        Material {
            shader: shader.into(),
            render_queue: 3000,
            keywords: Vec::new(),
            properties: props.into_iter().map(|(n, v)| (n.to_string(), v)).collect(),
        }
    }

    fn index(shaders: &[&str]) -> ShaderIndex {
        let dir = std::env::temp_dir().join(format!(
            "unbound-materials-{}-{}",
            std::process::id(),
            shaders.len()
        ));
        let _ = std::fs::create_dir_all(&dir);
        for (i, text) in shaders.iter().enumerate() {
            std::fs::write(dir.join(format!("s{i}.shader")), text).unwrap();
        }
        ShaderIndex::scan(&[dir])
    }

    const UI: &str = r#"Shader "UI/Default" {
Properties {
    _MainTex ("Sprite Texture", 2D) = "white" {}
    _StencilComp ("Stencil Comparison", Float) = 8
    _Stencil ("Stencil ID", Float) = 0
    _StencilOp ("Stencil Operation", Float) = 0
    _ColorMask ("Color Mask", Float) = 15
}
SubShader {
    Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] }
    Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
    Blend One OneMinusSrcAlpha
    ColorMask [_ColorMask]
    Pass {
        CGPROGRAM
        #pragma vertex vert
        ENDCG
    }
}}"#;

    #[test]
    fn states_follow_properties() {
        let mut r = Resolver::new(index(&[UI]));
        let m = r.resolve(&material(
            "UI/Default",
            vec![
                ("_Stencil", PropertyValue::Float(1.0)),
                ("_StencilComp", PropertyValue::Float(3.0)),
                ("_ColorMask", PropertyValue::Float(0.0)),
            ],
        ));
        assert_eq!(m.passes.len(), 1);
        let p = &m.passes[0];
        assert_eq!(p.state.blend, Some([1, 10, 1, 10]));
        assert!(!p.state.zwrite);
        assert_eq!((p.state.cull, p.state.ztest, p.state.color_mask), (0, 4, 0));
        assert_eq!(p.stencil_ref, 1);
        assert_eq!(p.state.stencil.unwrap().comp, 3);
        assert!(p.program.source.contains("#pragma vertex vert"));
        // Default material: no stencil test.
        let plain = r.resolve(&material("UI/Default", vec![]));
        assert_eq!(plain.passes[0].state.stencil, None);
        assert_eq!(plain.texture("_MainTex"), (0, "white".to_string()));
        assert_eq!(plain.texture("_Other"), (0, "gray".to_string()));
    }

    #[test]
    fn fixed_function_fallbacks_and_errors() {
        let ff = r#"Shader "Mobile/Particles/Additive" {
Properties { _MainTex ("Particle Texture", 2D) = "white" {} }
Category { Blend SrcAlpha One Cull Off Lighting Off ZWrite Off
    BindChannels { Bind "Color", color Bind "Vertex", vertex Bind "TexCoord", texcoord }
    SubShader { Pass { SetTexture [_MainTex] { combine texture * primary } } }
}}"#;
        let srp_only = r#"Shader "SRP" {
SubShader { Tags { "RenderPipeline"="UniversalPipeline" } Pass { } }
Fallback "Mobile/Particles/Additive"
}"#;
        let mut r = Resolver::new(index(&[ff, srp_only]));
        let m = r.resolve(&material("Mobile/Particles/Additive", vec![]));
        assert_eq!(m.passes.len(), 1);
        assert_eq!(m.passes[0].state.blend, Some([5, 1, 5, 1]));
        assert!(m.passes[0].program.source.contains("tex2D(_MainTex"));
        // No SubShader for the built-in pipeline: the fallback draws.
        let fallback = r.resolve(&material("SRP", vec![]));
        assert_eq!(fallback.shader, "Mobile/Particles/Additive");
        // Unknown: the error shader.
        let missing = r.resolve(&material("Nope/Nothing", vec![]));
        assert_eq!(missing.shader, ERROR_SHADER_NAME);
        assert_eq!(missing.passes.len(), 1);
    }
}
