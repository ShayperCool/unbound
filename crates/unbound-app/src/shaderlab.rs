//! A reader for the parts of ShaderLab the renderer needs: a shader's name, its properties'
//! defaults, `Fallback`, and the passes of the SubShader the built-in render pipeline runs: each
//! one's render states (Blend, BlendOp, ZWrite, ZTest, Cull, ColorMask, Offset, Stencil, Tags),
//! its program (CG/HLSL, a surface shader's, or the fixed-function commands). State values may be
//! literals or `[_Property]` references, resolved per material.
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
};
pub use unbound_shaders::fixed::{ColorValue, FixedFunction, LightingMaterial, TextureStage};

#[derive(Debug, Clone, PartialEq)]
enum Token {
    Word(String),
    Str(String),
    Num(f32),
    /// `[_Name]`
    Prop(String),
    Open,
    Close,
    LParen,
    RParen,
    Comma,
    Eq,
}

/// A program block's code (`CGPROGRAM` ... `ENDCG` and friends).
#[derive(Debug, Clone)]
struct Block {
    /// `CGINCLUDE`/`HLSLINCLUDE`: code for every program after it.
    include: bool,
    /// CG (`CGPROGRAM`, `CGINCLUDE`) rather than HLSL.
    cg: bool,
    /// `GLSLPROGRAM`: not compiled.
    glsl: bool,
    code: String,
}

/// Placeholder words for program blocks in the stripped text.
const BLOCK_WORD: &str = "__unbound_block_";

/// Removes comments, and replaces program blocks by a placeholder word (`__unbound_block_N`).
fn strip(source: &str) -> String {
    strip_blocks(source).0
}

const PROGRAM_OPEN: [&str; 5] = [
    "CGPROGRAM",
    "CGINCLUDE",
    "HLSLPROGRAM",
    "HLSLINCLUDE",
    "GLSLPROGRAM",
];
const PROGRAM_CLOSE: [&str; 3] = ["ENDCG", "ENDHLSL", "ENDGLSL"];

/// The first of `words` in `line` as a whole word (before a `//` comment when `code_only`):
/// its byte offset and the word.
fn find_word<'w>(line: &str, words: &[&'w str], code_only: bool) -> Option<(usize, &'w str)> {
    let end = if code_only {
        line.find("//").unwrap_or(line.len())
    } else {
        line.len()
    };
    let ident = |c: Option<char>| c.is_some_and(|c| c.is_ascii_alphanumeric() || c == '_');
    words
        .iter()
        .filter_map(|w| {
            let mut from = 0;
            while let Some(at) = line[from..end].find(w) {
                let at = from + at;
                if !ident(line[..at].chars().next_back())
                    && !ident(line[at + w.len()..].chars().next())
                {
                    return Some((at, *w));
                }
                from = at + w.len();
            }
            None
        })
        .min_by_key(|(at, _)| *at)
}

/// Puts program keywords that share a line with other text (`Pass { CGPROGRAM`, `ENDCG }`) on
/// lines of their own: ShaderLab reads them anywhere.
fn program_keywords_on_own_lines(source: &str) -> std::borrow::Cow<'_, str> {
    let mut out = String::with_capacity(source.len());
    let mut in_program = false;
    let mut changed = false;
    for line in source.lines() {
        let mut rest = line;
        let mut split = false;
        loop {
            let words: &[&str] = if in_program {
                &PROGRAM_CLOSE
            } else {
                &PROGRAM_OPEN
            };
            let Some((at, word)) = find_word(rest, words, !in_program) else {
                break;
            };
            let before = &rest[..at];
            if !before.trim().is_empty() {
                out.push_str(before);
                out.push('\n');
                changed = true;
            }
            out.push_str(word);
            out.push('\n');
            in_program = !in_program;
            rest = &rest[at + word.len()..];
            split = true;
        }
        if !split {
            out.push_str(line);
            out.push('\n');
        } else if !rest.trim().is_empty() {
            out.push_str(rest);
            out.push('\n');
            changed = true;
        }
    }
    if changed {
        std::borrow::Cow::Owned(out)
    } else {
        std::borrow::Cow::Borrowed(source)
    }
}

fn strip_blocks(source: &str) -> (String, Vec<Block>) {
    let source = program_keywords_on_own_lines(source);
    let source = source.as_ref();
    let mut out = String::with_capacity(source.len());
    let mut blocks: Vec<Block> = Vec::new();
    let mut in_program = false;
    let mut in_block_comment = false;
    for line in source.lines() {
        let trimmed = line.trim();
        if in_program {
            if trimmed.starts_with("ENDCG")
                || trimmed.starts_with("ENDHLSL")
                || trimmed.starts_with("ENDGLSL")
            {
                in_program = false;
            } else if let Some(block) = blocks.last_mut() {
                block.code.push_str(line);
                block.code.push('\n');
            }
            out.push('\n');
            continue;
        }
        let opening = [
            "CGPROGRAM",
            "CGINCLUDE",
            "HLSLPROGRAM",
            "HLSLINCLUDE",
            "GLSLPROGRAM",
        ]
        .into_iter()
        .find(|k| trimmed.starts_with(k));
        if let Some(keyword) = opening {
            in_program = true;
            out.push_str(&format!("{BLOCK_WORD}{}\n", blocks.len()));
            blocks.push(Block {
                include: keyword.ends_with("INCLUDE"),
                cg: keyword.starts_with("CG"),
                glsl: keyword.starts_with("GLSL"),
                code: String::new(),
            });
            continue;
        }
        let mut chars = line.chars().peekable();
        let mut in_string = false;
        while let Some(c) = chars.next() {
            if in_block_comment {
                if c == '*' && chars.peek() == Some(&'/') {
                    chars.next();
                    in_block_comment = false;
                }
                continue;
            }
            if in_string {
                out.push(c);
                if c == '"' {
                    in_string = false;
                }
                continue;
            }
            match c {
                '"' => {
                    in_string = true;
                    out.push(c);
                }
                '/' if chars.peek() == Some(&'/') => break,
                '/' if chars.peek() == Some(&'*') => {
                    chars.next();
                    in_block_comment = true;
                }
                _ => out.push(c),
            }
        }
        out.push('\n');
    }
    (out, blocks)
}

fn tokenize_text(text: &str) -> Vec<Token> {
    let mut tokens = Vec::new();
    let chars: Vec<char> = text.chars().collect();
    let mut i = 0;
    while i < chars.len() {
        let c = chars[i];
        match c {
            c if c.is_whitespace() || c == '\u{feff}' => i += 1,
            '{' => {
                tokens.push(Token::Open);
                i += 1;
            }
            '}' => {
                tokens.push(Token::Close);
                i += 1;
            }
            '(' => {
                tokens.push(Token::LParen);
                i += 1;
            }
            ')' => {
                tokens.push(Token::RParen);
                i += 1;
            }
            ',' => {
                tokens.push(Token::Comma);
                i += 1;
            }
            '=' => {
                tokens.push(Token::Eq);
                i += 1;
            }
            '"' => {
                let start = i + 1;
                let mut j = start;
                while j < chars.len() && chars[j] != '"' {
                    j += 1;
                }
                tokens.push(Token::Str(
                    chars[start..j.min(chars.len())].iter().collect(),
                ));
                i = j + 1;
            }
            '[' => {
                let start = i + 1;
                let mut j = start;
                while j < chars.len() && chars[j] != ']' {
                    j += 1;
                }
                let inner: String = chars[start..j.min(chars.len())].iter().collect();
                tokens.push(Token::Prop(inner.trim().to_string()));
                i = j + 1;
            }
            c if c.is_ascii_digit() || c == '-' || c == '.' || c == '+' => {
                let start = i;
                let mut j = i + 1;
                while j < chars.len()
                    && (chars[j].is_ascii_alphanumeric() || chars[j] == '.' || chars[j] == '-')
                {
                    j += 1;
                }
                let s: String = chars[start..j].iter().collect();
                match s.parse::<f32>() {
                    Ok(n) => tokens.push(Token::Num(n)),
                    // `2D`, `3D`: property types.
                    Err(_) => tokens.push(Token::Word(s)),
                }
                i = j;
            }
            _ => {
                let start = i;
                let mut j = i + 1;
                while j < chars.len()
                    && !chars[j].is_whitespace()
                    && !matches!(
                        chars[j],
                        '{' | '}' | '(' | ')' | ',' | '=' | '"' | '[' | ']'
                    )
                {
                    j += 1;
                }
                tokens.push(Token::Word(chars[start..j].iter().collect()));
                i = j;
            }
        }
    }
    tokens
}

