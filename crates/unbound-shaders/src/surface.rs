//! Unity surface shaders (`#pragma surface surf Lambert ...`): the forward base pass Unity's
//! surface shader compiler generates from them, as a vertex/fragment program for the usual path.
//!
//! As in Unity, the shader's code is analysed in its preprocessed variant, after Unity's lighting
//! includes: the input struct the surface function reads, the output struct it fills (and whether
//! it writes `Normal`, which puts it in tangent space), the vertex modifier's input type, and the
//! lighting function's form (`Lighting<Name>(s, gi)` / `(s, viewDir, gi)` with
//! `Lighting<Name>_GI`, or the older `(s, lightDir, atten)` / `(s, lightDir, viewDir, atten)`).
//! The generated code follows Unity's ("Show generated code"): `v2f_surf`, `vert_surf` and
//! `frag_surf`, lit by the main directional light (`_WorldSpaceLightPos0`, `_LightColor0`),
//! with ambient from spherical harmonics (`unity_SH*`), vertex-lit point lights
//! (`VERTEXLIGHT_ON`), and baked lightmap coordinates from UV2 and `unity_LightmapST`.
//! Forward-add passes carry the additional light without baked GI.
use crate::{
    Program,
    preprocess::{Kind, Tok, tokenize},
};
use anyhow::{Result, anyhow, bail};
use std::{collections::HashSet, fmt::Write, ops::Range};

/// The generated pass's entry points.
pub(crate) const VERTEX_ENTRY: &str = "vert_surf";
pub(crate) const FRAGMENT_ENTRY: &str = "frag_surf";

#[derive(Debug, Clone, Copy, PartialEq)]
enum Alpha {
    Opaque,
    Fade,
    Premultiplied,
}

/// `#pragma surface`: the surface function, the lighting model and the options that shape the
/// forward base pass.
#[derive(Debug, Clone, PartialEq)]
pub(crate) struct Surface {
    surf: String,
    model: String,
    vertex: Option<String>,
    final_color: Option<String>,
    alpha: Alpha,
    alpha_test: Option<String>,
    /// The output keeps its alpha (`keepalpha`, blending); otherwise it is 1, as opaque.
    keep_alpha: bool,
    fog: bool,
    ambient: bool,
    vertex_lights: bool,
    half_as_view: bool,
}

impl Surface {
    /// Reads the pragma's words after `surface`.
    pub(crate) fn parse(words: &[String]) -> Result<Surface> {
        let [surf, model, options @ ..] = words else {
            bail!("#pragma surface names no surface function and lighting model");
        };
        // `alpha`/`alpha:auto` is premultiplied for physically based lighting.
        let pbs = matches!(model.as_str(), "Standard" | "StandardSpecular");
        let mut s = Surface {
            surf: surf.clone(),
            model: model.clone(),
            vertex: None,
            final_color: None,
            alpha: Alpha::Opaque,
            alpha_test: None,
            keep_alpha: false,
            fog: true,
            ambient: true,
            vertex_lights: true,
            half_as_view: false,
        };
        for option in options {
            let (name, value) = match option.split_once(':') {
                Some((n, v)) => (n, Some(v)),
                None => (option.as_str(), None),
            };
            match (name, value) {
                ("alpha", None | Some("auto")) => {
                    s.alpha = if pbs {
                        Alpha::Premultiplied
                    } else {
                        Alpha::Fade
                    }
                }
                ("alpha", Some("fade" | "blend")) => s.alpha = Alpha::Fade,
                ("alpha", Some("premul")) => s.alpha = Alpha::Premultiplied,
                ("alphatest", Some(v)) => s.alpha_test = Some(v.to_string()),
                ("keepalpha", None) | ("decal", Some("blend")) => s.keep_alpha = true,
                ("vertex", Some(f)) => s.vertex = Some(f.to_string()),
                ("finalcolor", Some(f)) => s.final_color = Some(f.to_string()),
                ("nofog", None) => s.fog = false,
                ("noambient", None) => s.ambient = false,
                ("novertexlights", None) => s.vertex_lights = false,
                ("halfasview", None) => s.half_as_view = true,
                ("tessellate", _) => {
                    bail!("#pragma surface {option}: surface shader tessellation is not supported")
                }
                // The other passes (forward add, shadow caster, deferred, meta), lightmaps and
                // instancing: Unbound generates and draws the forward base pass only.
                _ => {}
            }
        }
        s.keep_alpha |= s.alpha != Alpha::Opaque;
        Ok(s)
    }

    /// The program Unbound compiles: the pass's directives and Unity's lighting includes, the
    /// shader's own code (its `#pragma surface` commented out), then, given the analysis of the
    /// former, the generated pass.
    pub(crate) fn program(&self, shader: &Program, analysis: Option<&Analysis>) -> Program {
        // The forward add pass when the program is that pass's (one per pixel light).
        let add = shader
            .light_mode
            .as_deref()
            .is_some_and(|m| m.eq_ignore_ascii_case("ForwardAdd"));
        // Before the analysis, `INTERNAL_DATA` is in place so the input struct shows it.
        let mut source = self.head(analysis.is_none_or(|a| a.tangent_normal), add);
        source.push_str("// Original surface shader snippet:\n");
        source.push_str(&comment_out_pragma(&shader.source));
        if let Some(a) = analysis {
            source.push('\n');
            source.push_str(&self.pass(a, add));
        }
        Program {
            file: shader.file.clone(),
            source,
            cg: true,
            light_mode: Some(if add { "ForwardAdd" } else { "ForwardBase" }.into()),
        }
    }

