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
    /// Front faces' compare and operations.
    pub comp: u8,
    pub pass: u8,
    pub fail: u8,
    pub zfail: u8,
    pub read_mask: u8,
    pub write_mask: u8,
    /// Back faces' `[comp, pass, fail, zfail]`.
    pub back: [u8; 4],
}

impl StencilState {
    /// The faces swapped (a mirrored draw's front faces are its back faces as drawn).
    pub fn swapped(self) -> Self {
        StencilState {
            comp: self.back[0],
            pass: self.back[1],
            fail: self.back[2],
            zfail: self.back[3],
            back: [self.comp, self.pass, self.fail, self.zfail],
            ..self
        }
    }
}

/// A render target's own blending and write mask (`Blend N ...`, `ColorMask mask N`).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct TargetState {
    pub blend: Option<[u8; 4]>,
    pub color_mask: u8,
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
    /// Each render target's states where a pass gives any target its own (None: every target
    /// has `blend` and `color_mask`).
    pub targets: Option<[TargetState; crate::shaderlab::MAX_TARGETS]>,
    pub stencil: Option<StencilState>,
    /// `Offset factor, units`, as integers (factor is rounded; units are what Unity's are).
    pub offset: [i32; 2],
    /// `AlphaToMask On`: the alpha output is the sample coverage (under MSAA).
    pub alpha_to_mask: bool,
    /// `ZClip` (on by default); off clamps depth instead of clipping at the near and far planes.
    pub zclip: bool,
    /// Triangles drawn as their edges (`GL.wireframe`, set per draw by the camera's state).
    pub wireframe: bool,
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
            targets: None,
            stencil: None,
            offset: [0, 0],
            alpha_to_mask: false,
            zclip: true,
            wireframe: false,
        }
    }
}

/// One pass a material draws.
#[derive(Debug, Clone)]
pub struct ResolvedPass {
    pub program: unbound_shaders::Program,
    pub state: RenderState,
    pub stencil_ref: u32,
    /// A `GrabPass`: the texture it copies the target into (it draws nothing).
    pub grab: Option<String>,
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
        // Declared empty texture defaults use gray; a sampler without a ShaderLab property has
        // a distinct neutral placeholder (resources defaults.texels, builtinrp events probes).
        let default = match self.lab.property(name) {
            Some(PropertyDefault::Texture(t)) if !t.is_empty() => t.clone(),
            Some(PropertyDefault::Texture(_)) => "gray".to_string(),
            _ => "unbound-unassigned".to_string(),
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
        // A maximum LOD carries over to the fallbacks; a replacement tag does not fall back.
        let suffix = crate::shaderlab::Selection::parse(name).1.fallback_suffix();
        let mut current = name.to_string();
        for _ in 0..8 {
            let lab = self.index.get(&current)?.clone();
            if lab.has_subshader {
                return Some(lab);
            }
            current = format!("{}{}", lab.fallback?, suffix.as_deref()?);
        }
        None
    }

    /// The active render pipeline's tag; true when it changed (materials must be resolved again).
    pub fn set_pipeline(&mut self, tag: &str) -> bool {
        self.index.set_pipeline(tag)
    }