/// A literal or a material property reference.
#[derive(Debug, Clone, PartialEq)]
pub enum Value {
    Word(String),
    Num(f32),
    Prop(String),
}

#[derive(Debug, Clone, PartialEq)]
pub enum PropertyDefault {
    Float(f32),
    Vector([f32; 4]),
    /// The default texture's name: `white`, `black`, `gray`, `bump`, `red`, ...
    Texture(String),
}

#[derive(Debug, Clone, Default, PartialEq)]
pub struct Stencil {
    pub reference: Option<Value>,
    pub comp: Option<Value>,
    pub pass: Option<Value>,
    pub fail: Option<Value>,
    pub zfail: Option<Value>,
    pub read_mask: Option<Value>,
    pub write_mask: Option<Value>,
}

/// Render states as written; `None` keeps the enclosing (or Unity's default) state.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct States {
    /// `Off` or `src dst [, src_alpha dst_alpha]`.
    pub blend: Option<Vec<Value>>,
    pub blend_op: Option<Vec<Value>>,
    pub zwrite: Option<Value>,
    pub ztest: Option<Value>,
    pub cull: Option<Value>,
    pub color_mask: Option<Value>,
    pub stencil: Option<Stencil>,
    /// `Offset factor, units`.
    pub offset: Option<[Value; 2]>,
    /// `AlphaToMask On|Off` (or a property).
    pub alpha_to_mask: Option<Value>,
    /// `ZClip True|False` (or a property).
    pub zclip: Option<Value>,
    pub tags: HashMap<String, String>,
    /// Fixed-function setup (used by passes without a program).
    pub fixed: FixedStates,
}

/// The fixed-function commands; SubShader-wide ones are inherited by its passes.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct FixedStates {
    pub lighting: Option<bool>,
    pub material: Option<LightingMaterial>,
    pub separate_specular: Option<bool>,
    pub color_material: Option<String>,
    pub color: Option<ColorValue>,
    /// `AlphaTest Off` is `Some(None)`.
    pub alpha_test: Option<Option<(String, ColorValue)>>,
    pub bindings: Option<Vec<(String, String)>>,
    /// The pass's `SetTexture` stages.
    pub stages: Vec<TextureStage>,
}

impl States {
    /// `other` on top of `self`.
    fn overlay(&self, other: &States) -> States {
        let mut tags = self.tags.clone();
        tags.extend(other.tags.iter().map(|(k, v)| (k.clone(), v.clone())));
        let (a, b) = (&self.fixed, &other.fixed);
        States {
            blend: other.blend.clone().or_else(|| self.blend.clone()),
            blend_op: other.blend_op.clone().or_else(|| self.blend_op.clone()),
            zwrite: other.zwrite.clone().or_else(|| self.zwrite.clone()),
            ztest: other.ztest.clone().or_else(|| self.ztest.clone()),
            cull: other.cull.clone().or_else(|| self.cull.clone()),
            color_mask: other.color_mask.clone().or_else(|| self.color_mask.clone()),
            stencil: other.stencil.clone().or_else(|| self.stencil.clone()),
            offset: other.offset.clone().or_else(|| self.offset.clone()),
            alpha_to_mask: other
                .alpha_to_mask
                .clone()
                .or_else(|| self.alpha_to_mask.clone()),
            zclip: other.zclip.clone().or_else(|| self.zclip.clone()),
            tags,
            fixed: FixedStates {
                lighting: b.lighting.or(a.lighting),
                material: b.material.clone().or_else(|| a.material.clone()),
                separate_specular: b.separate_specular.or(a.separate_specular),
                color_material: b
                    .color_material
                    .clone()
                    .or_else(|| a.color_material.clone()),
                color: b.color.clone().or_else(|| a.color.clone()),
                alpha_test: b.alpha_test.clone().or_else(|| a.alpha_test.clone()),
                bindings: b.bindings.clone().or_else(|| a.bindings.clone()),
                stages: if b.stages.is_empty() {
                    a.stages.clone()
                } else {
                    b.stages.clone()
                },
            },
        }
    }

    /// The fixed-function program these states describe.
    pub fn fixed_function(&self) -> FixedFunction {
        let f = &self.fixed;
        FixedFunction {
            lighting: f.lighting.unwrap_or(false),
            vertex_lights: self
                .tags
                .get("LightMode")
                .is_some_and(|m| m.eq_ignore_ascii_case("Vertex")),
            material: f.material.clone().unwrap_or_default(),
            separate_specular: f.separate_specular.unwrap_or(false),
            color_material: f.color_material.clone(),
            color: f.color.clone(),
            alpha_test: f.alpha_test.clone().flatten(),
            stages: f.stages.clone(),
            bindings: f.bindings.clone().unwrap_or_default(),
        }
    }
}

#[derive(Debug, Clone, Default)]
pub struct PassInfo {
    /// `Name "..."` (empty when unnamed).
    pub name: String,
    /// The SubShader's states with the pass's on top.
    pub states: States,
    /// `UsePass "Shader/PASS"`: the pass is that one (resolved by [`ShaderIndex`]).
    pub use_pass: Option<String>,
    /// The pass's program (with the include blocks before it), when it has a CG or HLSL one;
    /// passes without one are fixed-function.
    pub program: Option<PassProgram>,
    /// A `GrabPass`: the texture it grabs into (`_GrabTexture` when it names none, grabbed
    /// for every object; a named one once a frame).
    pub grab: Option<String>,
    /// A `GLSLPROGRAM` pass (not compiled).
    pub glsl: bool,
}

/// A pass's program code.
#[derive(Debug, Clone, PartialEq)]
pub struct PassProgram {
    /// The `CGINCLUDE`/`HLSLINCLUDE` blocks in scope, then the program.
    pub code: String,
    /// `CGPROGRAM` (rather than `HLSLPROGRAM`).
    pub cg: bool,
    /// The `.shader` file it is in (a `UsePass` takes another shader's).
    pub file: PathBuf,
    /// A surface shader's program (Unity generates the pass from it).
    pub surface: bool,
}

/// How a pass takes part in built-in forward rendering, by its `LightMode` tag.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum LightMode {
    /// No tag, or `Always`.
    Always,
    ForwardBase,
    ForwardAdd,
    /// `Vertex` (legacy vertex lit): drawn when the SubShader has no forward passes.
    Vertex,
    /// Shadow casters, deferred, meta, lightmapped vertex lit, ...: not drawn.
    Other,
}

impl PassInfo {
    pub fn light_mode(&self) -> LightMode {
        match self.states.tags.get("LightMode") {
            None => {
                let name = self
                    .use_pass
                    .as_deref()
                    .unwrap_or(&self.name)
                    .to_ascii_uppercase();
                if name.ends_with("CASTER") || name.ends_with("META") {
                    LightMode::Other
                } else {
                    LightMode::Always
                }
            }
            Some(mode) => match mode.to_ascii_lowercase().as_str() {
                "always" | "" => LightMode::Always,
                "forwardbase" => LightMode::ForwardBase,
                "forwardadd" => LightMode::ForwardAdd,
                "vertex" => LightMode::Vertex,
                _ => LightMode::Other,
            },
        }
    }
}

/// How a SubShader is picked besides the render pipeline: a maximum LOD (`Shader.maximumLOD`,
/// `Shader.globalMaximumLOD`) or a replacement tag. The managed side names such a selection by
/// suffixes of the shader's name: `Name|lod=200`, `Name|tag=RenderType=Opaque`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Selection {
    pub max_lod: Option<u32>,
    pub tag: Option<(String, String)>,
    /// The Vertex Lit rendering path's pick (`Name|vertexlit`): the first SubShader with a pass
    /// it draws (`Vertex` or untagged), else the fallback's.
    pub vertex_lit: bool,
}

