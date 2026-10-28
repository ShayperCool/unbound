//! Fixed-function passes (`SetTexture` combiners, `Color`, `Lighting`, `Material`, `AlphaTest`)
//! written as the vertex/fragment program they stand for, the way Unity emulates them on
//! programmable hardware, so they compile like any other program.
use anyhow::{Result, bail};
use std::fmt::Write;

/// A color: a literal or a material property (`[_Color]`).
#[derive(Debug, Clone, PartialEq)]
pub enum ColorValue {
    Literal([f32; 4]),
    Property(String),
}

/// A `SetTexture [_Tex] { combine ... constantColor ... }` stage.
#[derive(Debug, Clone, PartialEq)]
pub struct TextureStage {
    pub texture: String,
    /// The `combine` expression as written (`texture * primary double, texture`).
    pub combine: String,
    pub constant: Option<ColorValue>,
}

/// `Material { ... }` (for `Lighting On`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct LightingMaterial {
    pub diffuse: Option<ColorValue>,
    pub ambient: Option<ColorValue>,
    pub specular: Option<ColorValue>,
    pub emission: Option<ColorValue>,
    pub shininess: Option<ColorValue>,
}

#[derive(Debug, Clone, Default, PartialEq)]
pub struct FixedFunction {
    /// `Lighting On`.
    pub lighting: bool,
    /// A `"LightMode"="Vertex"` pass: lit by up to eight lights per vertex
    /// (`unity_LightPosition`, ... in view space) instead of the main light.
    pub vertex_lights: bool,
    pub material: LightingMaterial,
    /// `SeparateSpecular On`.
    pub separate_specular: bool,
    /// `ColorMaterial AmbientAndDiffuse` / `Emission`: vertex colors replace those.
    pub color_material: Option<String>,
    /// `Color (r,g,b,a)` / `Color [_Prop]`.
    pub color: Option<ColorValue>,
    /// `AlphaTest Greater [_Cutoff]`: (comparison, reference).
    pub alpha_test: Option<(String, ColorValue)>,
    pub stages: Vec<TextureStage>,
    /// `BindChannels { Bind "texcoord1", texcoord }`: (source, target).
    pub bindings: Vec<(String, String)>,
}

