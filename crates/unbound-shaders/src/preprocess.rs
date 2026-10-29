//! A C preprocessor for shader programs, as Unity's shader compiler preprocesses them:
//! `#include` (through a resolver), object- and function-like macros (with `#`, `##` and
//! `__VA_ARGS__`), `#if` expressions where undefined names are 0, and `#pragma` lines collected
//! for the caller. glslang's own preprocessor rejects some of what Unity's includes rely on (an
//! undefined name inside an `#if` expression), so programs reach glslang already preprocessed.
//!
//! Macro expansion follows Prosser's algorithm: every token carries the set of macros it came
//! from, which keeps self-referencing macros from recursing.
use anyhow::{Result, anyhow, bail};
use std::{
    collections::{HashMap, HashSet, VecDeque},
    path::{Path, PathBuf},
    rc::Rc,
};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum Kind {
    Ident,
    Number,
    /// A string or character literal.
    Str,
    Punct,
    Space,
    Newline,
}

#[derive(Debug, Clone)]
pub(crate) struct Tok {
    pub(crate) kind: Kind,
    pub(crate) text: Rc<str>,
    /// The macros this token was expanded from (Prosser's hide set).
    hide: Rc<Vec<Rc<str>>>,
}

impl Tok {
    fn new(kind: Kind, text: &str) -> Tok {
        Tok {
            kind,
            text: text.into(),
            hide: Rc::new(Vec::new()),
        }
    }

    pub(crate) fn is(&self, text: &str) -> bool {
        self.kind == Kind::Punct && &*self.text == text
    }

    pub(crate) fn blank(&self) -> bool {
        matches!(self.kind, Kind::Space | Kind::Newline)
    }
}

#[derive(Debug, Clone)]
struct Macro {
    /// `None` for an object-like macro.
    params: Option<Vec<String>>,
    variadic: bool,
    body: Vec<Tok>,
}

/// A `#pragma` line of the program (not of the files it includes).
#[derive(Debug, Clone, PartialEq)]
pub struct Pragma {
    pub words: Vec<String>,
}

/// Finds an included file: `(including file's directory, name)` → its path and text.
pub type Resolve<'a> = dyn Fn(Option<&Path>, &str) -> Option<(PathBuf, String)> + 'a;

pub struct Output {
    pub text: String,
    /// `#pragma` lines of the top-level source, in order (active branches only).
    pub pragmas: Vec<Pragma>,
}

pub struct Preprocessor<'a> {
    macros: HashMap<String, Macro>,
    resolve: &'a Resolve<'a>,
    once: HashSet<PathBuf>,
    pragmas: Vec<Pragma>,
    depth: usize,
    /// Expanding an `#if` expression: `defined` that macros produce is answered too (as GCC and
    /// Unity's compilers do; Unity's includes rely on it).
    in_condition: bool,
}

const MAX_INCLUDE_DEPTH: usize = 64;