impl Selection {
    /// Splits `Name|lod=N|tag=K=V` into the shader's name and the selection.
    pub fn parse(name: &str) -> (&str, Selection) {
        let mut parts = name.split('|');
        let base = parts.next().unwrap_or(name);
        let mut selection = Selection::default();
        for part in parts {
            if let Some(lod) = part.strip_prefix("lod=") {
                selection.max_lod =
                    Some(lod.parse::<i64>().unwrap_or(0).clamp(0, u32::MAX as i64) as u32);
            } else if let Some(tag) = part.strip_prefix("tag=")
                && let Some((key, value)) = tag.split_once('=')
            {
                selection.tag = Some((key.to_string(), value.to_string()));
            } else if part == "vertexlit" {
                selection.vertex_lit = true;
            }
        }
        (base, selection)
    }

    /// The suffix a fallback shader is looked up with: the same maximum LOD (a replacement does
    /// not fall back).
    pub fn fallback_suffix(&self) -> Option<String> {
        if self.tag.is_some() {
            return None;
        }
        let mut suffix = match self.max_lod {
            Some(lod) => format!("|lod={lod}"),
            None => String::new(),
        };
        if self.vertex_lit {
            suffix.push_str("|vertexlit");
        }
        Some(suffix)
    }
}

#[derive(Debug, Clone, Default)]
pub struct ShaderLab {
    pub name: String,
    pub properties: Vec<(String, PropertyDefault)>,
    /// Every pass of the SubShader the active render pipeline uses ([`ShaderLab::select`]; the
    /// built-in one's when parsed); surface shaders: their generated forward pass.
    pub passes: Vec<PassInfo>,
    /// That SubShader's tags.
    pub tags: HashMap<String, String>,
    /// Whether a SubShader was found for the render pipeline.
    pub has_subshader: bool,
    /// Every SubShader, in order.
    pub subshaders: Vec<SubShader>,
    /// `Fallback "Name"`.
    pub fallback: Option<String>,
    /// `Dependency "Name" = "Shader"` lines (terrain shaders' add pass and base map shaders).
    pub dependencies: Vec<(String, String)>,
    /// Properties whose defaults convert to linear: non-HDR Color, or `[Gamma]`.
    pub colors: std::collections::HashSet<String>,
    /// Float and color properties marked `[Gamma]`.
    pub gamma: std::collections::HashSet<String>,
    /// The `.shader` file.
    pub path: PathBuf,
}

impl ShaderLab {
    pub fn property(&self, name: &str) -> Option<&PropertyDefault> {
        self.properties
            .iter()
            .find(|(n, _)| n == name)
            .map(|(_, v)| v)
    }

    /// Picks the SubShader a render pipeline runs: the first whose `RenderPipeline` tag is the
    /// pipeline's (`Shader.globalRenderPipeline`) or absent; the built-in pipeline (empty tag)
    /// runs only untagged ones.
    pub fn select(&mut self, pipeline: &str) {
        self.select_with(pipeline, &Selection::default());
    }

    /// [`ShaderLab::select`] under a maximum LOD (SubShaders above it are skipped) or for a
    /// replacement tag (the first SubShader whose tag has the value; none: no SubShader).
    pub fn select_with(&mut self, pipeline: &str, selection: &Selection) {
        let pipeline = pipeline.trim();
        // The screen-space shadow collector's SubShaders, one per filter (`ShadowmapFilter`), are
        // all available, as passes in order: the engine picks hard or soft by pass.
        if !self.subshaders.is_empty()
            && self
                .subshaders
                .iter()
                .all(|sub| sub.tags.contains_key("ShadowmapFilter"))
        {
            self.has_subshader = true;
            self.tags = self.subshaders[0].tags.clone();
            self.passes = self
                .subshaders
                .iter()
                .flat_map(|sub| sub.passes.clone())
                .collect();
            return;
        }
        let found = self.subshaders.iter().find(|sub| {
            let runs = match sub.tags.get("RenderPipeline").map(|t| t.trim()) {
                None | Some("") => true,
                Some(tag) => !pipeline.is_empty() && tag == pipeline,
            };
            let vertex_lit = !selection.vertex_lit
                || sub
                    .passes
                    .iter()
                    .any(|p| matches!(p.light_mode(), LightMode::Vertex | LightMode::Always));
            runs && vertex_lit
                && selection.max_lod.is_none_or(|max| sub.lod <= max)
                && selection
                    .tag
                    .as_ref()
                    .is_none_or(|(key, value)| sub.tags.get(key).is_some_and(|v| v == value))
        });
        match found {
            Some(sub) => {
                self.has_subshader = true;
                self.tags = sub.tags.clone();
                self.passes = sub.passes.clone();
            }
            None => {
                self.has_subshader = false;
                self.tags = HashMap::new();
                self.passes = Vec::new();
            }
        }
    }

    /// The pass a renderer list draws for a `LightMode` tag: the first whose tag it is (an
    /// untagged pass is `SRPDefaultUnlit`).
    pub fn tagged_pass(&self, light_mode: &str) -> Option<usize> {
        self.passes.iter().position(|p| {
            if p.grab.is_some() {
                return false;
            }
            match p.states.tags.get("LightMode") {
                Some(tag) => tag == light_mode,
                None => light_mode == "SRPDefaultUnlit",
            }
        })
    }

    pub fn parse(source: &str) -> Option<ShaderLab> {
        Self::parse_file(source, Path::new(""))
    }

    /// Parses the shader in `path` (whose programs' includes resolve from its directory).
    pub fn parse_file(source: &str, path: &Path) -> Option<ShaderLab> {
        let (text, blocks) = strip_blocks(source);
        let tokens = tokenize_text(&text);
        let mut p = Parser {
            t: &tokens,
            i: 0,
            blocks: &blocks,
            includes: Vec::new(),
            path,
        };
        // `Shader "Name" {`
        loop {
            match p.next()? {
                Token::Word(w) if w == "Shader" => break,
                _ => continue,
            }
        }
        let Token::Str(name) = p.next()? else {
            return None;
        };
        if p.next()? != Token::Open {
            return None;
        }
        let mut shader = ShaderLab {
            name,
            path: path.to_path_buf(),
            ..Default::default()
        };
        p.body(&mut shader, &States::default());
        Some(shader)
    }

    /// The passes drawn in built-in forward rendering, in order: `Always` and `ForwardBase`
    /// passes (`Vertex` ones when there are no forward passes). `ForwardAdd` passes draw per
    /// additional pixel light, which Unbound does not have.
    pub fn forward_passes(&self) -> Vec<usize> {
        let forward = self.passes.iter().any(|p| {
            matches!(
                p.light_mode(),
                LightMode::ForwardBase | LightMode::ForwardAdd
            )
        });
        self.passes
            .iter()
            .enumerate()
            .filter(|(_, p)| match p.light_mode() {
                LightMode::Always | LightMode::ForwardBase => true,
                LightMode::Vertex => !forward,
                _ => false,
            })
            .map(|(i, _)| i)
            .collect()
    }
}

/// The states a surface shader's `#pragma surface` options give its generated forward pass:
/// alpha blending (premultiplied with physically based lighting), decals.
fn surface_states(code: &str) -> States {
    let mut states = States::default();
    let Some(line) = code
        .lines()
        .map(str::trim)
        .find(|l| l.starts_with("#pragma") && l.split_whitespace().nth(1) == Some("surface"))
    else {
        return states;
    };
    let words: Vec<&str> = line.split_whitespace().collect();
    let model = words.get(3).copied().unwrap_or("");
    let physical = model.starts_with("Standard");
    let word = |w: &str| Value::Word(w.into());
    let fade = vec![word("SrcAlpha"), word("OneMinusSrcAlpha")];
    let premul = vec![word("One"), word("OneMinusSrcAlpha")];
    for option in words.iter().skip(4) {
        let blend = match *option {
            "alpha" | "alpha:auto" if physical => Some(premul.clone()),
            "alpha" | "alpha:auto" | "alpha:fade" | "alpha:blend" | "decal:blend" => {
                Some(fade.clone())
            }
            "alpha:premul" => Some(premul.clone()),
            "decal:add" => Some(vec![word("One"), word("One")]),
            _ => None,
        };
        if let Some(blend) = blend {
            // Unity's generated pass: `ZWrite Off Blend ... ColorMask RGB`.
            states.blend = Some(blend);
            states.zwrite = Some(word("Off"));
            states.color_mask = Some(word("RGB"));
            if option.starts_with("decal") {
                states.offset = Some([Value::Num(-1.0), Value::Num(-1.0)]);
            }
        }
    }
    states.tags.insert("LightMode".into(), "ForwardBase".into());
    states
}