/// The program's source (CG: `HLSLSupport.cginc`, `UnityShaderVariables.cginc` and
/// `UnityShaderUtilities.cginc` included).
pub fn generate(ff: &FixedFunction) -> Result<String> {
    let mut uniforms: Vec<String> = Vec::new();
    let mut declare = |decl: String| {
        if !uniforms.contains(&decl) {
            uniforms.push(decl);
        }
    };
    let color = |c: &ColorValue, declare: &mut dyn FnMut(String)| -> String {
        match c {
            ColorValue::Literal([r, g, b, a]) => format!("fixed4({r:?}, {g:?}, {b:?}, {a:?})"),
            ColorValue::Property(p) => {
                declare(format!("fixed4 {p};"));
                p.clone()
            }
        }
    };

    // Which mesh channel feeds each texture stage's coordinates (texcoord0 by default).
    let stage_uv = |stage: usize| -> u32 {
        for (source, target) in &ff.bindings {
            let target = target.to_ascii_lowercase();
            let index = match target.as_str() {
                "texcoord" => Some(0),
                t => t
                    .strip_prefix("texcoord")
                    .and_then(|n| n.parse::<usize>().ok()),
            };
            if index == Some(stage) {
                let source = source.to_ascii_lowercase();
                return match source.as_str() {
                    "texcoord1" => 1,
                    _ => 0,
                };
            }
        }
        0
    };
    let vertex_color = ff.color.is_none()
        || ff
            .bindings
            .iter()
            .any(|(s, _)| s.eq_ignore_ascii_case("color"));

    let mut vertex = String::new();
    let mut fragment = String::new();
    // The primary color: lit, or the Color command, or the vertex color.
    if ff.lighting {
        let m = &ff.material;
        let pick = |c: &Option<ColorValue>, default: [f32; 4]| {
            c.clone().unwrap_or(ColorValue::Literal(default))
        };
        let mut diffuse = color(&pick(&m.diffuse, [1.0; 4]), &mut declare);
        let mut ambient = color(&pick(&m.ambient, [1.0; 4]), &mut declare);
        let mut emission = color(&pick(&m.emission, [0.0, 0.0, 0.0, 0.0]), &mut declare);
        let specular = color(&pick(&m.specular, [0.0, 0.0, 0.0, 0.0]), &mut declare);
        let shininess = color(&pick(&m.shininess, [0.7; 4]), &mut declare);
        match ff.color_material.as_deref().map(str::to_ascii_lowercase) {
            Some(c) if c == "emission" => emission = "v.color".into(),
            Some(_) => {
                diffuse = "v.color".into();
                ambient = "v.color".into();
            }
            None => {}
        }
        if ff.vertex_lights {
            // Unity's emulation of fixed-function lighting: each light attenuated by distance
            // (1 / (1 + k d²), nothing past its range) and spot cone, at half the color it is
            // passed with; diffuse clamped per light.
            writeln!(
                vertex,
                "    float3 n = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, v.normal));
    float3 viewPos = UnityObjectToViewPos(v.vertex);
    float3 viewDir = -normalize(viewPos);
    float shininessPower = {shininess}.x * 128.0;
    float3 diffuseLight = 0.0;
    float3 specularLight = 0.0;
    for (int il = 0; il < 8; ++il) {{
        float3 dirToLight = unity_LightPosition[il].xyz - viewPos * unity_LightPosition[il].w;
        float distSqr = dot(dirToLight, dirToLight);
        float att = 1.0 / (1.0 + unity_LightAtten[il].z * distSqr);
        if (unity_LightPosition[il].w != 0.0 && distSqr > unity_LightAtten[il].w) att = 0.0;
        dirToLight *= rsqrt(max(distSqr, 0.000001));
        float rho = max(dot(dirToLight, unity_SpotDirection[il].xyz), 0.0);
        att *= saturate((rho - unity_LightAtten[il].x) * unity_LightAtten[il].y);
        att *= 0.5;
        float ndl = max(dot(n, dirToLight), 0.0);
        diffuseLight += min(ndl * {diffuse}.rgb * unity_LightColor[il].rgb * att, 1.0);
        if (ndl > 0.0) {{
            float3 h = normalize(dirToLight + viewDir);
            specularLight += att * saturate(pow(max(dot(n, h), 0.0), shininessPower)) * unity_LightColor[il].rgb;
        }}
    }}
    fixed4 lit;
    lit.rgb = glstate_lightmodel_ambient.rgb * 2.0 * {ambient}.rgb + {emission}.rgb + diffuseLight;
    lit.a = {diffuse}.a;
    float3 specularColor = {specular}.rgb * specularLight;"
            )?;
        } else {
        writeln!(
            vertex,
            "    float3 n = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, v.normal));
    float3 viewPos = UnityObjectToViewPos(v.vertex);
    float3 l = normalize(mul((float3x3)UNITY_MATRIX_V, _WorldSpaceLightPos0.xyz));
    float ndl = max(0.0, dot(n, l));
    float3 h = normalize(l - normalize(viewPos));
    float spec = pow(max(0.0, dot(n, h)), {shininess}.x * 128.0) * (ndl > 0.0);
    fixed4 lit;
    lit.rgb = glstate_lightmodel_ambient.rgb * 2.0 * {ambient}.rgb + {emission}.rgb + {diffuse}.rgb * _LightColor0.rgb * ndl;
    lit.a = {diffuse}.a;
    float3 specularColor = {specular}.rgb * _LightColor0.rgb * spec;"
        )?;
        }
        if ff.separate_specular {
            vertex.push_str("    o.color = saturate(lit);\n    o.specular = specularColor;\n");
        } else {
            vertex.push_str(
                "    o.color = saturate(fixed4(lit.rgb + specularColor, lit.a));\n    o.specular = 0;\n",
            );
        }
    } else if let (Some(c), false) = (&ff.color, vertex_color) {
        let c = color(c, &mut declare);
        writeln!(vertex, "    o.color = {c};\n    o.specular = 0;")?;
    } else {
        vertex.push_str("    o.color = v.color;\n    o.specular = 0;\n");
    }

    // Texture stages: combine into `previous`.
    let mut constants = Vec::new();
    for (i, stage) in ff.stages.iter().enumerate() {
        let tex = &stage.texture;
        declare(format!("sampler2D {tex};"));
        declare(format!("float4 {tex}_ST;"));
        let uv = stage_uv(i);
        writeln!(
            vertex,
            "    o.uv{i} = v.texcoord{uv}.xy * {tex}_ST.xy + {tex}_ST.zw;"
        )?;
        let constant = match &stage.constant {
            Some(c) => color(c, &mut declare),
            None => "fixed4(1, 1, 1, 1)".into(),
        };
        constants.push(constant.clone());
        let (rgb, alpha) = combine(&stage.combine)?;
        writeln!(
            fragment,
            "    {{
        fixed4 ff_texture = tex2D({tex}, i.uv{i});
        fixed4 ff_constant = {constant};
        fixed4 rgb = {rgb};
        fixed4 alpha = {alpha};
        ff_previous = fixed4(rgb.rgb, alpha.a);
    }}"
        )?;
    }
    if ff.lighting && ff.separate_specular {
        fragment.push_str("    ff_previous.rgb = saturate(ff_previous.rgb + i.specular);\n");
    }
    if let Some((comparison, reference)) = &ff.alpha_test {
        let reference = color(reference, &mut declare);
        let pass = match comparison.to_ascii_lowercase().as_str() {
            "greater" => "ff_previous.a > r",
            "gequal" => "ff_previous.a >= r",
            "less" => "ff_previous.a < r",
            "lequal" => "ff_previous.a <= r",
            "equal" => "ff_previous.a == r",
            "notequal" => "ff_previous.a != r",
            "never" => "false",
            _ => "true",
        };
        writeln!(
            fragment,
            "    {{ float r = {reference}.x; if (!({pass})) discard; }}"
        )?;
    }

    let mut out = String::new();
    out.push_str("#pragma vertex unbound_ff_vert\n#pragma fragment unbound_ff_frag\n");
    out.push_str("#include \"UnityCG.cginc\"\n#include \"UnityLightingCommon.cginc\"\n");
    for u in &uniforms {
        out.push_str(u);
        out.push('\n');
    }
    out.push_str(
        "struct unbound_ff_appdata {
    float4 vertex : POSITION;
    float3 normal : NORMAL;
    fixed4 color : COLOR;
    float4 texcoord0 : TEXCOORD0;
    float4 texcoord1 : TEXCOORD1;
};
struct unbound_ff_v2f {
    float4 pos : SV_POSITION;
    fixed4 color : COLOR0;
    fixed3 specular : COLOR1;
",
    );
    for i in 0..ff.stages.len() {
        writeln!(out, "    float2 uv{i} : TEXCOORD{i};")?;
    }
    out.push_str(
        "};
unbound_ff_v2f unbound_ff_vert(unbound_ff_appdata v)
{
    unbound_ff_v2f o;
    o.pos = UnityObjectToClipPos(v.vertex);
",
    );
    out.push_str(&vertex);
    out.push_str(
        "    return o;
}
fixed4 unbound_ff_frag(unbound_ff_v2f i) : SV_Target
{
    fixed4 ff_primary = i.color;
    fixed4 ff_previous = ff_primary;
",
    );
    out.push_str(&fragment);
    out.push_str("    return ff_previous;\n}\n");
    Ok(out)
}