    fn head(&self, tangent_normal: bool, add: bool) -> String {
        let mut h = String::new();
        let mut line = |l: &str| {
            h.push_str(l);
            h.push('\n');
        };
        line(if add {
            "// The forward add pass Unity generates for a surface shader."
        } else {
            "// The forward base pass Unity generates for a surface shader."
        });
        line(&format!("#pragma vertex {VERTEX_ENTRY}"));
        line(&format!("#pragma fragment {FRAGMENT_ENTRY}"));
        if self.fog {
            line("#pragma multi_compile_fog");
        }
        line(if add {
            "#pragma multi_compile_fwdadd"
        } else if self.vertex_lights {
            "#pragma multi_compile_fwdbase"
        } else {
            "#pragma multi_compile_fwdbase novertexlight"
        });
        line("#include \"UnityShaderUtilities.cginc\"");
        if add {
            line("#ifndef UNITY_PASS_FORWARDADD");
            line("#define UNITY_PASS_FORWARDADD");
            line("#endif");
        } else {
            line("#ifndef UNITY_PASS_FORWARDBASE");
            line("#define UNITY_PASS_FORWARDBASE");
            line("#endif");
        }
        if self.alpha == Alpha::Premultiplied {
            line("#define _ALPHAPREMULTIPLY_ON 1");
        }
        if !self.ambient {
            line("#undef LIGHTPROBE_SH");
        }
        // No reflection probes: specular reflections are the sky's average color.
        line("#ifndef _GLOSSYREFLECTIONS_OFF");
        line("#define _GLOSSYREFLECTIONS_OFF 1");
        line("#endif");
        line("#include \"UnityCG.cginc\"");
        line("#include \"Lighting.cginc\"");
        if matches!(self.model.as_str(), "Standard" | "StandardSpecular") {
            line("#include \"UnityPBSLighting.cginc\"");
        }
        line("#include \"AutoLight.cginc\"");
        if tangent_normal {
            line(
                "#define INTERNAL_DATA half3 internalSurfaceTtoW0; half3 internalSurfaceTtoW1; half3 internalSurfaceTtoW2;",
            );
            line(
                "#define WorldReflectionVector(data,normal) reflect (data.worldRefl, half3(dot(data.internalSurfaceTtoW0,normal), dot(data.internalSurfaceTtoW1,normal), dot(data.internalSurfaceTtoW2,normal)))",
            );
            line(
                "#define WorldNormalVector(data,normal) fixed3(dot(data.internalSurfaceTtoW0,normal), dot(data.internalSurfaceTtoW1,normal), dot(data.internalSurfaceTtoW2,normal))",
            );
        } else {
            line("#define INTERNAL_DATA");
            line("#define WorldReflectionVector(data,normal) data.worldRefl");
            line("#define WorldNormalVector(data,normal) normal");
        }
        h
    }

    /// What the generated pass needs to know of the shader's code: `text` is
    /// [`Surface::program`] without an analysis, preprocessed for the variant.
    pub(crate) fn analyse(&self, text: &str) -> Result<Analysis> {
        let t: Vec<Tok> = tokenize(text).into_iter().filter(|t| !t.blank()).collect();
        let items = items(&t);
        let functions = |name: &str| {
            items
                .iter()
                .filter_map(|i| match i {
                    Item::Function(f) if f.name == name => Some(f),
                    _ => None,
                })
                .collect::<Vec<_>>()
        };
        let fields = |name: &str| {
            items.iter().rev().find_map(|i| match i {
                Item::Struct(n, body) if n == name => Some(fields(&t[body.clone()])),
                _ => None,
            })
        };

        let surf = functions(&self.surf)
            .into_iter()
            .find(|f| f.params.len() == 2)
            .ok_or_else(|| {
                anyhow!(
                    "the surface function `{}` (Input, inout output) is not defined",
                    self.surf
                )
            })?;
        let input = surf.params[0].ty.clone();
        let output = surf.params[1].ty.clone();
        let input_fields =
            fields(&input).ok_or_else(|| anyhow!("the input struct `{input}` is not defined"))?;
        let output_members: Vec<String> = fields(&output)
            .ok_or_else(|| anyhow!("the output struct `{output}` is not defined"))?
            .into_iter()
            .map(|f| f.name)
            .collect();
        let tangent_normal = output_members.iter().any(|m| m == "Normal")
            && writes(&t, &items, &surf.body, &surf.params[1].name, "Normal", 0);

        let (appdata, custom_data) = match &self.vertex {
            Some(name) => {
                let f = functions(name)
                    .into_iter()
                    .find(|f| (1..=2).contains(&f.params.len()))
                    .ok_or_else(|| {
                        anyhow!("the vertex modifier `{name}` (inout appdata) is not defined")
                    })?;
                (f.params[0].ty.clone(), f.params.len() == 2)
            }
            None => ("appdata_full".to_string(), false),
        };
        let appdata_members: Vec<String> = fields(&appdata)
            .ok_or_else(|| anyhow!("the vertex input struct `{appdata}` is not defined"))?
            .into_iter()
            .map(|f| f.name)
            .collect();

        let name = format!("Lighting{}", self.model);
        let has_gi = !functions(&format!("{name}_GI")).is_empty();
        let forms: Vec<Lighting> = functions(&name)
            .iter()
            .filter_map(|f| {
                let gi = f.params.last().is_some_and(|p| p.ty == "UnityGI");
                Some(match (f.params.len(), gi) {
                    (2, true) => Lighting::Gi { view: false },
                    (3, true) => Lighting::Gi { view: true },
                    (3, false) => Lighting::Old { view: false },
                    (4, false) => Lighting::Old { view: true },
                    _ => return None,
                })
            })
            .collect();
        let lighting = forms
            .iter()
            .find(|f| has_gi && matches!(f, Lighting::Gi { .. }))
            .or_else(|| forms.iter().find(|f| matches!(f, Lighting::Old { .. })))
            .copied()
            .ok_or_else(|| {
                if forms.is_empty() {
                    anyhow!("the lighting function `{name}` is not defined")
                } else {
                    anyhow!("`{name}` takes a UnityGI, but `{name}_GI` is not defined")
                }
            })?;

        // What the pass reads that the shader does not declare.
        let names: HashSet<&str> = t
            .iter()
            .filter(|t| t.kind == Kind::Ident)
            .map(|t| &*t.text)
            .collect();
        let mut declare: Vec<String> = Vec::new();
        for f in &input_fields {
            if let Role::Uv(_, texture) = role(f) {
                let st = format!("{texture}_ST");
                let line = format!("float4 {st};");
                if !names.contains(st.as_str()) && !declare.contains(&line) {
                    declare.push(line);
                }
            }
        }
        if let Some(cutoff) = &self.alpha_test
            && !names.contains(cutoff.as_str())
        {
            declare.push(format!("fixed {cutoff};"));
        }

        Ok(Analysis {
            input,
            input_fields,
            output,
            output_members,
            tangent_normal,
            appdata,
            appdata_members,
            custom_data,
            lighting,
            declare,
        })
    }