/// The states of a surface shader's generated forward add pass (None with `noforwardadd`):
/// additive (`Blend One One`, `SrcAlpha One` when alpha fades), no depth writes.
fn surface_add_states(code: &str) -> Option<States> {
    let line = code
        .lines()
        .map(str::trim)
        .find(|l| l.starts_with("#pragma") && l.split_whitespace().nth(1) == Some("surface"))?;
    let words: Vec<&str> = line.split_whitespace().collect();
    if words.iter().skip(4).any(|w| *w == "noforwardadd") {
        return None;
    }
    let model = words.get(3).copied().unwrap_or("");
    let physical = model.starts_with("Standard");
    let word = |w: &str| Value::Word(w.into());
    let fade = words.iter().skip(4).any(|w| {
        matches!(*w, "alpha:fade" | "alpha:blend" | "decal:blend")
            || (!physical && matches!(*w, "alpha" | "alpha:auto"))
    });
    let mut states = States::default();
    states.blend = Some(if fade {
        vec![word("SrcAlpha"), word("One")]
    } else {
        vec![word("One"), word("One")]
    });
    states.zwrite = Some(word("Off"));
    states.tags.insert("LightMode".into(), "ForwardAdd".into());
    Some(states)
}

/// A ShaderLab command as Unbound matches it: Unity reads them case-insensitively (the screen-space
/// shadow collector writes `Subshader`).
fn command(word: &str) -> &str {
    const KNOWN: [&str; 31] = [
        "Properties",
        "SubShader",
        "Category",
        "Fallback",
        "Pass",
        "UsePass",
        "GrabPass",
        "Tags",
        "Blend",
        "BlendOp",
        "ZWrite",
        "ZTest",
        "Cull",
        "Offset",
        "ColorMask",
        "Stencil",
        "Lighting",
        "SeparateSpecular",
        "ColorMaterial",
        "Color",
        "AlphaTest",
        "Material",
        "BindChannels",
        "SetTexture",
        "Fog",
        "Name",
        "LOD",
        "ZClip",
        "AlphaToMask",
        "Conservative",
        "CustomEditor",
    ];
    KNOWN
        .iter()
        .find(|k| k.eq_ignore_ascii_case(word))
        .copied()
        .unwrap_or(word)
}

/// A tag's name as Unbound looks it up: Unity's tag names are case-insensitive (the legacy
/// VertexLit shader writes `"LIGHTMODE"`).
fn canonical_tag(name: String) -> String {
    const KNOWN: [&str; 9] = [
        "LightMode",
        "Queue",
        "RenderType",
        "RenderPipeline",
        "IgnoreProjector",
        "DisableBatching",
        "ForceNoShadowCasting",
        "CanUseSpriteAtlas",
        "PreviewType",
    ];
    KNOWN
        .iter()
        .find(|k| k.eq_ignore_ascii_case(&name))
        .map(|k| k.to_string())
        .unwrap_or(name)
}

struct Parser<'a> {
    t: &'a [Token],
    i: usize,
    blocks: &'a [Block],
    /// Include blocks in scope (the shader's, then the SubShader's).
    includes: Vec<usize>,
    path: &'a Path,
}

/// A parsed SubShader, before one is chosen.
/// A SubShader: its tags and passes.
#[derive(Debug, Clone, Default)]
pub struct SubShader {
    pub tags: HashMap<String, String>,
    pub passes: Vec<PassInfo>,
    /// `LOD n` (0 when it has none).
    pub lod: u32,
}