impl<'a> Preprocessor<'a> {
    pub fn new(resolve: &'a Resolve<'a>) -> Preprocessor<'a> {
        Preprocessor {
            macros: HashMap::new(),
            resolve,
            once: HashSet::new(),
            pragmas: Vec::new(),
            depth: 0,
            in_condition: false,
        }
    }

    /// `#define name value` before the source.
    pub fn define(&mut self, name: &str, value: &str) {
        self.macros.insert(
            name.to_string(),
            Macro {
                params: None,
                variadic: false,
                body: trim(tokenize(value)),
            },
        );
    }

    pub fn run(mut self, source: &str, file: Option<&Path>) -> Result<Output> {
        let mut out = String::with_capacity(source.len() * 4);
        self.file(source, file, true, &mut out)?;
        Ok(Output {
            text: out,
            pragmas: self.pragmas,
        })
    }

    fn file(
        &mut self,
        source: &str,
        file: Option<&Path>,
        top: bool,
        out: &mut String,
    ) -> Result<()> {
        let where_ = || file.map_or("<program>".to_string(), |f| f.display().to_string());
        let lines = logical_lines(source);
        // (active, a branch was taken, the enclosing region is active)
        let mut conds: Vec<(bool, bool, bool)> = Vec::new();
        let mut pending: Vec<Tok> = Vec::new();
        let active = |c: &Vec<(bool, bool, bool)>| c.last().is_none_or(|t| t.0);
        for (number, line) in lines.iter().enumerate() {
            let trimmed = line.trim_start();
            if let Some(directive) = trimmed.strip_prefix('#') {
                // Text before the directive is expanded first: a macro call cannot span it.
                if !pending.is_empty() {
                    self.emit(std::mem::take(&mut pending), out)?;
                }
                let directive = directive.trim_start();
                let (name, rest) = split_word(directive);
                let rest = rest.trim();
                let on = active(&conds);
                let context = || format!("{}:{}", where_(), number + 1);
                match name {
                    "if" => {
                        let value = on
                            && self
                                .condition(rest)
                                .map_err(|e| anyhow!("{}: {e}", context()))?;
                        conds.push((value, value, on));
                    }
                    "ifdef" | "ifndef" => {
                        let defined = self.macros.contains_key(split_word(rest).0);
                        let value = on && (defined == (name == "ifdef"));
                        conds.push((value, value, on));
                    }
                    "elif" => {
                        let Some(top) = conds.last().copied() else {
                            bail!("{}: #elif without #if", context());
                        };
                        let value = top.2
                            && !top.1
                            && self
                                .condition(rest)
                                .map_err(|e| anyhow!("{}: {e}", context()))?;
                        *conds.last_mut().unwrap() = (value, top.1 || value, top.2);
                    }
                    "else" => {
                        let Some(top) = conds.last().copied() else {
                            bail!("{}: #else without #if", context());
                        };
                        let value = top.2 && !top.1;
                        *conds.last_mut().unwrap() = (value, true, top.2);
                    }
                    "endif" => {
                        if conds.pop().is_none() {
                            bail!("{}: #endif without #if", context());
                        }
                    }
                    _ if !on => {}
                    "define" => self
                        .define_line(rest)
                        .map_err(|e| anyhow!("{}: {e}", context()))?,
                    "undef" => {
                        self.macros.remove(split_word(rest).0);
                    }
                    "include" | "include_with_pragmas" => {
                        let include = self
                            .include_name(rest)
                            .map_err(|e| anyhow!("{}: {e}", context()))?;
                        let dir = file.and_then(Path::parent);
                        let Some((path, text)) = (self.resolve)(dir, &include) else {
                            bail!("{}: cannot find include file \"{include}\"", context());
                        };
                        if !self.once.contains(&path) {
                            if self.depth >= MAX_INCLUDE_DEPTH {
                                bail!("{}: includes nested too deep", context());
                            }
                            self.depth += 1;
                            // `#include_with_pragmas` makes the file's pragmas the program's.
                            let pragmas = name == "include_with_pragmas" && top;
                            self.file(&text, Some(&path), pragmas, out)?;
                            self.depth -= 1;
                        }
                    }
                    "pragma" => {
                        let words: Vec<String> =
                            rest.split_whitespace().map(str::to_string).collect();
                        match words.first().map(String::as_str) {
                            Some("once") => {
                                if let Some(f) = file {
                                    self.once.insert(f.to_path_buf());
                                }
                            }
                            // Matrix packing changes code generation: glslang applies it.
                            Some(w) if w.starts_with("pack_matrix") => {
                                out.push('#');
                                out.push_str(directive);
                            }
                            _ => {
                                if top {
                                    self.pragmas.push(Pragma { words });
                                }
                            }
                        }
                    }
                    "error" => bail!("{}: #error {rest}", context()),
                    _ => {} // #line, #warning, unknown
                }
                out.push('\n');
                continue;
            }
            if active(&conds) {
                pending.extend(tokenize(line));
                pending.push(Tok::new(Kind::Newline, "\n"));
            } else if pending.is_empty() {
                // Dropped, but the line count holds.
                out.push('\n');
            } else {
                pending.push(Tok::new(Kind::Newline, "\n"));
            }
        }
        if !pending.is_empty() {
            self.emit(pending, out)?;
        }
        if !conds.is_empty() {
            bail!("{}: unterminated #if", where_());
        }
        Ok(())
    }

    fn emit(&mut self, tokens: Vec<Tok>, out: &mut String) -> Result<()> {
        let expanded = self.expand(tokens.into())?;
        let mut previous: Option<Kind> = None;
        for t in expanded {
            // Tokens that came from different places must not merge into one.
            if let Some(p) = previous
                && needs_space(p, &t)
            {
                out.push(' ');
            }
            out.push_str(&t.text);
            previous = Some(t.kind);
        }
        Ok(())
    }

    fn include_name(&mut self, rest: &str) -> Result<String> {
        let rest = rest.trim();
        if let Some(s) = rest.strip_prefix('"') {
            return Ok(s.split('"').next().unwrap_or_default().to_string());
        }
        if let Some(s) = rest.strip_prefix('<') {
            return Ok(s.split('>').next().unwrap_or_default().to_string());
        }
        // `#include MACRO`
        let expanded = self.expand(trim(tokenize(rest)).into())?;
        let text: String = expanded.iter().map(|t| &*t.text).collect();
        let text = text.trim();
        Ok(text
            .trim_matches(|c| c == '"' || c == '<' || c == '>')
            .to_string())
    }

    fn define_line(&mut self, rest: &str) -> Result<()> {
        let tokens = tokenize(rest);
        let mut i = 0;
        let Some(name) = tokens.first().filter(|t| t.kind == Kind::Ident) else {
            bail!("#define without a name");
        };
        let name = name.text.to_string();
        i += 1;
        let mut params = None;
        let mut variadic = false;
        // A function-like macro's `(` follows its name directly.
        if tokens.get(i).is_some_and(|t| t.is("(")) {
            i += 1;
            let mut list = Vec::new();
            loop {
                match tokens.get(i) {
                    None => bail!("#define {name}: unterminated parameter list"),
                    Some(t) if t.is(")") => {
                        i += 1;
                        break;
                    }
                    Some(t) if t.is("...") => {
                        variadic = true;
                        list.push("__VA_ARGS__".to_string());
                    }
                    Some(t) if t.kind == Kind::Ident => list.push(t.text.to_string()),
                    _ => {}
                }
                i += 1;
            }
            params = Some(list);
        }
        let body = trim(tokens[i..].to_vec());
        self.macros.insert(
            name,
            Macro {
                params,
                variadic,
                body,
            },
        );
        Ok(())
    }

    /// Evaluates an `#if`/`#elif` expression.
    fn condition(&mut self, text: &str) -> Result<bool> {
        // `defined X` and `defined(X)` are answered before expansion.
        let tokens = tokenize(text);
        let mut resolved = Vec::new();
        let mut i = 0;
        while i < tokens.len() {
            let t = &tokens[i];
            if t.kind == Kind::Ident && &*t.text == "defined" {
                let mut j = i + 1;
                while tokens.get(j).is_some_and(Tok::blank) {
                    j += 1;
                }
                let paren = tokens.get(j).is_some_and(|t| t.is("("));
                if paren {
                    j += 1;
                    while tokens.get(j).is_some_and(Tok::blank) {
                        j += 1;
                    }
                }
                let Some(name) = tokens.get(j).filter(|t| t.kind == Kind::Ident) else {
                    bail!("`defined` without a name");
                };
                let value = self.macros.contains_key(&*name.text);
                j += 1;
                if paren {
                    while tokens.get(j).is_some_and(Tok::blank) {
                        j += 1;
                    }
                    if !tokens.get(j).is_some_and(|t| t.is(")")) {
                        bail!("`defined(` without `)`");
                    }
                    j += 1;
                }
                resolved.push(Tok::new(Kind::Number, if value { "1" } else { "0" }));
                i = j;
                continue;
            }
            resolved.push(t.clone());
            i += 1;
        }
        self.in_condition = true;
        let expanded = self.expand(resolved.into());
        self.in_condition = false;
        let expanded: Vec<Tok> = expanded?.into_iter().filter(|t| !t.blank()).collect();
        let mut eval = Eval { t: &expanded, i: 0 };
        let value = eval.ternary()?;
        if eval.i != expanded.len() {
            bail!(
                "unexpected `{}` in #if expression `{text}`",
                expanded[eval.i].text
            );
        }
        Ok(value != 0)
    }

    /// Expands macros in a token sequence (Prosser's algorithm).
    fn expand(&mut self, mut input: VecDeque<Tok>) -> Result<Vec<Tok>> {
        let mut out = Vec::new();
        let mut guard = 0usize;
        while let Some(t) = input.pop_front() {
            guard += 1;
            if guard > 10_000_000 {
                bail!("macro expansion does not terminate");
            }
            if self.in_condition && t.kind == Kind::Ident && &*t.text == "defined" {
                // `defined X` / `defined(X)` from a macro's body.
                let next = |input: &mut VecDeque<Tok>| {
                    while input.front().is_some_and(Tok::blank) {
                        input.pop_front();
                    }
                    input.pop_front()
                };
                let mut name = next(&mut input);
                let paren = name.as_ref().is_some_and(|t| t.is("("));
                if paren {
                    name = next(&mut input);
                }
                let Some(name) = name.filter(|t| t.kind == Kind::Ident) else {
                    bail!("`defined` without a name");
                };
                if paren && !next(&mut input).is_some_and(|t| t.is(")")) {
                    bail!("`defined(` without `)`");
                }
                let value = self.macros.contains_key(&*name.text);
                out.push(Tok::new(Kind::Number, if value { "1" } else { "0" }));
                continue;
            }
            if t.kind != Kind::Ident || t.hide.iter().any(|h| **h == *t.text) {
                out.push(t);
                continue;
            }
            let Some(m) = self.macros.get(&*t.text).cloned() else {
                out.push(t);
                continue;
            };
            let name: Rc<str> = t.text.clone();
            match &m.params {
                None => {
                    let mut hide = (*t.hide).clone();
                    hide.push(name.clone());
                    let hide = Rc::new(hide);
                    let body = self.paste(m.body.iter().map(|b| with_hide(b, &hide)).collect())?;
                    for b in body.into_iter().rev() {
                        input.push_front(b);
                    }
                }
                Some(params) => {
                    // The call's `(`, possibly on a later line.
                    let mut j = 0;
                    while input.get(j).is_some_and(Tok::blank) {
                        j += 1;
                    }
                    if !input.get(j).is_some_and(|t| t.is("(")) {
                        out.push(t);
                        continue;
                    }
                    let mut newlines = input
                        .iter()
                        .take(j)
                        .filter(|t| t.kind == Kind::Newline)
                        .count();
                    input.drain(..=j);
                    let (args, rparen, consumed_newlines) = collect_args(&mut input)
                        .ok_or_else(|| anyhow!("unterminated call of macro {name}"))?;
                    newlines += consumed_newlines;
                    let args = fit_args(args, params.len(), m.variadic)
                        .ok_or_else(|| anyhow!("macro {name} takes {} arguments", params.len()))?;
                    let mut hide: Vec<Rc<str>> = t
                        .hide
                        .iter()
                        .filter(|h| rparen.hide.contains(h))
                        .cloned()
                        .collect();
                    hide.push(name.clone());
                    let hide = Rc::new(hide);
                    let body = self.substitute(&m, params, &args, &hide)?;
                    for b in body.into_iter().rev() {
                        input.push_front(b);
                    }
                    // Lines the call spanned: kept after it so line numbers hold.
                    for _ in 0..newlines {
                        out.push(Tok::new(Kind::Newline, "\n"));
                    }
                }
            }
        }
        Ok(out)
    }

    /// A function-like macro's body with its arguments in place.
    fn substitute(
        &mut self,
        m: &Macro,
        params: &[String],
        args: &[Vec<Tok>],
        hide: &Rc<Vec<Rc<str>>>,
    ) -> Result<Vec<Tok>> {
        let index = |t: &Tok| {
            if t.kind != Kind::Ident {
                return None;
            }
            params.iter().position(|p| *p == *t.text)
        };
        let body = &m.body;
        let mut result: Vec<Tok> = Vec::new();
        let mut i = 0;
        while i < body.len() {
            let t = &body[i];
            // `# param`: the argument as a string literal.
            if t.is("#") {
                let mut j = i + 1;
                while body.get(j).is_some_and(Tok::blank) {
                    j += 1;
                }
                if let Some(p) = body.get(j).and_then(index) {
                    result.push(with_hide(&Tok::new(Kind::Str, &stringify(&args[p])), hide));
                    i = j + 1;
                    continue;
                }
            }
            if let Some(p) = index(t) {
                // An operand of `##` is the argument as written; otherwise fully expanded.
                let next_paste = body[i + 1..]
                    .iter()
                    .find(|t| !t.blank())
                    .is_some_and(|t| t.is("##"));
                let prev_paste = body[..i]
                    .iter()
                    .rev()
                    .find(|t| !t.blank())
                    .is_some_and(|t| t.is("##"));
                let arg = if next_paste || prev_paste {
                    args[p].clone()
                } else {
                    self.expand(args[p].clone().into())?
                };
                if arg.is_empty() && (next_paste || prev_paste) {
                    result.push(Tok::new(Kind::Space, "")); // a placemarker
                }
                result.extend(arg.iter().map(|a| with_hide(a, hide)));
                i += 1;
                continue;
            }
            result.push(with_hide(t, hide));
            i += 1;
        }
        self.paste(result)
    }

    /// Applies `##` operators.
    fn paste(&mut self, tokens: Vec<Tok>) -> Result<Vec<Tok>> {
        if !tokens.iter().any(|t| t.is("##")) {
            return Ok(tokens);
        }
        let mut out: Vec<Tok> = Vec::new();
        let mut i = 0;
        while i < tokens.len() {
            let t = &tokens[i];
            if t.is("##") {
                while out
                    .last()
                    .is_some_and(|t| t.kind == Kind::Space && !t.text.is_empty())
                {
                    out.pop();
                }
                let mut j = i + 1;
                while tokens
                    .get(j)
                    .is_some_and(|t| t.kind == Kind::Space && !t.text.is_empty())
                {
                    j += 1;
                }
                let left = out.pop();
                let right = tokens.get(j).cloned();
                let text = format!(
                    "{}{}",
                    left.as_ref().map_or("", |t| &*t.text),
                    right.as_ref().map_or("", |t| &*t.text)
                );
                let hide = left
                    .as_ref()
                    .or(right.as_ref())
                    .map(|t| t.hide.clone())
                    .unwrap_or_default();
                let mut joined = tokenize(&text);
                for tok in &mut joined {
                    tok.hide = hide.clone();
                }
                out.extend(joined);
                i = j + 1;
                continue;
            }
            out.push(t.clone());
            i += 1;
        }
        Ok(out)
    }
}