    /// The generated `v2f_surf`, `vert_surf` and `frag_surf`.
    fn pass(&self, a: &Analysis, add: bool) -> String {
        let tangent = a.tangent_normal;
        let gi = matches!(a.lighting, Lighting::Gi { .. });
        let mut slot = 0;
        let mut texcoord = || {
            slot += 1;
            slot - 1
        };

        // Interpolated input members, packed one per interpolator.
        let mut v2f = String::new();
        let mut custom = String::new(); // from the vertex modifier's data
        let mut computed = String::new(); // once the position is known
        let mut unpack = String::new();
        for (k, f) in a.input_fields.iter().enumerate() {
            let field = |v2f: &mut String, n: u32, what: &str| {
                let _ = writeln!(v2f, "  {} pack{k} : TEXCOORD{n}; // {what}", f.ty);
            };
            match role(f) {
                Role::Uv(set, texture) => {
                    field(&mut v2f, texcoord(), &texture);
                    let coord = ["texcoord", "texcoord1", "texcoord2", "texcoord3"][set];
                    let _ = writeln!(
                        computed,
                        "  o.pack{k}.xy = TRANSFORM_TEX(v.{coord}, {texture});"
                    );
                }
                Role::Color => {
                    field(&mut v2f, texcoord(), "vertex color");
                    let _ = writeln!(computed, "  o.pack{k} = v.color;");
                }
                Role::ScreenPos => {
                    field(&mut v2f, texcoord(), "screen position");
                    let _ = writeln!(computed, "  o.pack{k} = ComputeScreenPos(o.pos);");
                }
                Role::Custom if a.custom_data => {
                    field(&mut v2f, texcoord(), &f.name);
                    let _ = writeln!(custom, "  o.pack{k} = customInputData.{};", f.name);
                }
                _ => continue,
            }
            let _ = writeln!(unpack, "  surfIN.{} = IN.pack{k};", f.name);
        }
        if tangent {
            for i in 0..3 {
                let _ = writeln!(v2f, "  float4 tSpace{i} : TEXCOORD{};", texcoord());
            }
        } else {
            let _ = writeln!(v2f, "  float3 worldNormal : TEXCOORD{};", texcoord());
            let _ = writeln!(v2f, "  float3 worldPos : TEXCOORD{};", texcoord());
        }
        if gi {
            let _ = writeln!(
                v2f,
                "  #if UNITY_SHOULD_SAMPLE_SH\n  half3 sh : TEXCOORD{}; // SH\n  #endif",
                texcoord()
            );
        } else {
            let _ = writeln!(
                v2f,
                "  fixed3 vlight : TEXCOORD{}; // ambient/SH/vertexlights",
                texcoord()
            );
        }
        if gi && !add {
            let _ = writeln!(
                v2f,
                "  #ifdef LIGHTMAP_ON\n  float2 lmap : TEXCOORD{};\n  #endif",
                texcoord()
            );
            let uv = if a.appdata_members.iter().any(|m| m == "texcoord1") {
                "v.texcoord1.xy"
            } else {
                "float2(0,0)"
            };
            let _ = writeln!(
                computed,
                "  #ifdef LIGHTMAP_ON\n  o.lmap = {uv} * unity_LightmapST.xy + unity_LightmapST.zw;\n  #endif"
            );
        }
        if self.fog {
            let _ = writeln!(v2f, "  UNITY_FOG_COORDS({})", texcoord());
        }
        if add {
            // The light's coordinates (attenuation, cookie) and shadow coordinates.
            let a = texcoord();
            let b = texcoord();
            let _ = writeln!(v2f, "  UNITY_LIGHTING_COORDS({a},{b})");
        } else {
            let _ = writeln!(v2f, "  UNITY_SHADOW_COORDS({})", texcoord());
        }

        let mut s = String::new();
        let _ = write!(
            s,
            "// vertex-to-fragment interpolation data\n\
             struct v2f_surf {{\n  UNITY_POSITION(pos);\n{v2f}  UNITY_VERTEX_INPUT_INSTANCE_ID\n  UNITY_VERTEX_OUTPUT_STEREO\n}};\n"
        );
        for d in &a.declare {
            let _ = writeln!(s, "{d}");
        }

        // The vertex shader.
        let _ = write!(
            s,
            "\n// vertex shader\nv2f_surf {VERTEX_ENTRY} ({} v) {{\n  UNITY_SETUP_INSTANCE_ID(v);\n  v2f_surf o;\n  UNITY_INITIALIZE_OUTPUT(v2f_surf,o);\n  UNITY_TRANSFER_INSTANCE_ID(v,o);\n  UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);\n",
            a.appdata
        );
        match &self.vertex {
            Some(f) if a.custom_data => {
                let _ = write!(
                    s,
                    "  {} customInputData;\n  {f} (v, customInputData);\n{custom}",
                    a.input
                );
            }
            Some(f) => {
                let _ = writeln!(s, "  {f} (v);");
            }
            None => {}
        }
        let _ = write!(
            s,
            "  o.pos = UnityObjectToClipPos(v.vertex);\n{computed}  float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;\n  float3 worldNormal = UnityObjectToWorldNormal(v.normal);\n"
        );
        if tangent {
            s.push_str(
                "  fixed3 worldTangent = UnityObjectToWorldDir(v.tangent.xyz);\n\
                 \x20 fixed tangentSign = v.tangent.w * unity_WorldTransformParams.w;\n\
                 \x20 fixed3 worldBinormal = cross(worldNormal, worldTangent) * tangentSign;\n\
                 \x20 o.tSpace0 = float4(worldTangent.x, worldBinormal.x, worldNormal.x, worldPos.x);\n\
                 \x20 o.tSpace1 = float4(worldTangent.y, worldBinormal.y, worldNormal.y, worldPos.y);\n\
                 \x20 o.tSpace2 = float4(worldTangent.z, worldBinormal.z, worldNormal.z, worldPos.z);\n",
            );
        } else {
            s.push_str("  o.worldPos.xyz = worldPos;\n  o.worldNormal = worldNormal;\n");
        }
        let point_lights = |target: &str| {
            if self.vertex_lights {
                format!(
                    "  #ifdef VERTEXLIGHT_ON\n  {target} += Shade4PointLights (\n    unity_4LightPosX0, unity_4LightPosY0, unity_4LightPosZ0,\n    unity_LightColor[0].rgb, unity_LightColor[1].rgb, unity_LightColor[2].rgb, unity_LightColor[3].rgb,\n    unity_4LightAtten0, worldPos, worldNormal);\n  #endif\n"
                )
            } else {
                String::new()
            }
        };
        s.push_str("  // SH/ambient and vertex lights\n");
        if add {
            // The add pass lights with its one light.
        } else if gi {
            let _ = write!(
                s,
                "  #if UNITY_SHOULD_SAMPLE_SH && !UNITY_SAMPLE_FULL_SH_PER_PIXEL\n  o.sh = 0;\n{}  o.sh = ShadeSHPerVertex (worldNormal, o.sh);\n  #endif\n",
                point_lights("o.sh")
            );
        } else {
            let _ = write!(
                s,
                "  #if UNITY_SHOULD_SAMPLE_SH && !UNITY_SAMPLE_FULL_SH_PER_PIXEL\n  o.vlight = ShadeSH9 (float4(worldNormal,1.0));\n  #else\n  o.vlight = 0.0;\n  #endif\n{}",
                point_lights("o.vlight")
            );
        }
        let lightmap_uv = if a.appdata_members.iter().any(|m| m == "texcoord1") {
            "v.texcoord1.xy"
        } else {
            "0"
        };
        let _ = writeln!(s, "  UNITY_TRANSFER_LIGHTING(o,{lightmap_uv});");
        if self.fog {
            s.push_str("  UNITY_TRANSFER_FOG(o,o.pos);\n");
        }
        s.push_str("  return o;\n}\n");

        // The fragment shader.
        let facing = a
            .input_fields
            .iter()
            .any(|f| matches!(role(f), Role::Facing));
        let _ = write!(
            s,
            "\n// fragment shader\nfixed4 {FRAGMENT_ENTRY} (v2f_surf IN{}) : SV_Target {{\n  UNITY_SETUP_INSTANCE_ID(IN);\n",
            if facing { ", fixed facing : VFACE" } else { "" }
        );
        if tangent {
            s.push_str("  UNITY_EXTRACT_TBN(IN);\n");
        }
        let _ = writeln!(s, "  // prepare and unpack data\n  {} surfIN;", a.input);
        if self.fog {
            s.push_str("  UNITY_EXTRACT_FOG(IN);\n");
        }
        let _ = write!(
            s,
            "  UNITY_INITIALIZE_OUTPUT({},surfIN);\n{unpack}",
            a.input
        );
        s.push_str(if tangent {
            "  float3 worldPos = float3(IN.tSpace0.w, IN.tSpace1.w, IN.tSpace2.w);\n"
        } else {
            "  float3 worldPos = IN.worldPos.xyz;\n"
        });
        s.push_str(
            "  #ifndef USING_DIRECTIONAL_LIGHT\n    fixed3 lightDir = normalize(UnityWorldSpaceLightDir(worldPos));\n  #else\n    fixed3 lightDir = _WorldSpaceLightPos0.xyz;\n  #endif\n  float3 worldViewDir = normalize(UnityWorldSpaceViewDir(worldPos));\n",
        );
        for f in &a.input_fields {
            let value = match role(f) {
                Role::ViewDir if tangent => "_unity_tbn_0 * worldViewDir.x + _unity_tbn_1 * worldViewDir.y  + _unity_tbn_2 * worldViewDir.z".to_string(),
                Role::ViewDir => "worldViewDir".into(),
                Role::WorldPos => "worldPos".into(),
                Role::WorldNormal if tangent => "0.0".into(),
                Role::WorldNormal => "IN.worldNormal".into(),
                Role::WorldRefl if tangent => "-worldViewDir".into(),
                Role::WorldRefl => "reflect(-worldViewDir, IN.worldNormal)".into(),
                Role::Internal(i) if tangent => format!("_unity_tbn_{i}"),
                Role::Facing => "facing".into(),
                _ => continue,
            };
            let _ = writeln!(s, "  surfIN.{} = {value};", f.name);
        }
        let member = |m: &str| a.output_members.iter().any(|o| o == m);
        let _ = writeln!(s, "  {0} o = ({0})0;", a.output);
        if member("Occlusion") {
            s.push_str("  o.Occlusion = 1.0;\n");
        }
        if member("Normal") {
            s.push_str(if tangent {
                "  o.Normal = fixed3(0,0,1);\n"
            } else {
                "  o.Normal = IN.worldNormal;\n"
            });
        }
        let _ = writeln!(
            s,
            "\n  // call surface function\n  {} (surfIN, o);",
            self.surf
        );
        if let Some(cutoff) = &self.alpha_test {
            let _ = writeln!(s, "\n  // alpha test\n  clip (o.Alpha - {cutoff});");
        }
        if tangent {
            s.push_str(
                "  float3 worldN;\n  worldN.x = dot(_unity_tbn_0, o.Normal);\n  worldN.y = dot(_unity_tbn_1, o.Normal);\n  worldN.z = dot(_unity_tbn_2, o.Normal);\n  worldN = normalize(worldN);\n  o.Normal = worldN;\n",
            );
        }
        s.push_str(
            "\n  // compute lighting & shadowing factor\n  UNITY_LIGHT_ATTENUATION(atten, IN, worldPos)\n  fixed4 c = 0;\n",
        );
        let lighting = format!("Lighting{}", self.model);
        match a.lighting {
            Lighting::Gi { view } if add => {
                let _ = writeln!(
                    s,
                    "\n  // Setup lighting environment\n  UnityGI gi;\n  UNITY_INITIALIZE_OUTPUT(UnityGI, gi);\n  gi.indirect.diffuse = 0;\n  gi.indirect.specular = 0;\n  gi.light.color = _LightColor0.rgb;\n  gi.light.dir = lightDir;\n  gi.light.color *= atten;\n  c += {lighting} (o, {}gi);",
                    if view { "worldViewDir, " } else { "" }
                );
            }
            Lighting::Old { view } if add => {
                let view = match (view, self.half_as_view) {
                    (false, _) => "",
                    (true, false) => "worldViewDir, ",
                    (true, true) => "normalize(lightDir + worldViewDir), ",
                };
                let _ = writeln!(s, "\n  c += {lighting} (o, lightDir, {view}atten);");
            }
            Lighting::Gi { view } => {
                let _ = write!(
                    s,
                    "\n  // Setup lighting environment\n  UnityGI gi;\n  UNITY_INITIALIZE_OUTPUT(UnityGI, gi);\n  gi.indirect.diffuse = 0;\n  gi.indirect.specular = 0;\n  gi.light.color = _LightColor0.rgb;\n  gi.light.dir = lightDir;\n  // Call GI (lightmaps/SH/reflections) lighting function\n  UnityGIInput giInput;\n  UNITY_INITIALIZE_OUTPUT(UnityGIInput, giInput);\n  giInput.light = gi.light;\n  giInput.worldPos = worldPos;\n  giInput.worldViewDir = worldViewDir;\n  giInput.atten = atten;\n  giInput.lightmapUV = 0.0;\n  #ifdef LIGHTMAP_ON\n    giInput.lightmapUV.xy = IN.lmap;\n  #endif\n  #if UNITY_SHOULD_SAMPLE_SH && !UNITY_SAMPLE_FULL_SH_PER_PIXEL\n    giInput.ambient = IN.sh;\n  #else\n    giInput.ambient.rgb = 0.0;\n  #endif\n  giInput.probeHDR[0] = unity_SpecCube0_HDR;\n  giInput.probeHDR[1] = unity_SpecCube1_HDR;\n  #if defined(UNITY_SPECCUBE_BLENDING) || defined(UNITY_SPECCUBE_BOX_PROJECTION)\n    giInput.boxMin[0] = unity_SpecCube0_BoxMin; // .w holds lerp value for blending\n  #endif\n  #ifdef UNITY_SPECCUBE_BOX_PROJECTION\n    giInput.boxMax[0] = unity_SpecCube0_BoxMax;\n    giInput.probePosition[0] = unity_SpecCube0_ProbePosition;\n    giInput.boxMax[1] = unity_SpecCube1_BoxMax;\n    giInput.boxMin[1] = unity_SpecCube1_BoxMin;\n    giInput.probePosition[1] = unity_SpecCube1_ProbePosition;\n  #endif\n  {lighting}_GI(o, giInput, gi);\n\n  // realtime lighting: call lighting function\n  c += {lighting} (o, {}gi);\n",
                    if view { "worldViewDir, " } else { "" }
                );
            }
            Lighting::Old { view } => {
                let view = match (view, self.half_as_view) {
                    (false, _) => "",
                    (true, false) => "worldViewDir, ",
                    (true, true) => "normalize(lightDir + worldViewDir), ",
                };
                let _ = writeln!(
                    s,
                    "\n  // realtime lighting: call lighting function\n  c = {lighting} (o, lightDir, {view}atten);\n  c.rgb += o.Albedo * IN.vlight;"
                );
            }
        }
        if member("Emission") && !add {
            s.push_str("  c.rgb += o.Emission;\n");
        }
        if add && !self.keep_alpha {
            s.push_str("  c.a = 0.0;\n");
        }
        if let Some(f) = &self.final_color {
            let _ = writeln!(s, "  {f} (surfIN, o, c);");
        }
        if self.fog {
            s.push_str(if add {
                "  UNITY_APPLY_FOG_COLOR(_unity_fogCoord, c, fixed4(0,0,0,0)); // apply fog\n"
            } else {
                "  UNITY_APPLY_FOG(_unity_fogCoord, c); // apply fog\n"
            });
        }
        if !self.keep_alpha {
            s.push_str("  UNITY_OPAQUE_ALPHA(c.a);\n");
        }
        s.push_str("  return c;\n}\n");
        s
    }
}