impl Parser<'_> {
    /// The program block a placeholder word stands for.
    fn block(&self, word: &str) -> Option<usize> {
        word.strip_prefix(BLOCK_WORD)?
            .parse()
            .ok()
            .filter(|&i| i < self.blocks.len())
    }

    /// A pass's program: the include blocks in scope, then the program block.
    fn program(&self, index: usize) -> Option<PassProgram> {
        let block = &self.blocks[index];
        if block.glsl || block.include {
            return None;
        }
        let mut code = String::new();
        for &i in &self.includes {
            if self.blocks[i].cg == block.cg {
                code.push_str(&self.blocks[i].code);
            }
        }
        code.push_str(&block.code);
        Some(PassProgram {
            code,
            cg: block.cg,
            file: self.path.to_path_buf(),
            surface: false,
        })
    }

    fn next(&mut self) -> Option<Token> {
        let t = self.t.get(self.i).cloned();
        self.i += 1;
        t
    }

    fn peek(&self) -> Option<&Token> {
        self.t.get(self.i)
    }

    /// Skips a `{ ... }` block whose `{` is next.
    fn skip_block(&mut self) {
        if self.peek() != Some(&Token::Open) {
            return;
        }
        let mut depth = 0;
        while let Some(t) = self.next() {
            match t {
                Token::Open => depth += 1,
                Token::Close => {
                    depth -= 1;
                    if depth == 0 {
                        return;
                    }
                }
                _ => {}
            }
        }
    }

    fn value(&mut self) -> Option<Value> {
        match self.peek()? {
            Token::Word(w) => {
                let v = Value::Word(w.clone());
                self.i += 1;
                Some(v)
            }
            Token::Num(n) => {
                let v = Value::Num(*n);
                self.i += 1;
                Some(v)
            }
            Token::Prop(p) => {
                let v = Value::Prop(p.clone());
                self.i += 1;
                Some(v)
            }
            _ => None,
        }
    }

    /// A fixed-function color: `(r, g, b, a)`, `[_Prop]` or a number.
    fn color_value(&mut self) -> Option<ColorValue> {
        match self.peek()? {
            Token::Prop(p) => {
                let v = ColorValue::Property(p.clone());
                self.i += 1;
                Some(v)
            }
            Token::Num(n) => {
                let v = ColorValue::Literal([*n; 4]);
                self.i += 1;
                Some(v)
            }
            Token::LParen => {
                self.i += 1;
                let mut v = [0.0f32, 0.0, 0.0, 1.0];
                let mut k = 0;
                while let Some(t) = self.next() {
                    match t {
                        Token::RParen => break,
                        Token::Num(n) if k < 4 => {
                            v[k] = n;
                            k += 1;
                        }
                        _ => {}
                    }
                }
                Some(ColorValue::Literal(v))
            }
            _ => None,
        }
    }

    /// The shader's (or a Category's) body up to its closing brace.
    fn body(&mut self, shader: &mut ShaderLab, outer: &States) {
        let mut states = outer.clone();
        while let Some(t) = self.next() {
            match t {
                Token::Close => return,
                Token::Open => {
                    self.i -= 1;
                    self.skip_block();
                }
                Token::Word(w) if self.block(&w).is_some_and(|b| self.blocks[b].include) => {
                    self.includes.push(self.block(&w).unwrap());
                }
                Token::Word(w) => match command(&w) {
                    "Properties" => self.properties(shader),
                    "SubShader" => {
                        let sub = self.subshader(&states);
                        shader.subshaders.push(sub);
                        shader.select("");
                    }
                    "Category" => {
                        if self.peek() == Some(&Token::Open) {
                            self.i += 1;
                            self.body(shader, &states);
                        }
                    }
                    "Fallback" => match self.next() {
                        Some(Token::Str(name)) => shader.fallback = Some(name),
                        // `Fallback Off`
                        _ => shader.fallback = None,
                    },
                    // `Dependency "AddPassShader" = "Hidden/..."`
                    "Dependency" => {
                        if let Some(Token::Str(key)) = self.next() {
                            if self.peek() == Some(&Token::Eq) {
                                self.i += 1;
                            }
                            if let Some(Token::Str(value)) = self.next() {
                                shader.dependencies.push((key, value));
                            }
                        }
                    }
                    other => {
                        self.state(other, &mut states);
                    }
                },
                _ => {}
            }
        }
    }

    fn properties(&mut self, shader: &mut ShaderLab) {
        if self.next() != Some(Token::Open) {
            return;
        }
        let mut gamma = false;
        let mut hdr = false;
        loop {
            let Some(t) = self.next() else { return };
            match t {
                Token::Close => return,
                // Attributes: `[HideInInspector]`, `[Toggle(X)]` (tokenized as one Prop).
                Token::Prop(attribute) => {
                    gamma |= attribute.eq_ignore_ascii_case("gamma");
                    hdr |= attribute.eq_ignore_ascii_case("hdr");
                    continue;
                }
                Token::Word(name) => {
                    // `(` "Display" `,` Type [`(` a `,` b `)`] `)` `=` default
                    if self.peek() != Some(&Token::LParen) {
                        continue;
                    }
                    self.i += 1;
                    let mut depth = 1;
                    let mut kind = String::new();
                    let mut after_comma = false;
                    while let Some(t) = self.next() {
                        match t {
                            Token::LParen => depth += 1,
                            Token::RParen => {
                                depth -= 1;
                                if depth == 0 {
                                    break;
                                }
                            }
                            Token::Comma if depth == 1 => after_comma = true,
                            Token::Word(w) if after_comma && kind.is_empty() => kind = w,
                            _ => {}
                        }
                    }
                    let is_gamma = std::mem::take(&mut gamma);
                    let is_hdr = std::mem::take(&mut hdr);
                    if self.peek() != Some(&Token::Eq) {
                        continue;
                    }
                    self.i += 1;
                    let kind = kind.to_ascii_lowercase();
                    if (kind == "color" && !is_hdr) || is_gamma {
                        shader.colors.insert(name.clone());
                    }
                    if is_gamma {
                        shader.gamma.insert(name.clone());
                    }
                    let default = match self.peek() {
                        Some(Token::Num(n)) => {
                            let n = *n;
                            self.i += 1;
                            Some(PropertyDefault::Float(n))
                        }
                        Some(Token::LParen) => {
                            self.i += 1;
                            let mut v = [0.0f32; 4];
                            let mut k = 0;
                            while let Some(t) = self.next() {
                                match t {
                                    Token::RParen => break,
                                    Token::Num(n) if k < 4 => {
                                        v[k] = n;
                                        k += 1;
                                    }
                                    _ => {}
                                }
                            }
                            Some(PropertyDefault::Vector(v))
                        }
                        Some(Token::Str(s)) => {
                            let s = s.clone();
                            self.i += 1;
                            if self.peek() == Some(&Token::Open) {
                                self.skip_block();
                            }
                            Some(PropertyDefault::Texture(s))
                        }
                        _ => None,
                    };
                    if let Some(mut d) = default {
                        if (kind == "color" || kind == "vector")
                            && let PropertyDefault::Float(f) = d
                        {
                            d = PropertyDefault::Vector([f, f, f, f]);
                        }
                        shader.properties.push((name, d));
                    }
                }
                _ => {}
            }
        }
    }

    fn subshader(&mut self, outer: &States) -> SubShader {
        let mut sub = SubShader {
            tags: HashMap::new(),
            passes: Vec::new(),
            lod: 0,
        };
        if self.next() != Some(Token::Open) {
            return sub;
        }
        let mut states = outer.clone();
        let scope = self.includes.len();
        let mut passes: Vec<PassInfo> = Vec::new();
        while let Some(t) = self.next() {
            match t {
                Token::Close => break,
                Token::Open => {
                    self.i -= 1;
                    self.skip_block();
                }
                Token::Word(w) if self.block(&w).is_some() => {
                    let b = self.block(&w).unwrap();
                    if self.blocks[b].include {
                        self.includes.push(b);
                    } else if let Some(mut program) = self.program(b) {
                        // A SubShader-level program: a surface shader's, which generates
                        // a forward base pass and (unless `noforwardadd`) a forward add pass.
                        program.surface = true;
                        let surface = surface_states(&program.code);
                        let add = surface_add_states(&program.code);
                        passes.push(PassInfo {
                            name: "FORWARD".into(),
                            states: surface,
                            program: Some(program.clone()),
                            ..Default::default()
                        });
                        if let Some(add) = add {
                            passes.push(PassInfo {
                                name: "FORWARD".into(),
                                states: add,
                                program: Some(program),
                                ..Default::default()
                            });
                        }
                    }
                }
                Token::Word(w) => match command(&w) {
                    "LOD" => {
                        if let Some(Token::Num(n)) = self.peek() {
                            sub.lod = n.max(0.0) as u32;
                            self.i += 1;
                        }
                    }
                    "Pass" => passes.push(self.pass()),
                    "UsePass" => {
                        if let Some(Token::Str(target)) = self.peek() {
                            let target = target.clone();
                            self.i += 1;
                            passes.push(PassInfo {
                                use_pass: Some(target),
                                ..Default::default()
                            });
                        }
                    }
                    "GrabPass" => {
                        let start = self.i;
                        self.skip_block();
                        // The texture: a string of the block's own (not a `Name` or a tag).
                        let mut depth = 0;
                        let mut name = None;
                        let block = &self.t[start..self.i];
                        for (k, t) in block.iter().enumerate() {
                            match t {
                                Token::Open => depth += 1,
                                Token::Close => depth -= 1,
                                Token::Str(s)
                                    if depth == 1
                                        && !matches!(block.get(k.wrapping_sub(1)),
                                            Some(Token::Word(w)) if w.eq_ignore_ascii_case("Name")) =>
                                {
                                    name = Some(s.clone());
                                }
                                _ => {}
                            }
                        }
                        passes.push(PassInfo {
                            grab: Some(name.unwrap_or_default()),
                            ..Default::default()
                        });
                    }
                    other => self.state(other, &mut states),
                },
                _ => {}
            }
        }
        self.includes.truncate(scope);
        // States set after a pass apply to it too: ShaderLab states are SubShader-wide.
        sub.tags = states.tags.clone();
        sub.passes = passes
            .into_iter()
            .map(|mut pass| {
                if pass.use_pass.is_none() {
                    pass.states = states.overlay(&pass.states);
                }
                pass
            })
            .collect();
        sub
    }

    fn pass(&mut self) -> PassInfo {
        let mut pass = PassInfo::default();
        if self.next() != Some(Token::Open) {
            return pass;
        }
        let scope = self.includes.len();
        while let Some(t) = self.next() {
            match t {
                Token::Close => break,
                Token::Open => {
                    self.i -= 1;
                    self.skip_block();
                }
                Token::Word(w) if w == "Name" => {
                    if let Some(Token::Str(n)) = self.peek() {
                        pass.name = n.clone();
                        self.i += 1;
                    }
                }
                Token::Word(w) if self.block(&w).is_some() => {
                    let b = self.block(&w).unwrap();
                    if self.blocks[b].include {
                        self.includes.push(b);
                    } else if self.blocks[b].glsl {
                        pass.glsl = true;
                    } else {
                        pass.program = self.program(b);
                    }
                }
                Token::Word(w) => self.state(&w, &mut pass.states),
                _ => {}
            }
        }
        self.includes.truncate(scope);
        pass
    }

    /// One render state command, its keyword already read. Unknown commands are skipped: their
    /// arguments are words or numbers the caller's loop passes over, and their blocks are skipped.
    fn state(&mut self, keyword: &str, states: &mut States) {
        let keyword = command(keyword);
        let on = |v: Option<Value>| match v {
            Some(Value::Word(w)) => w.eq_ignore_ascii_case("on"),
            Some(Value::Num(n)) => n != 0.0,
            _ => false,
        };
        match keyword {
            "Tags" => {
                if self.next() != Some(Token::Open) {
                    return;
                }
                while let Some(t) = self.next() {
                    match t {
                        Token::Close => break,
                        Token::Str(k) if self.peek() == Some(&Token::Eq) => {
                            self.i += 1;
                            if let Some(Token::Str(v)) = self.next() {
                                states.tags.insert(canonical_tag(k), v);
                            }
                        }
                        _ => {}
                    }
                }
            }
            "Blend" => {
                // An optional render target index comes first: `Blend 1 One Zero`.
                if let Some(Token::Num(_)) = self.peek()
                    && matches!(
                        self.t.get(self.i + 1),
                        Some(Token::Word(_) | Token::Prop(_))
                    )
                {
                    self.i += 1;
                }
                let mut values = Vec::new();
                if let Some(first) = self.value() {
                    let off = first == Value::Word("Off".into());
                    values.push(first);
                    if !off {
                        values.extend(self.value());
                        if self.peek() == Some(&Token::Comma) {
                            self.i += 1;
                            values.extend(self.value());
                            values.extend(self.value());
                        }
                    }
                }
                states.blend = Some(values);
            }
            "BlendOp" => {
                let mut values = Vec::new();
                if let Some(v) = self.value() {
                    values.push(v);
                }
                if self.peek() == Some(&Token::Comma) {
                    self.i += 1;
                    if let Some(v) = self.value() {
                        values.push(v);
                    }
                }
                states.blend_op = Some(values);
            }
            "ZWrite" => states.zwrite = self.value(),
            "AlphaToMask" => states.alpha_to_mask = self.value(),
            "ZClip" => states.zclip = self.value(),
            "ZTest" => states.ztest = self.value(),
            "Cull" => states.cull = self.value(),
            "Offset" => {
                let factor = self.value();
                if self.peek() == Some(&Token::Comma) {
                    self.i += 1;
                }
                let units = self.value();
                if let (Some(f), Some(u)) = (factor, units) {
                    states.offset = Some([f, u]);
                }
            }
            "ColorMask" => {
                states.color_mask = self.value();
                // Optional render target index.
                if let Some(Token::Num(_)) = self.peek()
                    && matches!(states.color_mask, Some(Value::Word(_) | Value::Prop(_)))
                {
                    self.i += 1;
                }
            }
            "Stencil" => {
                if self.next() != Some(Token::Open) {
                    return;
                }
                let mut s = Stencil::default();
                while let Some(t) = self.next() {
                    match t {
                        Token::Close => break,
                        Token::Word(w) => {
                            let v = self.value();
                            match w.as_str() {
                                "Ref" => s.reference = v,
                                "Comp" | "CompFront" => s.comp = v,
                                "Pass" | "PassFront" => s.pass = v,
                                "Fail" | "FailFront" => s.fail = v,
                                "ZFail" | "ZFailFront" => s.zfail = v,
                                "ReadMask" => s.read_mask = v,
                                "WriteMask" => s.write_mask = v,
                                _ => {}
                            }
                        }
                        _ => {}
                    }
                }
                states.stencil = Some(s);
            }
            // Fixed function.
            "Lighting" => states.fixed.lighting = Some(on(self.value())),
            "SeparateSpecular" => states.fixed.separate_specular = Some(on(self.value())),
            "ColorMaterial" => {
                if let Some(Value::Word(w)) = self.value() {
                    states.fixed.color_material = Some(w);
                }
            }
            "Color" => states.fixed.color = self.color_value(),
            "AlphaTest" => match self.value() {
                Some(Value::Word(w)) if w.eq_ignore_ascii_case("off") => {
                    states.fixed.alpha_test = Some(None)
                }
                Some(Value::Word(comparison)) => {
                    let reference = self.color_value().unwrap_or(ColorValue::Literal([0.0; 4]));
                    states.fixed.alpha_test = Some(Some((comparison, reference)));
                }
                _ => {}
            },
            "Material" => {
                if self.next() != Some(Token::Open) {
                    return;
                }
                let mut m = LightingMaterial::default();
                while let Some(t) = self.next() {
                    match t {
                        Token::Close => break,
                        Token::Word(w) => {
                            let v = self.color_value();
                            match w.as_str() {
                                "Diffuse" => m.diffuse = v,
                                "Ambient" => m.ambient = v,
                                "Specular" => m.specular = v,
                                "Emission" => m.emission = v,
                                "Shininess" => m.shininess = v,
                                _ => {}
                            }
                        }
                        _ => {}
                    }
                }
                states.fixed.material = Some(m);
            }
            "BindChannels" => {
                if self.next() != Some(Token::Open) {
                    return;
                }
                let mut bindings = Vec::new();
                while let Some(t) = self.next() {
                    match t {
                        Token::Close => break,
                        Token::Word(w) if w == "Bind" => {
                            if let Some(Token::Str(source)) = self.next() {
                                if self.peek() == Some(&Token::Comma) {
                                    self.i += 1;
                                }
                                if let Some(Token::Word(target)) = self.next() {
                                    bindings.push((source, target));
                                }
                            }
                        }
                        _ => {}
                    }
                }
                states.fixed.bindings = Some(bindings);
            }
            "SetTexture" => {
                let Some(Token::Prop(texture)) = self.next() else {
                    self.skip_block();
                    return;
                };
                if self.next() != Some(Token::Open) {
                    return;
                }
                let mut stage = TextureStage {
                    texture,
                    combine: String::new(),
                    constant: None,
                };
                let mut in_combine = false;
                while let Some(t) = self.next() {
                    match t {
                        Token::Close => break,
                        Token::Word(w) if w.eq_ignore_ascii_case("combine") => in_combine = true,
                        Token::Word(w) if w.eq_ignore_ascii_case("constantColor") => {
                            in_combine = false;
                            stage.constant = self.color_value();
                        }
                        Token::Word(w) if w.eq_ignore_ascii_case("matrix") => {
                            in_combine = false;
                            self.value();
                        }
                        t if in_combine => {
                            let text = match t {
                                Token::Word(w) => w,
                                Token::Num(n) => n.to_string(),
                                Token::Prop(p) => p,
                                Token::LParen => "(".into(),
                                Token::RParen => ")".into(),
                                Token::Comma => ",".into(),
                                _ => String::new(),
                            };
                            stage.combine.push_str(&text);
                            stage.combine.push(' ');
                        }
                        _ => {}
                    }
                }
                if stage.combine.trim().is_empty() {
                    stage.combine = "texture".into();
                }
                states.fixed.stages.push(stage);
            }
            "Fog" => self.skip_block(),
            _ => {}
        }
    }
}