fn with_hide(t: &Tok, hide: &Rc<Vec<Rc<str>>>) -> Tok {
    let mut set = (**hide).clone();
    for h in t.hide.iter() {
        if !set.contains(h) {
            set.push(h.clone());
        }
    }
    Tok {
        kind: t.kind,
        text: t.text.clone(),
        hide: Rc::new(set),
    }
}

/// Arguments of a call whose `(` was consumed: each argument's tokens, the `)` and how many
/// newlines the call spanned.
fn collect_args(input: &mut VecDeque<Tok>) -> Option<(Vec<Vec<Tok>>, Tok, usize)> {
    let mut args = vec![Vec::new()];
    let mut depth = 0;
    let mut newlines = 0;
    while let Some(t) = input.pop_front() {
        if t.kind == Kind::Newline {
            newlines += 1;
            args.last_mut().unwrap().push(Tok::new(Kind::Space, " "));
            continue;
        }
        if t.is("(") {
            depth += 1;
        } else if t.is(")") {
            if depth == 0 {
                let args = args.into_iter().map(trim).collect();
                return Some((args, t, newlines));
            }
            depth -= 1;
        } else if t.is(",") && depth == 0 {
            args.push(Vec::new());
            continue;
        }
        args.last_mut().unwrap().push(t);
    }
    None
}