/// What the generated pass needs to know of the shader's code (see [`Surface::analyse`]).
#[derive(Debug, Clone, PartialEq)]
pub(crate) struct Analysis {
    /// The surface function's input struct and its members.
    input: String,
    input_fields: Vec<Field>,
    /// The output struct (`SurfaceOutput`, `SurfaceOutputStandard`, ...) and its members.
    output: String,
    output_members: Vec<String>,
    /// The surface function writes `Normal` (in tangent space, then).
    tangent_normal: bool,
    /// The vertex shader's input struct (the vertex modifier's, else `appdata_full`).
    appdata: String,
    appdata_members: Vec<String>,
    /// The vertex modifier fills the input struct's custom members.
    custom_data: bool,
    lighting: Lighting,
    /// Declarations the pass needs that the shader lacks (`_MainTex_ST`, the alpha cutoff).
    declare: Vec<String>,
}

/// The form of the lighting function.
#[derive(Debug, Clone, Copy, PartialEq)]
enum Lighting {
    /// `(s, gi)` / `(s, viewDir, gi)`, with `_GI` computing the global illumination.
    Gi { view: bool },
    /// `(s, lightDir, atten)` / `(s, lightDir, viewDir, atten)`, ambient added after.
    Old { view: bool },
}

/// A struct member or function parameter.
#[derive(Debug, Clone, PartialEq)]
struct Field {
    ty: String,
    name: String,
    semantic: Option<String>,
}

