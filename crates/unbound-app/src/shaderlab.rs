//! A reader for the parts of ShaderLab the renderer needs: a shader's name, its properties'
//! defaults and the render states of its first SubShader's first drawing pass (Blend, BlendOp,
//! ZWrite, ZTest, Cull, ColorMask, Stencil, Tags). Values may be literals or `[_Property]`
//! references, resolved per material.
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
};

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

/// Removes comments and program blocks (`CGPROGRAM` ... `ENDCG` and friends).
fn strip(source: &str) -> String {
    let mut out = String::with_capacity(source.len());
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
            }
            continue;
        }
        if trimmed.starts_with("CGPROGRAM")
            || trimmed.starts_with("CGINCLUDE")
            || trimmed.starts_with("HLSLPROGRAM")
            || trimmed.starts_with("HLSLINCLUDE")
            || trimmed.starts_with("GLSLPROGRAM")
        {
            in_program = true;
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
    out
}

fn tokenize(source: &str) -> Vec<Token> {
    let text = strip(source);
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
                tokens.push(Token::Str(chars[start..j.min(chars.len())].iter().collect()));
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
                    && !matches!(chars[j], '{' | '}' | '(' | ')' | ',' | '=' | '"' | '[' | ']')
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
    pub tags: HashMap<String, String>,
}

impl States {
    /// `other` on top of `self`.
    fn overlay(&self, other: &States) -> States {
        let mut tags = self.tags.clone();
        tags.extend(other.tags.iter().map(|(k, v)| (k.clone(), v.clone())));
        States {
            blend: other.blend.clone().or_else(|| self.blend.clone()),
            blend_op: other.blend_op.clone().or_else(|| self.blend_op.clone()),
            zwrite: other.zwrite.clone().or_else(|| self.zwrite.clone()),
            ztest: other.ztest.clone().or_else(|| self.ztest.clone()),
            cull: other.cull.clone().or_else(|| self.cull.clone()),
            color_mask: other.color_mask.clone().or_else(|| self.color_mask.clone()),
            stencil: other.stencil.clone().or_else(|| self.stencil.clone()),
            tags,
        }
    }
}

#[derive(Debug, Clone, Default)]
pub struct ShaderLab {
    pub name: String,
    pub properties: Vec<(String, PropertyDefault)>,
    /// The first SubShader's states merged with its first drawing pass's.
    pub states: States,
    pub pass_count: usize,
}

impl ShaderLab {
    pub fn property(&self, name: &str) -> Option<&PropertyDefault> {
        self.properties
            .iter()
            .find(|(n, _)| n == name)
            .map(|(_, v)| v)
    }

    pub fn parse(source: &str) -> Option<ShaderLab> {
        let tokens = tokenize(source);
        let mut p = Parser { t: &tokens, i: 0 };
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
            ..Default::default()
        };
        let mut found_subshader = false;
        p.body(&mut shader, &States::default(), &mut found_subshader);
        Some(shader)
    }
}

struct Parser<'a> {
    t: &'a [Token],
    i: usize,
}