/// Matches a call's arguments to the parameters (variadic ones joined with commas).
fn fit_args(mut args: Vec<Vec<Tok>>, count: usize, variadic: bool) -> Option<Vec<Vec<Tok>>> {
    // `F()` passes one empty argument; a macro without parameters takes none.
    if count == 0 && args.len() == 1 && args[0].is_empty() {
        return Some(Vec::new());
    }
    if variadic && args.len() >= count {
        let rest = args.split_off(count - 1);
        let mut joined = Vec::new();
        for (k, a) in rest.into_iter().enumerate() {
            if k > 0 {
                joined.push(Tok::new(Kind::Punct, ","));
            }
            joined.extend(a);
        }
        args.push(joined);
        return Some(args);
    }
    if variadic && args.len() + 1 == count {
        args.push(Vec::new());
        return Some(args);
    }
    (args.len() == count).then_some(args)
}

fn stringify(tokens: &[Tok]) -> String {
    let mut s = String::from("\"");
    let mut space = false;
    for t in tokens {
        if t.blank() {
            space = true;
            continue;
        }
        if space && s.len() > 1 {
            s.push(' ');
        }
        space = false;
        for c in t.text.chars() {
            if t.kind == Kind::Str && (c == '"' || c == '\\') {
                s.push('\\');
            }
            s.push(c);
        }
    }
    s.push('"');
    s
}