/// Shaders by name (the project's Assets and packages, then Unity's built-in ones), parsed on
/// first use.
#[derive(Default)]
pub struct ShaderIndex {
    files: HashMap<String, PathBuf>,
    parsed: HashMap<String, Option<ShaderLab>>,
    /// The active render pipeline's tag (empty: built-in): which SubShader shaders run.
    pipeline: String,
    /// Roots that gain shaders while the game runs (the ShaderLab importers generate, Shader
    /// Graph's: `Library/Unbound/GeneratedShaders`), with the modification time last scanned.
    live: Vec<(PathBuf, Option<std::time::SystemTime>)>,
}

/// Where the managed side writes the ShaderLab source importers generate
/// (`ShaderUtil.CreateShaderAsset`), relative to the project.
pub const GENERATED_SHADERS: &str = "Library/Unbound/GeneratedShaders";

impl ShaderIndex {
    /// Indexes every `.shader` file below the roots (an earlier root's shader wins a name).
    pub fn scan(roots: &[PathBuf]) -> ShaderIndex {
        let mut files = HashMap::new();
        let mut live = Vec::new();
        for root in roots {
            scan_dir(root, &mut files);
            if root.ends_with(GENERATED_SHADERS) {
                live.push((root.clone(), modified(root)));
            }
        }
        tracing::debug!("indexed {} project shaders", files.len());
        ShaderIndex {
            files,
            parsed: HashMap::new(),
            pipeline: String::new(),
            live,
        }
    }

    /// Picks up shaders generated since the last look (a name not found, or found missing).
    fn rescan_live(&mut self) -> bool {
        let mut changed = false;
        for (root, seen) in &mut self.live {
            let now = modified(root);
            if now != *seen {
                *seen = now;
                scan_dir(root, &mut self.files);
                changed = true;
            }
        }
        if changed {
            self.parsed.retain(|_, lab| lab.is_some());
        }
        changed
    }

    pub fn len(&self) -> usize {
        self.files.len()
    }

    /// Every indexed shader's name, sorted.
    pub fn names(&self) -> Vec<String> {
        let mut names: Vec<String> = self.files.keys().cloned().collect();
        names.sort();
        names
    }