/// What the pass fills an input struct member with.
enum Role {
    /// `uv<set>_<Texture>`: the texture set's coordinates, with the texture's tiling and offset.
    Uv(usize, String),
    /// A `COLOR` member: the vertex color.
    Color,
    ScreenPos,
    WorldPos,
    WorldNormal,
    WorldRefl,
    ViewDir,
    /// `INTERNAL_DATA`'s rows of the tangent-to-world matrix.
    Internal(usize),
    /// Data of the vertex modifier's.
    Custom,
    /// A `VFACE` member: which side of the triangle the fragment is on (1 front, -1 back).
    Facing,
}

fn role(f: &Field) -> Role {
    if f.semantic
        .as_deref()
        .is_some_and(|s| s.eq_ignore_ascii_case("VFACE"))
    {
        return Role::Facing;
    }
    if f.semantic
        .as_deref()
        .is_some_and(|s| s.eq_ignore_ascii_case("COLOR") || s.eq_ignore_ascii_case("COLOR0"))
    {
        return Role::Color;
    }
    match f.name.as_str() {
        "viewDir" => return Role::ViewDir,
        "worldPos" => return Role::WorldPos,
        "worldNormal" => return Role::WorldNormal,
        "worldRefl" => return Role::WorldRefl,
        "screenPos" => return Role::ScreenPos,
        _ => {}
    }
    if let Some(i) = f.name.strip_prefix("internalSurfaceTtoW")
        && let Ok(i @ 0..=2) = i.parse::<usize>()
    {
        return Role::Internal(i);
    }
    if let Some(rest) = f.name.strip_prefix("uv") {
        let (set, texture) = match rest.chars().next() {
            Some(d @ '2'..='4') => (d as usize - '1' as usize, &rest[1..]),
            _ => (0, rest),
        };
        if texture.len() > 1 && texture.starts_with('_') {
            return Role::Uv(set, texture.to_string());
        }
    }
    Role::Custom
}

