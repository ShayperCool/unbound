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

fn strip_blocks(source: &str) -> (String, Vec<Block>) {
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
pub struct PassInfo {
    /// `Name "..."` (empty when unnamed).
    pub name: String,
    /// The SubShader's states with the pass's on top.
    pub states: States,
    /// `UsePass "Shader/PASS"`: the states come from that pass (resolved by [`ShaderIndex`]).
    pub use_pass: Option<String>,
    /// The pass's program (with the include blocks before it), when it has a CG or HLSL one.
    pub program: Option<PassProgram>,
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
}

impl PassInfo {
    /// Whether the pass draws in forward rendering (not a shadow caster, meta, ... pass).
    pub fn draws(&self) -> bool {
        if let Some(mode) = self.states.tags.get("LightMode") {
            return !matches!(
                mode.to_ascii_lowercase().as_str(),
                "shadowcaster"
                    | "meta"
                    | "deferred"
                    | "prepassbase"
                    | "prepassfinal"
                    | "motionvectors"
                    | "depthonly"
                    | "forwardadd"
            );
        }
        let name = self
            .use_pass
            .as_deref()
            .unwrap_or(&self.name)
            .to_ascii_uppercase();
        !(name.ends_with("CASTER") || name.ends_with("SHADOWCASTER") || name.ends_with("META"))
    }
}

#[derive(Debug, Clone, Default)]
pub struct ShaderLab {
    pub name: String,
    pub properties: Vec<(String, PropertyDefault)>,
    /// Every pass of the first SubShader (surface shaders: their generated forward pass).
    pub passes: Vec<PassInfo>,
    /// The states of the pass that is drawn: the one named `NORMAL` when there is one (Spine's
    /// outline shaders), else the first drawing pass.
    pub states: States,
    /// Drawing passes: only the main one is drawn.
    pub pass_count: usize,
    /// The drawn pass (an index into `passes`).
    pub main_pass: Option<usize>,
    /// `Color` properties (not `[Gamma]`): converted to linear in linear color space.
    pub colors: std::collections::HashSet<String>,
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
        let mut found_subshader = false;
        p.body(&mut shader, &States::default(), &mut found_subshader);
        if !shader.passes.iter().any(PassInfo::draws)
            && let Some(blend) = surface_blend(source)
        {
            // A surface shader: Unity generates the forward pass from `#pragma surface`; its
            // alpha options blend (and stop writing depth) unless the SubShader says otherwise.
            let mut states = shader.states.clone();
            if states.blend.is_none() && blend.is_some() {
                states.blend = blend;
                if states.zwrite.is_none() {
                    states.zwrite = Some(Value::Word("Off".into()));
                }
            }
            shader.passes.insert(
                0,
                PassInfo {
                    name: "FORWARD".into(),
                    states,
                    use_pass: None,
                    program: None,
                },
            );
        }
        shader.select_main();
        Some(shader)
    }

    /// Picks the drawn pass and its states.
    pub fn select_main(&mut self) {
        let drawing: Vec<&PassInfo> = self.passes.iter().filter(|p| p.draws()).collect();
        self.pass_count = drawing.len();
        let is_normal = |p: &&PassInfo| {
            let name = p.use_pass.as_deref().map_or(p.name.as_str(), |u| {
                u.rsplit_once('/').map_or(u, |(_, pass)| pass)
            });
            name.eq_ignore_ascii_case("normal")
        };
        let main = drawing.iter().find(|p| is_normal(p)).or(drawing.first());
        if let Some(main) = main {
            self.states = main.states.clone();
        }
        self.main_pass = main.and_then(|m| self.passes.iter().position(|p| std::ptr::eq(p, *m)));
    }
}

/// The blend a `#pragma surface` line's options imply (`None` inside: no blending); `None`
/// when the shader is not a surface shader.
fn surface_blend(source: &str) -> Option<Option<Vec<Value>>> {
    let line = source
        .lines()
        .map(str::trim)
        .find(|l| l.starts_with("#pragma") && l.split_whitespace().nth(1) == Some("surface"))?;
    let word = |w: &str| Value::Word(w.into());
    for option in line.split_whitespace().skip(4) {
        match option {
            "alpha" | "alpha:auto" | "alpha:fade" | "alpha:blend" | "decal:blend" => {
                return Some(Some(vec![word("SrcAlpha"), word("OneMinusSrcAlpha")]));
            }
            "alpha:premul" => return Some(Some(vec![word("One"), word("OneMinusSrcAlpha")])),
            "decal:add" => return Some(Some(vec![word("One"), word("One")])),
            _ => {}
        }
    }
    Some(None)
}