    pub fn is_empty(&self) -> bool {
        self.files.is_empty()
    }

    pub fn get(&mut self, name: &str) -> Option<&ShaderLab> {
        let base = Selection::parse(name).0;
        if !self.live.is_empty() && !self.files.contains_key(base) {
            self.rescan_live();
        }
        if !self.parsed.contains_key(name) {
            let mut parsed = self.load(name);
            if let Some(lab) = &mut parsed {
                self.resolve_use_passes(lab, 0);
            }
            self.parsed.insert(name.to_string(), parsed);
        }
        self.parsed.get(name).and_then(Option::as_ref)
    }

    /// The shader a name stands for, its SubShader picked under the active pipeline and the
    /// name's [`Selection`].
    fn load(&self, name: &str) -> Option<ShaderLab> {
        let (base, selection) = Selection::parse(name);
        // Unity still answers to the legacy shaders' old names (`Fallback "VertexLit"`).
        let path = self
            .files
            .get(base)
            .or_else(|| self.files.get(&format!("Legacy Shaders/{base}")))?;
        let text = std::fs::read_to_string(path).ok()?;
        let mut lab = ShaderLab::parse_file(&text, path)?;
        lab.select_with(&self.pipeline, &selection);
        Some(lab)
    }

    /// The active render pipeline's tag. Returns whether it changed (shaders then select their
    /// SubShaders again).
    pub fn set_pipeline(&mut self, tag: &str) -> bool {
        if self.pipeline == tag {
            return false;
        }
        self.pipeline = tag.to_string();
        self.parsed.clear();
        true
    }

    pub fn pipeline(&self) -> &str {
        &self.pipeline
    }

    /// Replaces `UsePass` passes by the named passes (`"Shader/PASS"`, pass names compared
    /// case-insensitively as Unity does).
    fn resolve_use_passes(&self, lab: &mut ShaderLab, depth: u32) {
        if depth > 4 || lab.passes.iter().all(|p| p.use_pass.is_none()) {
            return;
        }
        for pass in &mut lab.passes {
            let Some(target) = &pass.use_pass else {
                continue;
            };
            let Some((shader, pass_name)) = target.rsplit_once('/') else {
                continue;
            };
            let Some(mut other) = self.load(shader) else {
                continue;
            };
            self.resolve_use_passes(&mut other, depth + 1);
            if let Some(found) = other
                .passes
                .iter()
                .find(|p| p.name.eq_ignore_ascii_case(pass_name))
            {
                // The pass itself, its name included (a UsePass pass keeps the target's name).
                let use_pass = pass.use_pass.take();
                *pass = found.clone();
                pass.use_pass = use_pass;
            }
        }
    }
}

fn modified(dir: &Path) -> Option<std::time::SystemTime> {
    std::fs::metadata(dir).and_then(|m| m.modified()).ok()
}

fn scan_dir(dir: &Path, files: &mut HashMap<String, PathBuf>) {
    let Ok(entries) = std::fs::read_dir(dir) else {
        return;
    };
    for entry in entries.flatten() {
        let path = entry.path();
        let Ok(kind) = entry.file_type() else {
            continue;
        };
        if kind.is_dir() {
            let name = entry.file_name();
            let name = name.to_string_lossy();
            // Unity ignores hidden folders and `~` folders.
            if name.starts_with('.') || name.ends_with('~') {
                continue;
            }
            scan_dir(&path, files);
        } else if path.extension().is_some_and(|e| e == "shader")
            && let Some(name) = std::fs::read_to_string(&path)
                .ok()
                .and_then(|t| shader_name(&t))
        {
            files.entry(name).or_insert(path);
        }
    }
}

/// The `Shader "Name"` declaration of a file.
fn shader_name(text: &str) -> Option<String> {
    let stripped = strip(text);
    let at = stripped.find("Shader")?;
    let rest = &stripped[at + "Shader".len()..];
    let open = rest.find('"')?;
    let close = rest[open + 1..].find('"')?;
    Some(rest[open + 1..open + 1 + close].to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn program_keywords_share_lines() {
        let lab = ShaderLab::parse(
            "Shader \"S\" { SubShader { Pass { CGPROGRAM\n#pragma vertex v\nfloat4 v() : SV_POSITION { return 0; }\nENDCG }\n Pass { CGPROGRAM // x\n#pragma vertex w\nENDCG } } }",
        )
        .unwrap();
        assert_eq!(lab.passes.len(), 2);
        assert!(
            lab.passes[0]
                .program
                .as_ref()
                .unwrap()
                .code
                .contains("#pragma vertex v")
        );
        assert!(
            lab.passes[1]
                .program
                .as_ref()
                .unwrap()
                .code
                .contains("#pragma vertex w")
        );
        let lod = ShaderLab::parse("Shader \"L\" { SubShader { LOD 300 Pass { } } SubShader { LOD 100 Tags { \"RenderType\"=\"Opaque\" } Pass { } } Fallback \"F\" }").unwrap();
        assert_eq!(lod.subshaders[0].lod, 300);
        assert_eq!(lod.subshaders[1].lod, 100);
        let mut picked = lod.clone();
        picked.select_with(
            "",
            &Selection {
                max_lod: Some(200),
                ..Default::default()
            },
        );
        assert!(picked.has_subshader && picked.tags.get("RenderType").is_some());
        picked.select_with(
            "",
            &Selection {
                max_lod: Some(50),
                ..Default::default()
            },
        );
        assert!(!picked.has_subshader);
        let (name, selection) = Selection::parse("A/B|tag=RenderType=TransparentCutout");
        assert_eq!(name, "A/B");
        assert_eq!(
            selection.tag,
            Some(("RenderType".to_string(), "TransparentCutout".to_string()))
        );
        picked.select_with("", &Selection::parse("L|tag=RenderType=Opaque").1);
        assert!(picked.has_subshader);
    }

    #[test]
    fn color_transfer_flags_match_native_material_defaults() {
        let lab = ShaderLab::parse(
            r#"Shader "S" { Properties {
            _C("C",Color)=(2,0.5,-0.25,3)
            [HDR] _H("H",Color)=(2,0.5,-0.25,3)
            _V("V",Vector)=(2,0.5,-0.25,3)
            [Gamma][HDR] _GH("GH",Color)=(2,0.5,-0.25,3)
            [Gamma] _GV("GV",Vector)=(2,0.5,-0.25,3)
            [Gamma] _GF("GF",Float)=2
        } SubShader { Pass {} } }"#,
        )
        .unwrap();
        for name in ["_C", "_GH", "_GV", "_GF"] {
            assert!(lab.colors.contains(name), "{name}");
        }
        for name in ["_H", "_V"] {
            assert!(!lab.colors.contains(name), "{name}");
        }
    }

    #[test]
    fn passes_keep_their_programs_with_includes_in_scope() {
        let source = r#"Shader "P" {
            Properties { _Tint ("Tint", Color) = (1,1,1,1) [Gamma] _Raw ("Raw", Color) = (0,0,0,0) _V ("V", Vector) = (1,2,3,4) }
            CGINCLUDE
            float shared;
            ENDCG
            SubShader {
                Pass { Tags { "LightMode"="ShadowCaster" }
                    CGPROGRAM
                    #pragma vertex shadow
                    ENDCG
                }
                Pass { Name "MAIN"
                    HLSLINCLUDE
                    float hlsl_only;
                    ENDHLSL
                    CGPROGRAM
                    #pragma vertex vert
                    ENDCG
                    Blend One One
                }
            }
        }"#;
        let lab = ShaderLab::parse_file(source, Path::new("/p/P.shader")).unwrap();
        assert_eq!(lab.forward_passes(), vec![1]);
        let main = &lab.passes[1];
        assert_eq!(main.name, "MAIN");
        let program = main.program.as_ref().unwrap();
        assert!(program.cg && program.file == Path::new("/p/P.shader"));
        // The shader's CGINCLUDE, not the HLSL one, then the program.
        assert!(
            program.code.contains("float shared;") && program.code.contains("#pragma vertex vert")
        );
        assert!(!program.code.contains("hlsl_only") && !program.code.contains("shadow"));
        // States still parse around the program block.
        assert!(main.states.blend.is_some());
        assert!(
            lab.colors.contains("_Tint")
                && lab.colors.contains("_Raw")
                && !lab.colors.contains("_V")
        );
    }

    const TMP_LIKE: &str = r#"