/// The shader's code with its `#pragma surface` lines commented out (lines keep their numbers).
fn comment_out_pragma(source: &str) -> String {
    source
        .split('\n')
        .map(|line| {
            let directive = line.trim_start().strip_prefix('#').map(str::trim_start);
            let surface = directive
                .and_then(|d| d.strip_prefix("pragma"))
                .is_some_and(|rest| rest.split_whitespace().next() == Some("surface"));
            if surface {
                format!("//{line}")
            } else {
                line.to_string()
            }
        })
        .collect::<Vec<_>>()
        .join("\n")
}

/// A top-level struct or function definition.
enum Item {
    /// The name and the body's tokens.
    Struct(String, Range<usize>),
    Function(Function),
}

struct Function {
    name: String,
    params: Vec<Param>,
    body: Range<usize>,
}

struct Param {
    ty: String,
    name: String,
    /// `out` or `inout`.
    writable: bool,
}

const MODIFIERS: [&str; 14] = [
    "in",
    "out",
    "inout",
    "uniform",
    "const",
    "static",
    "precise",
    "linear",
    "centroid",
    "nointerpolation",
    "noperspective",
    "sample",
    "inline",
    "volatile",
];

fn is_ident(t: Option<&Tok>) -> bool {
    t.is_some_and(|t| t.kind == Kind::Ident)
}

/// The index of the bracket closing the one at `open`.
fn closing(t: &[Tok], open: usize) -> Option<usize> {
    let (o, c) = match &*t[open].text {
        "(" => ("(", ")"),
        "{" => ("{", "}"),
        "[" => ("[", "]"),
        _ => return None,
    };
    let mut depth = 0usize;
    for (i, tok) in t.iter().enumerate().skip(open) {
        if tok.is(o) {
            depth += 1;
        } else if tok.is(c) {
            depth -= 1;
            if depth == 0 {
                return Some(i);
            }
        }
    }
    None
}

/// Tokens split at the commas outside brackets.
fn split_commas(t: &[Tok]) -> Vec<&[Tok]> {
    let mut parts = Vec::new();
    let (mut depth, mut start) = (0i32, 0);
    for (i, tok) in t.iter().enumerate() {
        match &*tok.text {
            "(" | "[" | "{" if tok.kind == Kind::Punct => depth += 1,
            ")" | "]" | "}" if tok.kind == Kind::Punct => depth -= 1,
            "," if tok.kind == Kind::Punct && depth == 0 => {
                parts.push(&t[start..i]);
                start = i + 1;
            }
            _ => {}
        }
    }
    if start < t.len() {
        parts.push(&t[start..]);
    }
    parts
}

/// The top-level struct and function definitions.
fn items(t: &[Tok]) -> Vec<Item> {
    let mut items = Vec::new();
    let mut depth = 0usize;
    let mut i = 0;
    while i < t.len() {
        let tok = &t[i];
        if tok.is("{") {
            depth += 1;
        } else if tok.is("}") {
            depth = depth.saturating_sub(1);
        } else if depth == 0 && tok.kind == Kind::Ident {
            if &*tok.text == "struct"
                && is_ident(t.get(i + 1))
                && t.get(i + 2).is_some_and(|t| t.is("{"))
                && let Some(end) = closing(t, i + 2)
            {
                items.push(Item::Struct(t[i + 1].text.to_string(), i + 3..end));
                i = end + 1;
                continue;
            }
            if i > 0
                && is_ident(t.get(i - 1))
                && t.get(i + 1).is_some_and(|t| t.is("("))
                && let Some(close) = closing(t, i + 1)
            {
                let mut open = close + 1;
                // A return value's semantic.
                if t.get(open).is_some_and(|t| t.is(":")) {
                    open += 2;
                }
                if t.get(open).is_some_and(|t| t.is("{"))
                    && let Some(end) = closing(t, open)
                {
                    items.push(Item::Function(Function {
                        name: tok.text.to_string(),
                        params: params(&t[i + 2..close]),
                        body: open + 1..end,
                    }));
                    i = end + 1;
                    continue;
                }
            }
        }
        i += 1;
    }
    items
}