impl Parser<'_> {
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

    /// The shader's (or a Category's) body up to its closing brace.
    fn body(&mut self, shader: &mut ShaderLab, outer: &States, found: &mut bool) {
        let mut states = outer.clone();
        while let Some(t) = self.next() {
            match t {
                Token::Close => return,
                Token::Open => {
                    self.i -= 1;
                    self.skip_block();
                }
                Token::Word(w) => match w.as_str() {
                    "Properties" => self.properties(shader),
                    "SubShader" => {
                        if *found {
                            self.skip_block();
                        } else {
                            *found = true;
                            self.subshader(shader, &states);
                        }
                    }
                    "Category" => {
                        if self.peek() == Some(&Token::Open) {
                            self.i += 1;
                            self.body(shader, &states, found);
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
        loop {
            let Some(t) = self.next() else { return };
            match t {
                Token::Close => return,
                // Attributes: `[HideInInspector]`, `[Toggle(X)]` (tokenized as one Prop).
                Token::Prop(_) => continue,
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
                    if self.peek() != Some(&Token::Eq) {
                        continue;
                    }
                    self.i += 1;
                    let kind = kind.to_ascii_lowercase();
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
                        if kind == "color" || kind == "vector" {
                            if let PropertyDefault::Float(f) = d {
                                d = PropertyDefault::Vector([f, f, f, f]);
                            }
                        }
                        shader.properties.push((name, d));
                    }
                }
                _ => {}
            }
        }
    }

    fn subshader(&mut self, shader: &mut ShaderLab, outer: &States) {
        if self.next() != Some(Token::Open) {
            return;
        }
        let mut states = outer.clone();
        let mut chosen: Option<States> = None;
        while let Some(t) = self.next() {
            match t {
                Token::Close => break,
                Token::Open => {
                    self.i -= 1;
                    self.skip_block();
                }
                Token::Word(w) => match w.as_str() {
                    "Pass" => {
                        let pass = self.pass();
                        let light_mode = pass
                            .tags
                            .get("LightMode")
                            .map(|s| s.to_ascii_lowercase())
                            .unwrap_or_default();
                        let drawing = !matches!(
                            light_mode.as_str(),
                            "shadowcaster" | "meta" | "deferred" | "prepassbase" | "prepassfinal"
                                | "motionvectors" | "depthonly" | "forwardadd"
                        );
                        if drawing {
                            shader.pass_count += 1;
                            if chosen.is_none() {
                                chosen = Some(pass);
                            }
                        }
                    }
                    "UsePass" | "GrabPass" => {
                        // `UsePass "Name"`, `GrabPass { }`
                        if let Some(Token::Str(_)) = self.peek() {
                            self.i += 1;
                        }
                        self.skip_block();
                    }
                    other => self.state(other, &mut states),
                },
                _ => {}
            }
        }
        shader.states = match chosen {
            Some(pass) => states.overlay(&pass),
            None => states,
        };
    }

    fn pass(&mut self) -> States {
        let mut states = States::default();
        if self.next() != Some(Token::Open) {
            return states;
        }
        while let Some(t) = self.next() {
            match t {
                Token::Close => break,
                Token::Open => {
                    self.i -= 1;
                    self.skip_block();
                }
                Token::Word(w) => self.state(&w, &mut states),
                _ => {}
            }
        }
        states
    }

    /// One render state command, its keyword already read. Unknown commands are skipped: their
    /// arguments are words or numbers the caller's loop passes over, and their blocks are skipped.
    fn state(&mut self, keyword: &str, states: &mut States) {
        match keyword {
            "Tags" => {
                if self.next() != Some(Token::Open) {
                    return;
                }
                while let Some(t) = self.next() {
                    match t {
                        Token::Close => break,
                        Token::Str(k) => {
                            if self.peek() == Some(&Token::Eq) {
                                self.i += 1;
                                if let Some(Token::Str(v)) = self.next() {
                                    states.tags.insert(k, v);
                                }
                            }
                        }
                        _ => {}
                    }
                }
            }
            "Blend" => {
                // An optional render target index comes first: `Blend 1 One Zero`.
                if let Some(Token::Num(_)) = self.peek() {
                    if matches!(self.t.get(self.i + 1), Some(Token::Word(_) | Token::Prop(_))) {
                        self.i += 1;
                    }
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
            "ZTest" => states.ztest = self.value(),
            "Cull" => states.cull = self.value(),
            "ColorMask" => {
                states.color_mask = self.value();
                // Optional render target index.
                if let Some(Token::Num(_)) = self.peek() {
                    if matches!(states.color_mask, Some(Value::Word(_) | Value::Prop(_))) {
                        self.i += 1;
                    }
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
            "Fog" | "Material" | "SetTexture" | "BindChannels" => {
                if let Some(Token::Prop(_)) = self.peek() {
                    self.i += 1;
                }
                self.skip_block();
            }
            _ => {}
        }
    }
}

/// The project's shaders by name (Assets and packages), parsed on first use.
#[derive(Default)]
pub struct ShaderIndex {
    files: HashMap<String, PathBuf>,
    parsed: HashMap<String, Option<ShaderLab>>,
}

impl ShaderIndex {
    /// Indexes every `.shader` file below the roots.
    pub fn scan(roots: &[PathBuf]) -> ShaderIndex {
        let mut files = HashMap::new();
        for root in roots {
            scan_dir(root, &mut files);
        }
        tracing::debug!("indexed {} project shaders", files.len());
        ShaderIndex {
            files,
            parsed: HashMap::new(),
        }
    }

    pub fn len(&self) -> usize {
        self.files.len()
    }

    pub fn is_empty(&self) -> bool {
        self.files.is_empty()
    }

    pub fn get(&mut self, name: &str) -> Option<&ShaderLab> {
        if !self.parsed.contains_key(name) {
            let parsed = self.files.get(name).and_then(|path| {
                let text = std::fs::read_to_string(path).ok()?;
                ShaderLab::parse(&text)
            });
            self.parsed.insert(name.to_string(), parsed);
        }
        self.parsed.get(name).and_then(Option::as_ref)
    }
}

fn scan_dir(dir: &Path, files: &mut HashMap<String, PathBuf>) {
    let Ok(entries) = std::fs::read_dir(dir) else {
        return;
    };
    for entry in entries.flatten() {
        let path = entry.path();
        let Ok(kind) = entry.file_type() else { continue };
        if kind.is_dir() {
            let name = entry.file_name();
            let name = name.to_string_lossy();
            // Unity ignores hidden folders and `~` folders.
            if name.starts_with('.') || name.ends_with('~') {
                continue;
            }
            scan_dir(&path, files);
        } else if path.extension().is_some_and(|e| e == "shader") {
            if let Some(name) = std::fs::read_to_string(&path)
                .ok()
                .and_then(|t| shader_name(&t))
            {
                files.entry(name).or_insert(path);
            }
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
    fn parses_states_and_properties() {
        let s = ShaderLab::parse(TMP_LIKE).unwrap();
        assert_eq!(s.name, "TextMeshPro/Mobile/Distance Field");
        assert_eq!(
            s.property("_FaceColor"),
            Some(&PropertyDefault::Vector([1.0; 4]))
        );
        assert_eq!(s.property("_StencilComp"), Some(&PropertyDefault::Float(8.0)));
        assert_eq!(
            s.property("_MainTex"),
            Some(&PropertyDefault::Texture("white".into()))
        );
        assert_eq!(s.property("_Ratio"), Some(&PropertyDefault::Float(-0.5)));
        assert_eq!(s.states.zwrite, Some(Value::Word("Off".into())));
        assert_eq!(
            s.states.blend,
            Some(vec![
                Value::Word("One".into()),
                Value::Word("OneMinusSrcAlpha".into())
            ])
        );
        assert_eq!(s.states.ztest, Some(Value::Prop("unity_GUIZTestMode".into())));
        assert_eq!(s.states.tags.get("Queue").map(String::as_str), Some("Transparent"));
        let stencil = s.states.stencil.unwrap();
        assert_eq!(stencil.comp, Some(Value::Prop("_StencilComp".into())));
        assert_eq!(s.pass_count, 1);
    }

    #[test]
    fn separate_alpha_blend_and_categories() {
        let s = ShaderLab::parse(
            r#"Shader "Legacy Shaders/Particles/Additive" {
            Properties { _TintColor ("Tint Color", Color) = (0.5,0.5,0.5,0.5) }
            Category {
                Tags { "Queue"="Transparent" }
                Blend SrcAlpha One, Zero One
                ColorMask RGB
                Cull Off Lighting Off ZWrite Off
                SubShader { Pass { } }
            }
            }"#,
        )
        .unwrap();
        assert_eq!(s.states.blend.as_ref().map(Vec::len), Some(4));
        assert_eq!(s.states.color_mask, Some(Value::Word("RGB".into())));
        assert_eq!(s.states.cull, Some(Value::Word("Off".into())));
        assert_eq!(s.states.zwrite, Some(Value::Word("Off".into())));
    }
}