fn trim(mut tokens: Vec<Tok>) -> Vec<Tok> {
    while tokens.last().is_some_and(Tok::blank) {
        tokens.pop();
    }
    let start = tokens
        .iter()
        .position(|t| !t.blank())
        .unwrap_or(tokens.len());
    tokens.drain(..start);
    tokens
}

fn needs_space(previous: Kind, next: &Tok) -> bool {
    let word = |k: Kind| matches!(k, Kind::Ident | Kind::Number);
    (word(previous) && word(next.kind)) || (previous == Kind::Punct && next.kind == Kind::Punct)
}

fn split_word(s: &str) -> (&str, &str) {
    let end = s
        .find(|c: char| !(c.is_ascii_alphanumeric() || c == '_'))
        .unwrap_or(s.len());
    (&s[..end], &s[end..])
}

/// Source lines with continuations joined and comments replaced by a space (a block comment's
/// newlines move after the line, so each line keeps its number).
fn logical_lines(source: &str) -> Vec<String> {
    let mut lines = Vec::new();
    let mut current = String::new();
    let mut in_block = false;
    let mut carried = 0usize;
    // A byte order mark starts some files (it is not part of the first line).
    let source = source
        .strip_prefix('\u{feff}')
        .unwrap_or(source)
        .replace("\r\n", "\n");
    let mut chars = source.chars().peekable();
    let mut in_string: Option<char> = None;
    while let Some(c) = chars.next() {
        if in_block {
            if c == '*' && chars.peek() == Some(&'/') {
                chars.next();
                in_block = false;
                current.push(' ');
            } else if c == '\n' {
                carried += 1;
            }
            continue;
        }
        if let Some(q) = in_string {
            current.push(c);
            if c == '\\' {
                if let Some(n) = chars.next() {
                    current.push(n);
                }
            } else if c == q || c == '\n' {
                in_string = None;
            }
            continue;
        }
        match c {
            '\\' if chars.peek() == Some(&'\n') => {
                chars.next();
                carried += 1;
            }
            '/' if chars.peek() == Some(&'/') => {
                // To the end of the line (continuations included).
                while let Some(&n) = chars.peek() {
                    if n == '\n' {
                        break;
                    }
                    chars.next();
                    if n == '\\' && chars.peek() == Some(&'\n') {
                        chars.next();
                        carried += 1;
                    }
                }
            }
            '/' if chars.peek() == Some(&'*') => {
                chars.next();
                in_block = true;
            }
            '"' | '\'' => {
                in_string = Some(c);
                current.push(c);
            }
            '\n' => {
                lines.push(std::mem::take(&mut current));
                for _ in 0..carried {
                    lines.push(String::new());
                }
                carried = 0;
            }
            _ => current.push(c),
        }
    }
    lines.push(current);
    lines
}