fn params(t: &[Tok]) -> Vec<Param> {
    split_commas(t)
        .into_iter()
        .filter_map(|p| {
            let mut writable = false;
            let mut words = p.iter().take_while(|t| !t.is(":") && !t.is("="));
            let ty = loop {
                let w = words.next().filter(|t| t.kind == Kind::Ident)?;
                if !MODIFIERS.contains(&&*w.text) {
                    break w.text.to_string();
                }
                writable |= matches!(&*w.text, "out" | "inout");
            };
            let name = words.next().filter(|t| t.kind == Kind::Ident)?;
            Some(Param {
                ty,
                name: name.text.to_string(),
                writable,
            })
        })
        .collect()
}

/// A struct body's members.
fn fields(t: &[Tok]) -> Vec<Field> {
    let mut fields = Vec::new();
    for declaration in t.split(|t| t.is(";")) {
        let mut rest = declaration;
        while rest
            .first()
            .is_some_and(|t| t.kind == Kind::Ident && MODIFIERS.contains(&&*t.text))
        {
            rest = &rest[1..];
        }
        let Some((ty, declarators)) = rest.split_first() else {
            continue;
        };
        if ty.kind != Kind::Ident {
            continue;
        }
        for d in split_commas(declarators) {
            let Some(name) = d.first().filter(|t| t.kind == Kind::Ident) else {
                continue;
            };
            let semantic = d
                .iter()
                .position(|t| t.is(":"))
                .and_then(|c| d.get(c + 1))
                .map(|s| s.text.to_string());
            fields.push(Field {
                ty: ty.text.to_string(),
                name: name.text.to_string(),
                semantic,
            });
        }
    }
    fields
}

const ASSIGNMENTS: [&str; 6] = ["=", "+=", "-=", "*=", "/=", "%="];

/// Whether a function body writes `var.member`, itself or by handing `var` to an `out`/`inout`
/// parameter of a function that does.
fn writes(
    t: &[Tok],
    items: &[Item],
    body: &Range<usize>,
    var: &str,
    member: &str,
    depth: u32,
) -> bool {
    let b = &t[body.clone()];
    let text = |i: usize| b.get(i).map(|t| &*t.text);
    for i in 0..b.len() {
        if b[i].kind != Kind::Ident {
            continue;
        }
        if &*b[i].text == var && (i == 0 || !b[i - 1].is(".")) && text(i + 1) == Some(".") {
            if text(i + 2) == Some(member) {
                // Through swizzles and indices to the operator.
                let mut j = i + 3;
                loop {
                    if b.get(j).is_some_and(|t| t.is(".")) {
                        j += 2;
                    } else if b.get(j).is_some_and(|t| t.is("[")) {
                        j = closing(b, j).map_or(b.len(), |c| c + 1);
                    } else {
                        break;
                    }
                }
                if b.get(j)
                    .is_some_and(|t| ASSIGNMENTS.iter().any(|a| t.is(a)))
                {
                    return true;
                }
            }
            continue;
        }
        if depth < 4
            && b.get(i + 1).is_some_and(|t| t.is("("))
            && let Some(close) = closing(b, i + 1)
        {
            for (k, argument) in split_commas(&b[i + 2..close]).iter().enumerate() {
                if !matches!(argument, [a] if &*a.text == var) {
                    continue;
                }
                let callee = items.iter().any(|item| match item {
                    Item::Function(f) if *f.name == *b[i].text => {
                        f.params.get(k).is_some_and(|p| {
                            p.writable && writes(t, items, &f.body, &p.name, member, depth + 1)
                        })
                    }
                    _ => false,
                });
                if callee {
                    return true;
                }
            }
        }
    }
    false
}

#[cfg(test)]
mod tests {
    use super::*;

    fn words(line: &str) -> Vec<String> {
        line.split_whitespace().map(str::to_string).collect()
    }

    #[test]
    fn options_shape_the_pass() {
        let s = Surface::parse(&words("surf Standard alpha vertex:vert nofog")).unwrap();
        assert_eq!(s.alpha, Alpha::Premultiplied);
        assert!(s.keep_alpha && !s.fog);
        assert_eq!(s.vertex.as_deref(), Some("vert"));
        let s = Surface::parse(&words("surf Lambert alpha alphatest:_Cutoff")).unwrap();
        assert_eq!(s.alpha, Alpha::Fade);
        assert_eq!(s.alpha_test.as_deref(), Some("_Cutoff"));
        let s = Surface::parse(&words("surf Lambert alphatest:_Cutoff noforwardadd")).unwrap();
        assert!(!s.keep_alpha);
        assert!(Surface::parse(&words("surf")).is_err());
        let error = Surface::parse(&words("surf Lambert tessellate:tess"))
            .unwrap_err()
            .to_string();
        assert!(error.contains("tessellation"), "{error}");
    }

    #[test]
    fn the_surface_pragma_is_commented_out() {
        let source = "a\n  #  pragma surface surf Lambert\n#pragma surfaceX\n#pragma target 3.0";
        assert_eq!(
            comment_out_pragma(source),
            "a\n//  #  pragma surface surf Lambert\n#pragma surfaceX\n#pragma target 3.0"
        );
    }