/// A combine expression's color and alpha parts as HLSL (over `texture`, `primary`,
/// `previous`, `constant`).
fn combine(text: &str) -> Result<(String, String)> {
    let tokens = tokens(text);
    let comma = tokens.iter().position(|t| t == ",");
    let (rgb, alpha) = match comma {
        Some(at) => (&tokens[..at], &tokens[at + 1..]),
        None => (&tokens[..], &tokens[..]),
    };
    Ok((expression(rgb)?, expression(alpha)?))
}

fn tokens(text: &str) -> Vec<String> {
    let mut out = Vec::new();
    let mut word = String::new();
    for c in text.chars() {
        if c.is_alphanumeric() || c == '_' {
            word.push(c);
            continue;
        }
        if !word.is_empty() {
            out.push(std::mem::take(&mut word));
        }
        if matches!(c, '*' | '+' | '-' | '(' | ')' | ',') {
            // `+-`: signed add.
            if c == '-' && out.last().is_some_and(|t| t == "+") {
                out.pop();
                out.push("+-".into());
            } else {
                out.push(c.to_string());
            }
        }
    }
    if !word.is_empty() {
        out.push(word);
    }
    out
}

/// One part of a combine: `src1 op src2 [op src3]` with `lerp(src) ` and `Double`/`Quad`.
fn expression(tokens: &[String]) -> Result<String> {
    let mut i = 0;
    let mut scale = 1;
    let mut sources = Vec::new();
    let mut ops: Vec<String> = Vec::new();
    while i < tokens.len() {
        let t = tokens[i].to_ascii_lowercase();
        match t.as_str() {
            "double" => {
                scale = 2;
                i += 1;
            }
            "quad" => {
                scale = 4;
                i += 1;
            }
            "*" | "+" | "-" | "+-" => {
                ops.push(t);
                i += 1;
            }
            "lerp" => {
                // `lerp ( src )`
                let close = tokens[i..]
                    .iter()
                    .position(|t| t == ")")
                    .map(|p| p + i)
                    .ok_or_else(|| anyhow::anyhow!("unclosed lerp in `{}`", tokens.join(" ")))?;
                let inner = source(&tokens[i + 2..close])?;
                ops.push(format!("lerp:{inner}"));
                i = close + 1;
            }
            _ => {
                // A source: `[one -] name [alpha]`.
                let mut j = i;
                let mut one_minus = false;
                if t == "one" && tokens.get(i + 1).is_some_and(|t| t == "-") {
                    one_minus = true;
                    j += 2;
                }
                let mut end = j + 1;
                if tokens
                    .get(end)
                    .is_some_and(|t| t.eq_ignore_ascii_case("alpha"))
                {
                    end += 1;
                }
                let mut s = source(&tokens[j..end])?;
                if one_minus {
                    s = format!("(1.0 - {s})");
                }
                sources.push(s);
                i = end;
            }
        }
    }
    let value = match (sources.as_slice(), ops.as_slice()) {
        ([a], []) => a.clone(),
        ([a, b], [op]) if op.starts_with("lerp:") => {
            // `a lerp(x) b`: from b to a by x's alpha.
            format!("lerp({b}, {a}, {}.a)", &op[5..])
        }
        ([a, b], [op]) => match op.as_str() {
            "*" => format!("({a} * {b})"),
            "+" => format!("({a} + {b})"),
            "-" => format!("({a} - {b})"),
            _ => format!("({a} + {b} - 0.5)"),
        },
        ([a, b, c], [m, op]) if m == "*" => match op.as_str() {
            "+" => format!("({a} * {b} + {c})"),
            "-" => format!("({a} * {b} - {c})"),
            "+-" => format!("({a} * {b} + {c} - 0.5)"),
            _ => bail!("combine `{}` is not supported", tokens.join(" ")),
        },
        _ => bail!("combine `{}` is not supported", tokens.join(" ")),
    };
    Ok(if scale == 1 {
        format!("saturate({value})")
    } else {
        format!("saturate({value} * {scale})")
    })
}