struct Parser<'a> {
    t: &'a [Token],
    i: usize,
    blocks: &'a [Block],
    /// Include blocks in scope (the shader's, then the SubShader's).
    includes: Vec<usize>,
    path: &'a Path,
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
                Token::Word(w) if self.block(&w).is_some_and(|b| self.blocks[b].include) => {
                    self.includes.push(self.block(&w).unwrap());
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
        let mut gamma = false;
        loop {
            let Some(t) = self.next() else { return };
            match t {
                Token::Close => return,
                // Attributes: `[HideInInspector]`, `[Toggle(X)]` (tokenized as one Prop).
                Token::Prop(attribute) => {
                    gamma |= attribute.eq_ignore_ascii_case("gamma");
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
                    if self.peek() != Some(&Token::Eq) {
                        continue;
                    }
                    self.i += 1;
                    let kind = kind.to_ascii_lowercase();
                    if kind == "color" && !is_gamma {
                        shader.colors.insert(name.clone());
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

    fn subshader(&mut self, shader: &mut ShaderLab, outer: &States) {
        if self.next() != Some(Token::Open) {
            return;
        }
        let mut states = outer.clone();
        let scope = self.includes.len();
        let mut passes: Vec<(String, States, Option<String>, Option<PassProgram>)> = Vec::new();
        while let Some(t) = self.next() {
            match t {
                Token::Close => break,
                Token::Open => {
                    self.i -= 1;
                    self.skip_block();
                }
                Token::Word(w) if self.block(&w).is_some_and(|b| self.blocks[b].include) => {
                    self.includes.push(self.block(&w).unwrap());
                }
                Token::Word(w) => match w.as_str() {
                    "Pass" => {
                        let (name, pass, program) = self.pass();
                        passes.push((name, pass, None, program));
                    }
                    "UsePass" => {
                        if let Some(Token::Str(target)) = self.peek() {
                            let target = target.clone();
                            self.i += 1;
                            passes.push((String::new(), States::default(), Some(target), None));
                        }
                    }
                    "GrabPass" => self.skip_block(),
                    other => self.state(other, &mut states),
                },
                _ => {}
            }
        }
        self.includes.truncate(scope);
        // States set after a pass apply to it too: ShaderLab states are SubShader-wide.
        shader.passes = passes
            .into_iter()
            .map(|(name, pass, use_pass, program)| PassInfo {
                name,
                states: states.overlay(&pass),
                use_pass,
                program,
            })
            .collect();
        shader.states = states;
    }

    fn pass(&mut self) -> (String, States, Option<PassProgram>) {
        let mut states = States::default();
        let mut name = String::new();
        let mut program = None;
        if self.next() != Some(Token::Open) {
            return (name, states, program);
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
                        name = n.clone();
                        self.i += 1;
                    }
                }
                Token::Word(w) if self.block(&w).is_some() => {
                    let b = self.block(&w).unwrap();
                    if self.blocks[b].include {
                        self.includes.push(b);
                    } else {
                        program = self.program(b);
                    }
                }
                Token::Word(w) => self.state(&w, &mut states),
                _ => {}
            }
        }
        self.includes.truncate(scope);
        (name, states, program)
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
                        Token::Str(k) if self.peek() == Some(&Token::Eq) => {
                            self.i += 1;
                            if let Some(Token::Str(v)) = self.next() {
                                states.tags.insert(k, v);
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
            "ZTest" => states.ztest = self.value(),
            "Cull" => states.cull = self.value(),
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
            let mut parsed = self.load(name);
            if let Some(lab) = &mut parsed {
                self.resolve_use_passes(lab, 0);
            }
            self.parsed.insert(name.to_string(), parsed);
        }
        self.parsed.get(name).and_then(Option::as_ref)
    }

    fn load(&self, name: &str) -> Option<ShaderLab> {
        let path = self.files.get(name)?;
        let text = std::fs::read_to_string(path).ok()?;
        ShaderLab::parse_file(&text, path)
    }

    /// Replaces `UsePass` passes' states by the named passes' (`"Shader/PASS"`, pass names
    /// compared case-insensitively as Unity does).
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
                pass.states = found.states.clone();
                pass.program = found.program.clone();
            }
        }
        lab.select_main();
    }
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
        let main = &lab.passes[lab.main_pass.unwrap()];
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
                && !lab.colors.contains("_Raw")
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
        assert_eq!(s.states.zwrite, Some(Value::Word("Off".into())));
        assert_eq!(
            s.states.blend,
            Some(vec![
                Value::Word("One".into()),
                Value::Word("OneMinusSrcAlpha".into())
            ])
        );
        assert_eq!(
            s.states.ztest,
            Some(Value::Prop("unity_GUIZTestMode".into()))
        );
        assert_eq!(
            s.states.tags.get("Queue").map(String::as_str),
            Some("Transparent")
        );
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

    #[test]
    fn normal_pass_and_surface_shaders() {
        let s = ShaderLab::parse(
            r#"Shader "Outline" {
            SubShader {
                Cull Off ZWrite Off Blend One OneMinusSrcAlpha
                Pass { Name "Outline" Blend SrcAlpha One }
                Pass { Name "Normal" }
                Pass { Name "Caster" Tags { "LightMode"="ShadowCaster" } }
            }}"#,
        )
        .unwrap();
        assert_eq!(s.passes.len(), 3);
        assert_eq!(s.pass_count, 2);
        assert_eq!(
            s.states.blend,
            Some(vec![
                Value::Word("One".into()),
                Value::Word("OneMinusSrcAlpha".into())
            ])
        );
        let surface = ShaderLab::parse(
            "Shader \"S\" { SubShader { Tags { \"Queue\"=\"Transparent\" }\nCGPROGRAM\n#pragma surface surf Lambert alpha:fade\nENDCG\n} }",
        )
        .unwrap();
        assert_eq!(surface.pass_count, 1);
        assert_eq!(
            surface.states.blend,
            Some(vec![
                Value::Word("SrcAlpha".into()),
                Value::Word("OneMinusSrcAlpha".into())
            ])
        );
        assert_eq!(surface.states.zwrite, Some(Value::Word("Off".into())));
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
            eprintln!(
                "{name}: blend {:?} zwrite {:?} ztest {:?} cull {:?} mask {:?} stencil {} passes {}",
                lab.states.blend,
                lab.states.zwrite,
                lab.states.ztest,
                lab.states.cull,
                lab.states.color_mask,
                lab.states.stencil.is_some(),
                lab.pass_count
            );
        }
    }
}