    /// The analysis on code as it reads once preprocessed (no Unity install needed).
    const CODE: &str = r#"
struct SurfaceOutput { fixed3 Albedo; fixed3 Normal; fixed3 Emission; half Specular; fixed Gloss; fixed Alpha; };
struct appdata_full { float4 vertex : POSITION; float4 tangent : TANGENT; float3 normal : NORMAL; float4 texcoord : TEXCOORD0; float4 texcoord1 : TEXCOORD1; fixed4 color : COLOR; };
inline fixed4 LightingLambert (SurfaceOutput s, UnityGI gi) { return 0; }
inline void LightingLambert_GI (SurfaceOutput s, UnityGIInput data, inout UnityGI gi) { }
inline fixed4 LightingWrap (SurfaceOutput s, fixed3 lightDir, fixed atten) { return 0; }
sampler2D _MainTex;
float4 _MainTex_ST;
struct Input { float2 uv_MainTex; float2 uv2_Detail; fixed4 tint : COLOR; float3 viewDir; float3 worldRefl; half3 internalSurfaceTtoW0; half3 internalSurfaceTtoW1; half3 internalSurfaceTtoW2; float2 param, extra; };
void vert (inout appdata_full v, out Input o) { o.param = v.texcoord.zw; }
void bump (inout SurfaceOutput s, float3 n) { s.Normal.xy = n.xy; }
void surf (Input IN, inout SurfaceOutput o) { o.Albedo = tex2D(_MainTex, IN.uv_MainTex).rgb; bump(o, float3(0, 0, 1)); }
void flat (Input IN, inout SurfaceOutput o) { o.Albedo = o.Normal; }
"#;

    #[test]
    fn the_code_is_analysed() {
        let s = Surface::parse(&words("surf Lambert vertex:vert alphatest:_Cutoff")).unwrap();
        let a = s.analyse(CODE).unwrap();
        assert_eq!(
            (a.input.as_str(), a.output.as_str()),
            ("Input", "SurfaceOutput")
        );
        assert_eq!(a.lighting, Lighting::Gi { view: false });
        // `o.Normal` written through a helper's inout parameter.
        assert!(a.tangent_normal);
        assert!(a.custom_data && a.appdata == "appdata_full");
        assert_eq!(
            a.input_fields
                .iter()
                .map(|f| f.name.as_str())
                .collect::<Vec<_>>(),
            [
                "uv_MainTex",
                "uv2_Detail",
                "tint",
                "viewDir",
                "worldRefl",
                "internalSurfaceTtoW0",
                "internalSurfaceTtoW1",
                "internalSurfaceTtoW2",
                "param",
                "extra"
            ]
        );
        assert!(matches!(role(&a.input_fields[1]), Role::Uv(1, ref t) if t == "_Detail"));
        assert!(matches!(role(&a.input_fields[2]), Role::Color));
        // `_MainTex_ST` is the shader's own; the others are declared by the pass.
        assert_eq!(a.declare, ["float4 _Detail_ST;", "fixed _Cutoff;"]);

        let pass = s.pass(&a, false);
        for expected in [
            "o.pack0.xy = TRANSFORM_TEX(v.texcoord, _MainTex);",
            "o.pack1.xy = TRANSFORM_TEX(v.texcoord1, _Detail);",
            "o.pack2 = v.color;",
            "vert (v, customInputData);",
            "o.pack8 = customInputData.param;",
            "surfIN.internalSurfaceTtoW1 = _unity_tbn_1;",
            "surfIN.worldRefl = -worldViewDir;",
            "o.lmap = v.texcoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw;",
            "giInput.lightmapUV.xy = IN.lmap;",
            "LightingLambert_GI(o, giInput, gi);",
            "c += LightingLambert (o, gi);",
            "clip (o.Alpha - _Cutoff);",
            "UNITY_OPAQUE_ALPHA(c.a);",
            "UNITY_APPLY_FOG(_unity_fogCoord, c);",
        ] {
            assert!(pass.contains(expected), "{expected}:\n{pass}");
        }

        // The forward add pass: the one light, attenuated; no ambient, emission or GI.
        let add = s.pass(&a, true);
        for expected in [
            "UNITY_LIGHTING_COORDS(",
            "gi.light.color *= atten;",
            "c += LightingLambert (o, gi);",
            "UNITY_APPLY_FOG_COLOR(_unity_fogCoord, c, fixed4(0,0,0,0));",
        ] {
            assert!(add.contains(expected), "{expected}:\n{add}");
        }
        assert!(!add.contains("LightingLambert_GI") && !add.contains("ShadeSH"));
        assert!(
            !add.contains("lmap"),
            "forward add must not repeat baked GI"
        );
        let mut no_uv2 = s.analyse(CODE).unwrap();
        no_uv2.appdata_members.retain(|m| m != "texcoord1");
        assert!(
            s.pass(&no_uv2, false)
                .contains("o.lmap = float2(0,0) * unity_LightmapST.xy")
        );

        // Old-style lighting, no normal written, no vertex modifier.
        let s = Surface::parse(&words("flat Wrap keepalpha nofog")).unwrap();
        let a = s.analyse(CODE).unwrap();
        assert_eq!(a.lighting, Lighting::Old { view: false });
        assert!(!a.tangent_normal && !a.custom_data);
        let pass = s.pass(&a, false);
        for expected in [
            "c = LightingWrap (o, lightDir, atten);",
            "c.rgb += o.Albedo * IN.vlight;",
            "surfIN.worldRefl = reflect(-worldViewDir, IN.worldNormal);",
            "o.Normal = IN.worldNormal;",
        ] {
            assert!(pass.contains(expected), "{expected}:\n{pass}");
        }
        assert!(!pass.contains("UNITY_OPAQUE_ALPHA") && !pass.contains("FOG"));
        // Custom members without a vertex modifier filling them are not interpolated.
        assert!(!pass.contains("param"), "{pass}");

        let missing = Surface::parse(&words("surf Missing")).unwrap();
        let error = missing.analyse(CODE).unwrap_err().to_string();
        assert!(error.contains("LightingMissing"), "{error}");
    }
}