    /// The SubShader a shader draws with under the active pipeline (after its fallbacks; the
    /// error shader's when none), as JSON: `{ shader, found, tags, passes: [{ name, tags, lighting }] }`.
    pub fn describe(&mut self, name: &str) -> String {
        let (found, lab) = match self.shader(name) {
            Some(lab) => (true, lab),
            None => (false, self.error_shader()),
        };
        let passes: Vec<serde_json::Value> = lab
            .passes
            .iter()
            .map(|p| {
                serde_json::json!({
                    "name": p.name,
                    "tags": p.states.tags,
                    "lighting": p.states.fixed.lighting.unwrap_or(false),
                })
            })
            .collect();
        serde_json::json!({
            "shader": lab.name,
            "found": found,
            "tags": lab.tags,
            "passes": passes,
            "dependencies": lab.dependencies.iter().cloned().collect::<std::collections::HashMap<_, _>>(),
            "gamma": lab.gamma,
            "colors": lab.colors,
            "fallback": lab.fallback,
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
                let lod = crate::shaderlab::Selection::parse(&material.shader)
                    .1
                    .max_lod;
                self.log_once(key, || {
                    if let (true, Some(lod)) = (known, lod) {
                        format!(
                            "shader `{}` has no SubShader within LOD {lod} (nor does its fallback): its renderers draw nothing",
                            crate::shaderlab::Selection::parse(&material.shader).0
                        )
                    } else if known {
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
        self.resolve_lab(material, lab)
    }

    /// Resolve all source SubShaders and their fallback chain for global preparation.
    /// A visited set also terminates malformed cyclic fallback chains.
    pub fn warmup_sources(&mut self, material: &Material) -> Result<Vec<ResolvedMaterial>, String> {
        let mut current = material.shader.clone();
        let mut seen = HashSet::new();
        let mut sources = Vec::new();
        while seen.insert(current.clone()) {
            let (labs, fallback) = self.index.warmup_sources(&current).ok_or_else(|| {
                format!("shader `{current}` has no readable source for global warm-up")
            })?;
            sources.extend(labs.into_iter().map(|lab| self.resolve_lab(material, lab)));
            let Some(fallback) = fallback else {
                break;
            };
            current = fallback;
        }
        Ok(sources)
    }

    fn resolve_lab(&mut self, material: &Material, lab: ShaderLab) -> ResolvedMaterial {
        let props = Props {
            material,
            lab: &lab,
        };
        let all: Vec<Result<ResolvedPass, String>> = lab
            .passes
            .iter()
            .map(|pass| {
                if let Some(name) = &pass.grab {
                    return Ok(ResolvedPass {
                        program: unbound_shaders::Program {
                            file: lab.path.clone(),
                            source: String::new(),
                            cg: false,
                            light_mode: None,
                        },
                        state: RenderState::default(),
                        stencil_ref: 0,
                        grab: Some(if name.is_empty() {
                            "_GrabTexture".into()
                        } else {
                            name.clone()
                        }),
                    });
                }
                let program = self.pass_program(&lab, pass)?;
                let (state, stencil_ref) = render_state(&props, pass);
                Ok(ResolvedPass {
                    program,
                    state,
                    stencil_ref,
                    grab: None,
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
        Value::Word(w) => match w.to_ascii_lowercase().as_str() {
            "zero" => 0,
            "one" => 1,
            "dstcolor" => 2,
            "srccolor" => 3,
            "oneminusdstcolor" => 4,
            "srcalpha" => 5,
            "oneminussrccolor" => 6,
            "dstalpha" => 7,
            "oneminusdstalpha" => 8,
            "srcalphasaturate" => 9,
            "oneminussrcalpha" => 10,
            _ => default,
        },
        other => props.number(other, default as f32) as u8,
    }
}

fn blend_op(v: &Value, props: &Props) -> u8 {
    match v {
        Value::Word(w) => match w.to_ascii_lowercase().as_str() {
            "sub" => 1,
            "revsub" => 2,
            "min" => 3,
            "max" => 4,
            _ => 0,
        },
        other => props.number(other, 0.0) as u8,
    }
}

fn compare(v: &Value, props: &Props, default: u8) -> u8 {
    let n = match v {
        Value::Word(w) => match w.to_ascii_lowercase().as_str() {
            "never" => 1,
            "less" => 2,
            "equal" => 3,
            "lequal" => 4,
            "greater" => 5,
            "notequal" => 6,
            "gequal" => 7,
            "always" | "off" | "disabled" => 8,
            _ => default,
        },
        other => props.number(other, default as f32) as u8,
    };
    // 0 (Disabled) means no test.
    if n == 0 || n > 8 { 8 } else { n }
}

fn stencil_op(v: &Value, props: &Props, default: u8) -> u8 {
    match v {
        Value::Word(w) => match w.to_ascii_lowercase().as_str() {
            "keep" => 0,
            "zero" => 1,
            "replace" => 2,
            "incrsat" => 3,
            "decrsat" => 4,
            "invert" => 5,
            "incrwrap" => 6,
            "decrwrap" => 7,
            _ => default,
        },
        other => (props.number(other, default as f32) as u8).min(7),
    }
}

/// `Blend src dst [, src_alpha dst_alpha]` (None: Off, or One Zero, which is no blending).
fn blend_state(values: &[Value], props: &Props) -> Option<[u8; 4]> {
    let off = matches!(values.first(), Some(Value::Word(w)) if w.eq_ignore_ascii_case("off"))
        || values.len() < 2;
    if off {
        return None;
    }
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
    (!(src == 1 && dst == 0 && src_a == 1 && dst_a == 0)).then_some([src, dst, src_a, dst_a])
}

/// `ColorMask`: RGBA bits 8, 4, 2, 1 (0: nothing written).
fn color_mask(v: &Value, props: &Props) -> u8 {
    match v {
        Value::Num(n) => *n as u8 & 15,
        Value::Word(w) => w.chars().fold(0, |m, c| {
            m | match c.to_ascii_uppercase() {
                'R' => 8,
                'G' => 4,
                'B' => 2,
                'A' => 1,
                _ => 0,
            }
        }),
        other => props.number(other, 15.0) as u8 & 15,
    }
}

/// A pass's render state and stencil reference under a material's properties.
fn render_state(props: &Props, pass: &PassInfo) -> (RenderState, u32) {
    let s = &pass.states;
    let mut state = RenderState::default();
    if let Some(values) = &s.blend {
        state.blend = blend_state(values, props);
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
            // ShaderLab words are case-insensitive (`Cull off`).
            Value::Word(w) => match w.to_ascii_lowercase().as_str() {
                "off" => 0,
                "front" => 1,
                _ => 2,
            },
            other => (props.number(other, 2.0) as u8).min(2),
        };
    }
    if let Some(v) = &s.color_mask {
        state.color_mask = color_mask(v, props);
    }
    // Targets with their own states keep the pass's for the rest; a target's own state wins
    // over the unindexed one whatever their order (unmeasured).
    if s.target_blends.iter().any(Option::is_some)
        || s.target_color_masks.iter().any(Option::is_some)
    {
        let mut targets = [TargetState {
            blend: state.blend,
            color_mask: state.color_mask,
        }; crate::shaderlab::MAX_TARGETS];
        for (target, (blend, mask)) in targets
            .iter_mut()
            .zip(s.target_blends.iter().zip(&s.target_color_masks))
        {
            if let Some(values) = blend {
                target.blend = blend_state(values, props);
            }
            if let Some(v) = mask {
                target.color_mask = color_mask(v, props);
            }
        }
        state.targets = Some(targets);
    }
    if let Some([factor, units]) = &s.offset {
        state.offset = [
            props.number(factor, 0.0).round() as i32,
            props.number(units, 0.0).round() as i32,
        ];
    }
    let switch = |v: &Value, default: bool| match v {
        Value::Word(w) => {
            w.eq_ignore_ascii_case("on") || w.eq_ignore_ascii_case("true") || w == "1"
        }
        other => props.number(other, if default { 1.0 } else { 0.0 }) != 0.0,
    };
    if let Some(v) = &s.alpha_to_mask {
        state.alpha_to_mask = switch(v, false);
    }
    if let Some(v) = &s.zclip {
        state.zclip = switch(v, true);
    }
    let mut stencil_ref = 0;
    if let Some(st) = &s.stencil {
        let mask = |v: &Option<Value>| {
            v.as_ref()
                .map(|v| props.number(v, 255.0) as u32 as u8)
                .unwrap_or(255)
        };
        // A face's own setting, else both faces' (Always, Keep by default); the face's own wins
        // whatever their order (unmeasured).
        let both = [&st.comp, &st.pass, &st.fail, &st.zfail];
        let face = |own: &[Option<Value>; 4]| -> [u8; 4] {
            std::array::from_fn(|i| match own[i].as_ref().or(both[i].as_ref()) {
                Some(v) if i == 0 => compare(v, props, 8),
                Some(v) => stencil_op(v, props, 0),
                None if i == 0 => 8,
                None => 0,
            })
        };
        let [comp, pass, fail, zfail] = face(&st.front);
        let stencil = StencilState {
            comp,
            pass,
            fail,
            zfail,
            read_mask: mask(&st.read_mask),
            write_mask: mask(&st.write_mask),
            back: face(&st.back),
        };
        // Always/Keep everywhere, on both faces, is no stencil test.
        let idle = |f: [u8; 4]| f == [8, 0, 0, 0];
        if !(idle([comp, pass, fail, zfail]) && idle(stencil.back)) {
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

    #[test]
    fn global_warmup_resolves_all_subshaders_use_passes_and_fallbacks() {
        let first = r#"Shader "Warm/First" {
            SubShader { LOD 300 Pass { Name "HIGH" ColorMask R } }
            SubShader { LOD 100 UsePass "Warm/Source/SOURCE" }
            Fallback "Warm/Fallback"
        }"#;
        let source = r#"Shader "Warm/Source" { SubShader { Pass { Name "SOURCE" ColorMask G
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 p : POSITION) : SV_POSITION { return p; }
            float4 frag() : SV_Target { return 1; }
            ENDHLSL
        } } }"#;
        let fallback = r#"Shader "Warm/Fallback" {
            SubShader { Tags { "RenderPipeline"="Other" } Pass { ColorMask B } }
            Fallback "Warm/First"
        }"#;
        let unused = r#"Shader "Warm/Unused" { SubShader { Pass { } } }"#;
        let mut resolver = Resolver::new(index(&[first, source, fallback, unused]));
        let input = material("Warm/First", vec![]);
        let ordinary = resolver.resolve(&input);
        assert_eq!(ordinary.all.len(), 1);
        assert_eq!(ordinary.all[0].as_ref().unwrap().state.color_mask, 8);
        let sources = resolver.warmup_sources(&input).unwrap();
        assert_eq!(
            sources.len(),
            3,
            "a cyclic fallback visits each source once"
        );
        let masks: Vec<_> = sources
            .iter()
            .map(|s| s.all[0].as_ref().unwrap().state.color_mask)
            .collect();
        assert_eq!(masks, vec![8, 4, 2]);
        assert!(
            resolver
                .warmup_sources(&material("Warm/Absent", vec![]))
                .is_err()
        );
    }

    fn material(shader: &str, props: Vec<(&str, PropertyValue)>) -> Material {
        Material {
            shader: shader.into(),
            render_queue: 3000,
            keywords: Vec::new(),
            properties: props.into_iter().map(|(n, v)| (n.to_string(), v)).collect(),
            index: Default::default(),
        }
    }

    /// The shaders in a scan directory of their own (tests run in parallel).
    fn index(shaders: &[&str]) -> ShaderIndex {
        static NEXT: std::sync::atomic::AtomicUsize = std::sync::atomic::AtomicUsize::new(0);
        let dir = std::env::temp_dir().join(format!(
            "unbound-materials-{}-{}",
            std::process::id(),
            NEXT.fetch_add(1, std::sync::atomic::Ordering::Relaxed)
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
        assert_eq!(
            plain.texture("_Other"),
            (0, "unbound-unassigned".to_string())
        );
    }

    #[test]
    fn state_words_ignore_case() {
        // As Unity's Hidden/Internal-Halo writes them.
        let halo = r#"Shader "Probe/LowerCase" {
SubShader {
    ZWrite off Cull off // the halo meshes flip handedness
    Blend oneminusdstcolor one
    ColorMask rgb
    ZTest lequal
    Pass {
        CGPROGRAM
        #pragma vertex vert
        ENDCG
    }
}}"#;
        let other = r#"Shader "Probe/Other" { SubShader { Pass { } } }"#;
        let mut r = Resolver::new(index(&[halo, UI, other]));
        let m = r.resolve(&material("Probe/LowerCase", vec![]));
        let p = &m.passes[0];
        assert_eq!(
            (p.state.cull, p.state.ztest, p.state.color_mask),
            (0, 4, 14)
        );
        assert!(!p.state.zwrite);
        assert_eq!(p.state.blend, Some([4, 1, 4, 1]));
    }

    /// Indexed `ColorMask`/`Blend` lines are their render target's own (as the Decal Shader
    /// Graph's DBuffer passes write them); the unindexed ones stay every other target's.
    #[test]
    fn render_targets_take_their_own_masks_and_blends() {
        let decal = r#"Shader "Probe/Targets" {
SubShader {
    Pass {
        Blend SrcAlpha OneMinusSrcAlpha
        Blend 1 One One
        ColorMask RGBA
        ColorMask 0 1
        ColorMask RGB 2
        CGPROGRAM
        #pragma vertex vert
        ENDCG
    }
}}"#;
        let plain = r#"Shader "Probe/Plain" { SubShader { Pass { ColorMask 0 } } }"#;
        let other = r#"Shader "Probe/Other" { SubShader { Pass { } } }"#;
        let mut r = Resolver::new(index(&[decal, plain, other]));
        let p = &r.resolve(&material("Probe/Targets", vec![])).passes[0];
        assert_eq!(
            (p.state.blend, p.state.color_mask),
            (Some([5, 10, 5, 10]), 15)
        );
        let targets = p.state.targets.expect("targets of their own");
        assert_eq!(
            (targets[0].blend, targets[0].color_mask),
            (Some([5, 10, 5, 10]), 15)
        );
        assert_eq!(
            (targets[1].blend, targets[1].color_mask),
            (Some([1, 1, 1, 1]), 0)
        );
        assert_eq!(
            (targets[2].blend, targets[2].color_mask),
            (Some([5, 10, 5, 10]), 14)
        );
        assert_eq!(targets[3].color_mask, 15);
        // Without an index the mask is every target's.
        let p = &r.resolve(&material("Probe/Plain", vec![])).passes[0];
        assert_eq!((p.state.color_mask, p.state.targets), (0, None));
    }

    /// `Comp`/`Pass` are both faces'; `CompBack`/`PassBack` (as URP's deferred SimpleLit light
    /// passes write them) a back face's own, the front then testing nothing; a mirrored draw's
    /// faces swap.
    #[test]
    fn stencil_faces_take_their_own_settings() {
        let faces = r#"Shader "Probe/Faces" {
SubShader {
    Pass { Stencil { Ref 80 ReadMask 112 CompBack Equal PassBack Zero } }
    Pass { Stencil { Ref 48 Comp Equal Pass Zero CompFront Always } }
}}"#;
        let mut r = Resolver::new(index(&[faces]));
        let m = r.resolve(&material("Probe/Faces", vec![]));
        let back_only = m.passes[0].state.stencil.expect("a back-face test");
        assert_eq!(
            [
                back_only.comp,
                back_only.pass,
                back_only.fail,
                back_only.zfail
            ],
            [8, 0, 0, 0]
        );
        assert_eq!(back_only.back, [3, 1, 0, 0]);
        assert_eq!((back_only.read_mask, m.passes[0].stencil_ref), (112, 80));
        let both = m.passes[1].state.stencil.expect("a test");
        assert_eq!([both.comp, both.pass], [8, 1]);
        assert_eq!(both.back, [3, 1, 0, 0]);
        let mirrored = back_only.swapped();
        assert_eq!([mirrored.comp, mirrored.pass], [3, 1]);
        assert_eq!(mirrored.back, [8, 0, 0, 0]);
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