fn source(tokens: &[String]) -> Result<String> {
    let name = tokens
        .first()
        .map(|t| t.to_ascii_lowercase())
        .unwrap_or_default();
    let base = match name.as_str() {
        // `texture` is an HLSL keyword: the program's variables are prefixed.
        "texture" | "primary" | "previous" | "constant" => format!("ff_{name}"),
        other => bail!("combine source `{other}` is not supported"),
    };
    let alpha = tokens
        .get(1)
        .is_some_and(|t| t.eq_ignore_ascii_case("alpha"));
    Ok(if alpha { format!("{base}.aaaa") } else { base })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn combiners_become_expressions() {
        let (rgb, alpha) = combine("texture * primary double, texture alpha").unwrap();
        assert_eq!(rgb, "saturate((ff_texture * ff_primary) * 2)");
        assert_eq!(alpha, "saturate(ff_texture.aaaa)");
        let (rgb, _) = combine("texture lerp(texture) previous").unwrap();
        assert_eq!(rgb, "saturate(lerp(ff_previous, ff_texture, ff_texture.a))");
        let (rgb, _) = combine("one - texture * primary").unwrap();
        assert_eq!(rgb, "saturate(((1.0 - ff_texture) * ff_primary))");
        let (rgb, _) = combine("previous * texture +- constant").unwrap();
        assert_eq!(
            rgb,
            "saturate((ff_previous * ff_texture + ff_constant - 0.5))"
        );
    }

    #[test]
    fn a_pass_becomes_a_program() {
        let ff = FixedFunction {
            stages: vec![TextureStage {
                texture: "_MainTex".into(),
                combine: "texture * primary".into(),
                constant: Some(ColorValue::Property("_TintColor".into())),
            }],
            bindings: vec![("Color".into(), "color".into())],
            alpha_test: Some(("Greater".into(), ColorValue::Property("_Cutoff".into()))),
            ..Default::default()
        };
        let source = generate(&ff).unwrap();
        for part in [
            "sampler2D _MainTex;",
            "fixed4 _TintColor;",
            "fixed4 _Cutoff;",
            "o.color = v.color;",
            "ff_previous.a > r",
        ] {
            assert!(source.contains(part), "{part}:\n{source}");
        }
    }
}