const PUNCTS: [&str; 24] = [
    "...", "<<=", ">>=", "##", "&&", "||", "==", "!=", "<=", ">=", "<<", ">>", "++", "--", "->",
    "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "::",
];

/// A line's (or a whole preprocessed text's) tokens, blanks included.
pub(crate) fn tokenize(line: &str) -> Vec<Tok> {
    let chars: Vec<char> = line.chars().collect();
    let mut tokens = Vec::new();
    let mut i = 0;
    while i < chars.len() {
        let c = chars[i];
        let start = i;
        let kind = if c == '\n' {
            i += 1;
            Kind::Newline
        } else if c.is_whitespace() || c == '\u{feff}' {
            let blank = |c: char| (c.is_whitespace() && c != '\n') || c == '\u{feff}';
            while i < chars.len() && blank(chars[i]) {
                i += 1;
            }
            Kind::Space
        } else if c.is_ascii_alphabetic() || c == '_' {
            while i < chars.len() && (chars[i].is_ascii_alphanumeric() || chars[i] == '_') {
                i += 1;
            }
            Kind::Ident
        } else if c.is_ascii_digit()
            || (c == '.' && chars.get(i + 1).is_some_and(char::is_ascii_digit))
        {
            // A pp-number: digits, letters, dots and signed exponents.
            i += 1;
            while i < chars.len() {
                let d = chars[i];
                let sign = (d == '+' || d == '-') && matches!(chars[i - 1], 'e' | 'E' | 'p' | 'P');
                if !(sign || d.is_ascii_alphanumeric() || d == '.' || d == '_') {
                    break;
                }
                i += 1;
            }
            Kind::Number
        } else if c == '"' || c == '\'' {
            i += 1;
            while i < chars.len() && chars[i] != c {
                if chars[i] == '\\' {
                    i += 1;
                }
                i += 1;
            }
            i = (i + 1).min(chars.len());
            Kind::Str
        } else {
            let rest: String = chars[i..chars.len().min(i + 3)].iter().collect();
            let len = PUNCTS
                .iter()
                .find(|p| rest.starts_with(**p))
                .map_or(1, |p| p.chars().count());
            i += len;
            Kind::Punct
        };
        // Every branch consumes a character: a token is never empty.
        debug_assert!(i > start);
        i = i.max(start + 1);
        let text: String = if kind == Kind::Space {
            // Whitespace (a byte order mark too) is a space for the compiler.
            " ".into()
        } else {
            chars[start..i].iter().collect()
        };
        tokens.push(Tok::new(kind, &text));
    }
    tokens
}