// comment
Shader "TextMeshPro/Mobile/Distance Field" {
Properties {
    _FaceColor ("Face Color", Color) = (1,1,1,1)
    [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
    _MainTex ("Font Atlas", 2D) = "white" {}
    _Ratio ("R", Range(-1,1)) = -0.5
}
SubShader {
    Tags { "Queue"="Transparent" "IgnoreProjector"="True" }
    Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] }
    Cull [_CullMode]
    ZWrite Off
    ZTest [unity_GUIZTestMode]
    Blend One OneMinusSrcAlpha
    ColorMask [_ColorMask]
    Pass {
        CGPROGRAM
        #pragma vertex VertShader
        float4 x = { 1, 2 };
        ENDCG
    }
    Pass {
        Tags { "LightMode"="ShadowCaster" }
        ZWrite On
    }
}
Fallback "Other"
}
"#;

    #[test]
    fn grab_passes_name_their_texture() {
        let s = ShaderLab::parse(
            r#"Shader "G" { SubShader {
                GrabPass { }
                GrabPass { "_Behind" }
                GrabPass { Name "NAMED" Tags { "LightMode" = "Always" } "_Tagged" }
                Pass { }
            } }"#,
        )
        .unwrap();
        let grabs: Vec<Option<&str>> = s.passes.iter().map(|p| p.grab.as_deref()).collect();
        assert_eq!(
            grabs,
            [Some(""), Some("_Behind"), Some("_Tagged"), None],
            "{:?}",
            s.passes
        );
        // The forward renderer runs them in order with the passes.
        assert_eq!(s.forward_passes(), [0, 1, 2, 3]);
    }

    #[test]
    fn parses_states_and_properties() {
        let s = ShaderLab::parse(TMP_LIKE).unwrap();
        assert_eq!(s.name, "TextMeshPro/Mobile/Distance Field");
        assert_eq!(
            s.property("_FaceColor"),
            Some(&PropertyDefault::Vector([1.0; 4]))
        );
        assert_eq!(
            s.property("_StencilComp"),
            Some(&PropertyDefault::Float(8.0))
        );
        assert_eq!(
            s.property("_MainTex"),
            Some(&PropertyDefault::Texture("white".into()))
        );
        assert_eq!(s.property("_Ratio"), Some(&PropertyDefault::Float(-0.5)));
        assert_eq!(s.fallback.as_deref(), Some("Other"));
        assert_eq!(s.forward_passes(), vec![0]);
        let states = &s.passes[0].states;
        assert_eq!(states.zwrite, Some(Value::Word("Off".into())));
        assert_eq!(
            states.blend,
            Some(vec![
                Value::Word("One".into()),
                Value::Word("OneMinusSrcAlpha".into())
            ])
        );
        assert_eq!(states.ztest, Some(Value::Prop("unity_GUIZTestMode".into())));
        assert_eq!(s.tags.get("Queue").map(String::as_str), Some("Transparent"));
        let stencil = states.stencil.clone().unwrap();
        assert_eq!(stencil.comp, Some(Value::Prop("_StencilComp".into())));
        // The shadow caster pass writes depth (its own state on the SubShader's).
        assert_eq!(s.passes[1].states.zwrite, Some(Value::Word("On".into())));
    }

    #[test]
    fn categories_and_fixed_function_passes() {
        let s = ShaderLab::parse(
            r#"Shader "Mobile/Particles/Additive" {
            Properties { _MainTex ("Particle Texture", 2D) = "white" {} }
            Category {
                Tags { "Queue"="Transparent" }
                Blend SrcAlpha One, Zero One
                ColorMask RGB
                Cull Off Lighting Off ZWrite Off Fog { Color (0,0,0,0) }
                BindChannels { Bind "Color", color Bind "Vertex", vertex Bind "TexCoord", texcoord }
                SubShader { Pass {
                    AlphaTest Greater [_Cutoff]
                    SetTexture [_MainTex] { constantColor (1, 0.5, 0, 1) combine texture * primary Double, texture }
                } }
            }
            }"#,
        )
        .unwrap();
        let states = &s.passes[0].states;
        assert_eq!(states.blend.as_ref().map(Vec::len), Some(4));
        assert_eq!(states.color_mask, Some(Value::Word("RGB".into())));
        assert_eq!(states.cull, Some(Value::Word("Off".into())));
        assert_eq!(states.zwrite, Some(Value::Word("Off".into())));
        let ff = states.fixed_function();
        assert!(!ff.lighting);
        assert_eq!(ff.bindings.len(), 3);
        assert_eq!(
            ff.alpha_test,
            Some(("Greater".into(), ColorValue::Property("_Cutoff".into())))
        );
        assert_eq!(ff.stages.len(), 1);
        assert_eq!(ff.stages[0].texture, "_MainTex");
        assert_eq!(
            ff.stages[0].combine.trim(),
            "texture * primary Double , texture"
        );
        assert_eq!(
            ff.stages[0].constant,
            Some(ColorValue::Literal([1.0, 0.5, 0.0, 1.0]))
        );
    }

    #[test]
    fn passes_subshaders_and_surface_shaders() {
        let s = ShaderLab::parse(
            r#"Shader "Outline" {
            SubShader {
                Tags { "RenderPipeline"="UniversalPipeline" }
                Pass { Name "URP" }
            }
            SubShader {
                Cull Off ZWrite Off Blend One OneMinusSrcAlpha
                Pass { Name "Outline" Blend SrcAlpha One }
                Pass { Name "Normal" }
                Pass { Name "Caster" Tags { "LightMode"="ShadowCaster" } }
            }}"#,
        )
        .unwrap();
        // The URP SubShader is skipped; both drawing passes draw, in order.
        assert_eq!(s.passes.len(), 3);
        assert_eq!(s.forward_passes(), vec![0, 1]);
        assert_eq!(
            s.passes[0].states.blend.as_ref().unwrap()[1],
            Value::Word("One".into())
        );
        assert_eq!(
            s.passes[1].states.blend.as_ref().unwrap()[1],
            Value::Word("OneMinusSrcAlpha".into())
        );
        let surface = ShaderLab::parse(
            "Shader \"S\" { SubShader { Tags { \"Queue\"=\"Transparent\" }\nCGPROGRAM\n#pragma surface surf Lambert alpha:fade\nENDCG\n} }",
        )
        .unwrap();
        assert_eq!(surface.forward_passes(), vec![0]);
        let pass = &surface.passes[0];
        assert!(pass.program.as_ref().unwrap().surface);
        assert_eq!(pass.light_mode(), LightMode::ForwardBase);
        assert_eq!(
            pass.states.blend,
            Some(vec![
                Value::Word("SrcAlpha".into()),
                Value::Word("OneMinusSrcAlpha".into())
            ])
        );
        assert_eq!(pass.states.zwrite, Some(Value::Word("Off".into())));
        // Vertex-lit passes draw only without forward ones.
        let vertex_lit = ShaderLab::parse(
            r#"Shader "V" { SubShader {
                Pass { Tags { "LightMode"="Vertex" } }
                Pass { Tags { "LightMode"="VertexLM" } }
            } }"#,
        )
        .unwrap();
        assert_eq!(vertex_lit.forward_passes(), vec![0]);
    }

    /// Every shader of a real project parses: `UNBOUND_TEST_PROJECT=<unity project> cargo test`.
    #[test]
    fn parses_project_shaders() {
        let Some(root) = std::env::var_os("UNBOUND_TEST_PROJECT") else {
            return;
        };
        let root = PathBuf::from(root);
        let mut index = ShaderIndex::scan(&[root.join("Assets"), root.join("Packages")]);
        let names: Vec<String> = index.files.keys().cloned().collect();
        assert!(!names.is_empty());
        for name in names {
            let lab = index
                .get(&name)
                .unwrap_or_else(|| panic!("{name} does not parse"));
            eprintln!("{name}: passes {:?}", lab.forward_passes());
        }
    }
}