/// `#if` expression evaluation over expanded tokens (blanks removed).
struct Eval<'a> {
    t: &'a [Tok],
    i: usize,
}

impl Eval<'_> {
    fn peek(&self) -> Option<&Tok> {
        self.t.get(self.i)
    }

    fn eat(&mut self, punct: &str) -> bool {
        if self.peek().is_some_and(|t| t.is(punct)) {
            self.i += 1;
            true
        } else {
            false
        }
    }

    fn ternary(&mut self) -> Result<i64> {
        let c = self.binary(0)?;
        if self.eat("?") {
            let a = self.ternary()?;
            if !self.eat(":") {
                bail!("`?` without `:` in #if expression");
            }
            let b = self.ternary()?;
            return Ok(if c != 0 { a } else { b });
        }
        Ok(c)
    }

    fn binary(&mut self, min: u8) -> Result<i64> {
        let mut left = self.unary()?;
        while let Some(op) = self
            .peek()
            .filter(|t| t.kind == Kind::Punct)
            .map(|t| t.text.clone())
        {
            let Some(prec) = precedence(&op) else { break };
            if prec < min {
                break;
            }
            self.i += 1;
            let right = self.binary(prec + 1)?;
            left = match &*op {
                "||" => ((left != 0) || (right != 0)) as i64,
                "&&" => ((left != 0) && (right != 0)) as i64,
                "|" => left | right,
                "^" => left ^ right,
                "&" => left & right,
                "==" => (left == right) as i64,
                "!=" => (left != right) as i64,
                "<" => (left < right) as i64,
                ">" => (left > right) as i64,
                "<=" => (left <= right) as i64,
                ">=" => (left >= right) as i64,
                "<<" => left.wrapping_shl(right as u32),
                ">>" => left.wrapping_shr(right as u32),
                "+" => left.wrapping_add(right),
                "-" => left.wrapping_sub(right),
                "*" => left.wrapping_mul(right),
                "/" => {
                    if right == 0 {
                        bail!("division by zero in #if expression");
                    }
                    left / right
                }
                "%" => {
                    if right == 0 {
                        bail!("division by zero in #if expression");
                    }
                    left % right
                }
                _ => unreachable!(),
            };
        }
        Ok(left)
    }

    fn unary(&mut self) -> Result<i64> {
        let Some(t) = self.peek().cloned() else {
            bail!("incomplete #if expression");
        };
        self.i += 1;
        Ok(match t.kind {
            Kind::Punct => match &*t.text {
                "(" => {
                    let v = self.ternary()?;
                    if !self.eat(")") {
                        bail!("`(` without `)` in #if expression");
                    }
                    v
                }
                "!" => (self.unary()? == 0) as i64,
                "~" => !self.unary()?,
                "-" => self.unary()?.wrapping_neg(),
                "+" => self.unary()?,
                other => bail!("unexpected `{other}` in #if expression"),
            },
            Kind::Number => number(&t.text)?,
            // A name no macro defines is 0 (`true` too, as in C).
            Kind::Ident => 0,
            Kind::Str => t.text.chars().nth(1).map_or(0, |c| c as i64),
            _ => bail!("unexpected `{}` in #if expression", t.text),
        })
    }
}

fn precedence(op: &str) -> Option<u8> {
    Some(match op {
        "||" => 1,
        "&&" => 2,
        "|" => 3,
        "^" => 4,
        "&" => 5,
        "==" | "!=" => 6,
        "<" | ">" | "<=" | ">=" => 7,
        "<<" | ">>" => 8,
        "+" | "-" => 9,
        "*" | "/" | "%" => 10,
        _ => return None,
    })
}

fn number(text: &str) -> Result<i64> {
    let t = text.trim_end_matches(['u', 'U', 'l', 'L']);
    let parsed = if let Some(hex) = t.strip_prefix("0x").or_else(|| t.strip_prefix("0X")) {
        i64::from_str_radix(hex, 16).ok()
    } else if t.len() > 1 && t.starts_with('0') && t.chars().all(|c| c.is_ascii_digit()) {
        i64::from_str_radix(&t[1..], 8).ok()
    } else if let Ok(v) = t.parse::<i64>() {
        Some(v)
    } else {
        // Shader code compares floats in #if (`#if SHADER_TARGET > 3.0`): truncated.
        t.trim_end_matches(['f', 'F', 'h', 'H'])
            .parse::<f64>()
            .ok()
            .map(|f| f as i64)
    };
    parsed.ok_or_else(|| anyhow!("`{text}` is not a number"))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn run(source: &str, files: &[(&str, &str)]) -> Result<Output> {
        let files: HashMap<String, String> = files
            .iter()
            .map(|(n, t)| (n.to_string(), t.to_string()))
            .collect();
        let resolve = move |_: Option<&Path>, name: &str| {
            files.get(name).map(|t| (PathBuf::from(name), t.clone()))
        };
        Preprocessor::new(&resolve).run(source, None)
    }

    fn words(text: &str) -> String {
        text.split_whitespace().collect::<Vec<_>>().join(" ")
    }

    #[test]
    fn defined_from_a_macro_body() {
        let resolve = |_: Option<&Path>, _: &str| None;
        let pp = Preprocessor::new(&resolve);
        let out = pp
            .run(
                "#define A 1\n#define HAS_A (defined(A) && !defined(B))\n#if HAS_A\nyes\n#else\nno\n#endif\n",
                None,
            )
            .unwrap();
        assert!(
            out.text.contains("yes") && !out.text.contains("no"),
            "{}",
            out.text
        );
    }

    #[test]
    fn undefined_names_are_zero_and_conditions_nest() {
        let out = run(
            "#define API_R16 !(SHADER_API_VULKAN || SHADER_API_GLES)\n#if (API_R16)\nyes\n#else\nno\n#endif\n\
             #ifdef MISSING\n#if broken (\n#endif\nhidden\n#elif 1\nshown\n#endif",
            &[],
        )
        .unwrap();
        assert_eq!(words(&out.text), "yes shown");
    }

    #[test]
    fn function_macros_expand_like_c() {
        let out = run(
            "#define CAT(a, b) a ## b\n#define STR(x) #x\n#define F(x) x + 1\n#define G F\n#define REC REC + 1\n\
             #define V(fmt, ...) call(fmt, __VA_ARGS__)\n\
             CAT(tex, 2D) STR(a \"b\") G(2) REC V(1, 2, 3)\nF\n(4)",
            &[],
        )
        .unwrap();
        assert_eq!(
            words(&out.text),
            "tex2D \"a \\\"b\\\"\" 2 + 1 REC + 1 call(1, 2,3) 4 + 1"
        );
        // The call spanning two lines keeps the line count (every line ends with a newline).
        assert_eq!(out.text.matches('\n').count(), 9);
    }

    #[test]
    fn includes_pragmas_and_once() {
        let out = run(
            "#include \"a.cginc\"\n#include \"a.cginc\"\n#pragma vertex vert\nUSE",
            &[(
                "a.cginc",
                "#pragma once\n#pragma fragment ignored\n#define USE used\n",
            )],
        )
        .unwrap();
        assert_eq!(words(&out.text), "used");
        assert_eq!(
            out.pragmas,
            vec![Pragma {
                words: vec!["vertex".into(), "vert".into()]
            }]
        );
        assert!(run("#include \"missing.cginc\"", &[]).is_err());
        assert!(run("#error stop", &[]).is_err());
    }

    /// `#include_with_pragmas` adds the file's pragmas to the program's (URP's 2D shaders take
    /// their light texture keywords from ShapeLightShared.hlsl this way); `#include` does not.
    #[test]
    fn include_with_pragmas_takes_the_files_pragmas() {
        let file = ("lights.hlsl", "#pragma multi_compile USE_A __\n");
        let out = run("#include_with_pragmas \"lights.hlsl\"\n", &[file]).unwrap();
        assert_eq!(
            out.pragmas,
            vec![Pragma {
                words: vec!["multi_compile".into(), "USE_A".into(), "__".into()]
            }]
        );
        assert!(run("#include \"lights.hlsl\"\n", &[file]).unwrap().pragmas.is_empty());
    }

    #[test]
    fn a_byte_order_mark_is_not_text() {
        let out = run("\u{feff}#define A 1\nA \u{feff}B", &[]).unwrap();
        assert_eq!(words(&out.text), "1 B");
    }

    #[test]
    fn comments_and_continuations_keep_line_numbers() {
        let out = run("a /* one\ntwo */ b\n#define X \\\n  x\nX // c\nlast", &[]).unwrap();
        let lines: Vec<&str> = out.text.lines().collect();
        assert_eq!(lines.len(), 6);
        assert_eq!(words(lines[4]), "x");
        assert_eq!(lines[5], "last");
    }
}
