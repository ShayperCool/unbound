//! HLSL scopes glslang does not read, rewritten into what it does (FXC and DXC, Unity's
//! compilers, read both):
//!
//! - namespaces (`namespace Threading { struct Wave ... }`, `Threading::Wave`): glslang neither
//!   finds a namespace's globals from its own functions nor reads a qualified type name. The
//!   namespaces are dropped and their qualifiers with them (two namespaces declaring one name
//!   then clash, which glslang reports).
//! - struct member functions declared in the struct and defined outside it
//!   (`uint Wave::GetIndex() { ... }`): glslang reads member functions defined in the struct
//!   only. Each definition replaces its declaration in the struct. Top-level declarations the
//!   moved bodies use that the struct comes before (a `groupshared` scratch array declared
//!   between the struct and its definitions) are moved up to the struct, with what they use in
//!   turn, unless they need the struct themselves.
//!
//! - calls of a struct's member functions from its own member functions without `this.`, which
//!   glslang resolves only when the arguments need no conversion (`And(v)` with a `bool` for
//!   `uint And(uint)` is "no matching overloaded function"): they are qualified with `this.`.
//!
//! core's Threading library (STP's compute kernels) uses all three.
//!
//! - `static` declarations in a cbuffer (`cbuffer UnityDOTSInstancing_X { uint M; static const
//!   int Mode = 1; }`): HLSL's compilers keep them out of the constant buffer, glslang makes them
//!   members of the block, which read zero. They move after the block, in their order. core's
//!   DOTS instancing macros declare each property's override mode so.

use std::collections::{BTreeSet, HashSet};
use std::ops::Range;

/// The source with namespaces and out-of-line member function definitions rewritten.
pub(crate) fn flatten_scopes(source: &str) -> String {
    qualify_member_calls(&inline_member_definitions(&flatten_namespaces(
        &hoist_cbuffer_statics(source),
    )))
}

/// `static` declarations of cbuffers moved out of them, right after each block.
fn hoist_cbuffer_statics(source: &str) -> String {
    if !source.contains("cbuffer") || !source.contains("static") {
        return source.to_string();
    }
    let toks = tokens(source);
    let is = |k: usize, s: &str| toks.get(k).is_some_and(|t| text(source, t) == s);
    let mut removed = Vec::new();
    let mut inserts = Vec::new();
    let mut k = 0;
    while k < toks.len() {
        if !(is(k, "cbuffer") && toks.get(k + 1).is_some_and(|t| t.kind == Kind::Word)) {
            k += 1;
            continue;
        }
        // The block's `{`, after any register binding.
        let mut open = k + 2;
        while open < toks.len() && !is(open, "{") && !is(open, ";") {
            open += 1;
        }
        let Some(close) = is(open, "{")
            .then(|| matching(source, &toks, open))
            .flatten()
        else {
            k = open + 1;
            continue;
        };
        let depth = toks[open].depth + 1;
        let mut moved = String::new();
        let mut m = open + 1;
        while m < close {
            // A member: its tokens up to its `;` at the block's depth.
            let mut end = m;
            while end < close && !(is(end, ";") && toks[end].depth == depth) {
                end += 1;
            }
            if is(m, "static") && end < close {
                removed.push(toks[m].start..toks[end].end);
                moved.push('\n');
                moved.push_str(&source[toks[m].start..toks[end].end]);
            }
            m = end + 1;
        }
        if !moved.is_empty() {
            // After the block and its optional `;`.
            let after = if is(close + 1, ";") { close + 1 } else { close };
            moved.push('\n');
            inserts.push((toks[after].end, moved));
        }
        k = close + 1;
    }
    if removed.is_empty() {
        return source.to_string();
    }
    edit(source, removed, inserts)
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Kind {
    /// An identifier or a number.
    Word,
    /// One punctuation byte, or `::`.
    Punct,
    /// A string literal.
    Text,
}

#[derive(Debug, Clone, Copy)]
struct Token {
    kind: Kind,
    start: usize,
    end: usize,
    /// Brace depth before the token.
    depth: u32,
}

fn is_word_byte(b: u8) -> bool {
    b.is_ascii_alphanumeric() || b == b'_'
}

/// Tokens of the source, skipping whitespace, comments and preprocessor lines (glslang's
/// expanded text keeps `#line` lines).
fn tokens(source: &str) -> Vec<Token> {
    let bytes = source.as_bytes();
    let mut out = Vec::new();
    let (mut i, mut depth, mut line_start) = (0usize, 0u32, true);
    while i < bytes.len() {
        let b = bytes[i];
        if b == b'\n' {
            line_start = true;
            i += 1;
            continue;
        }
        if b.is_ascii_whitespace() {
            i += 1;
            continue;
        }
        if line_start && b == b'#' {
            while i < bytes.len() && bytes[i] != b'\n' {
                i += 1;
            }
            continue;
        }
        line_start = false;
        if bytes[i..].starts_with(b"//") {
            while i < bytes.len() && bytes[i] != b'\n' {
                i += 1;
            }
            continue;
        }
        if bytes[i..].starts_with(b"/*") {
            i = source[i + 2..]
                .find("*/")
                .map_or(bytes.len(), |e| i + 2 + e + 2);
            continue;
        }
        let start = i;
        let kind = if is_word_byte(b) {
            while i < bytes.len() && is_word_byte(bytes[i]) {
                i += 1;
            }
            Kind::Word
        } else if b == b'"' {
            i += 1;
            while i < bytes.len() && bytes[i] != b'"' && bytes[i] != b'\n' {
                i += if bytes[i] == b'\\' { 2 } else { 1 };
            }
            i = (i + 1).min(bytes.len());
            Kind::Text
        } else if bytes[i..].starts_with(b"::") {
            i += 2;
            Kind::Punct
        } else {
            // One character (a multi-byte one whole).
            i += source[i..].chars().next().map_or(1, char::len_utf8);
            Kind::Punct
        };
        if b == b'}' {
            depth = depth.saturating_sub(1);
        }
        out.push(Token {
            kind,
            start,
            end: i,
            depth,
        });
        if b == b'{' {
            depth += 1;
        }
    }
    out
}

fn text<'a>(source: &'a str, t: &Token) -> &'a str {
    &source[t.start..t.end]
}

/// The source without the byte ranges given (sorted, not overlapping), with `inserts` (byte
/// offset, text) added.
fn edit(source: &str, mut removed: Vec<Range<usize>>, mut inserts: Vec<(usize, String)>) -> String {
    removed.sort_by_key(|r| r.start);
    inserts.sort_by_key(|(at, _)| *at);
    let mut out = String::with_capacity(source.len());
    let mut at = 0usize;
    let mut inserts = inserts.into_iter().peekable();
    for r in removed {
        while let Some((i, _)) = inserts.peek().filter(|(i, _)| *i <= r.start) {
            out.push_str(&source[at.min(*i)..*i]);
            at = at.max(*i);
            out.push_str(&inserts.next().unwrap().1);
        }
        if r.start >= at {
            out.push_str(&source[at..r.start]);
        }
        at = at.max(r.end);
    }
    for (i, s) in inserts {
        if i >= at {
            out.push_str(&source[at..i]);
            at = i;
        }
        out.push_str(&s);
    }
    out.push_str(&source[at..]);
    out
}

/// Drops `namespace N {` with its closing brace, `using namespace N;` and every `N::`
/// qualifier of a namespace the source declares.
fn flatten_namespaces(source: &str) -> String {
    if !source.contains("namespace") {
        return source.to_string();
    }
    let toks = tokens(source);
    let word = |k: usize, w: &str| {
        toks.get(k)
            .is_some_and(|t| t.kind == Kind::Word && text(source, t) == w)
    };
    let punct = |k: usize, p: &str| {
        toks.get(k)
            .is_some_and(|t| t.kind == Kind::Punct && text(source, t) == p)
    };
    let mut names = HashSet::new();
    for k in 0..toks.len() {
        if word(k, "namespace")
            && toks.get(k + 1).is_some_and(|t| t.kind == Kind::Word)
            && punct(k + 2, "{")
        {
            names.insert(text(source, &toks[k + 1]));
        }
    }
    if names.is_empty() {
        return source.to_string();
    }
    let mut removed = Vec::new();
    // Per open brace: whether it opens a namespace.
    let mut braces: Vec<bool> = Vec::new();
    let mut k = 0;
    while k < toks.len() {
        let t = toks[k];
        if word(k, "namespace") && punct(k + 2, "{") && names.contains(text(source, &toks[k + 1])) {
            removed.push(t.start..toks[k + 2].end);
            braces.push(true);
            k += 3;
            continue;
        }
        if word(k, "using") && word(k + 1, "namespace") && punct(k + 3, ";") {
            removed.push(t.start..toks[k + 3].end);
            k += 4;
            continue;
        }
        if t.kind == Kind::Word && names.contains(text(source, &t)) && punct(k + 1, "::") {
            // `A::B::x`: each namespace qualifier; a struct's (`Wave::Init`) stays.
            removed.push(t.start..toks[k + 1].end);
            k += 2;
            continue;
        }
        if punct(k, "{") {
            braces.push(false);
        } else if punct(k, "}") && braces.pop() == Some(true) {
            removed.push(t.start..t.end);
        }
        k += 1;
    }
    edit(source, removed, Vec::new())
}

/// A top-level struct definition: its name and the token indices of its braces.
struct StructDef<'a> {
    name: &'a str,
    /// The token that starts the struct's top-level item (`struct`, or an attribute before it).
    item: usize,
    open: usize,
    close: usize,
}

/// The index of the token closing the bracket opened at `open`.
fn matching(source: &str, toks: &[Token], open: usize) -> Option<usize> {
    let (o, c) = match text(source, &toks[open]) {
        "(" => ("(", ")"),
        "[" => ("[", "]"),
        "{" => ("{", "}"),
        _ => return None,
    };
    let mut depth = 0i32;
    for (k, t) in toks.iter().enumerate().skip(open) {
        if t.kind != Kind::Punct {
            continue;
        }
        let s = text(source, t);
        if s == o {
            depth += 1;
        } else if s == c {
            depth -= 1;
            if depth == 0 {
                return Some(k);
            }
        }
    }
    None
}

/// The first token of the item that ends before token `k` at `depth` (after the previous `;`,
/// `{` or `}` at that depth).
fn item_start(source: &str, toks: &[Token], k: usize, depth: u32) -> usize {
    let mut j = k;
    while j > 0 {
        let t = &toks[j - 1];
        if t.kind == Kind::Punct
            && matches!(text(source, t), ";" | "}" | "{")
            && (t.depth == depth || (text(source, t) == "{" && t.depth + 1 == depth))
        {
            break;
        }
        j -= 1;
    }
    j
}

/// A parameter list's types, for matching a declaration with its definition: per parameter its
/// tokens without input modifiers, default value, semantic and name.
fn parameter_types(source: &str, toks: &[Token], open: usize, close: usize) -> Vec<Vec<String>> {
    const MODIFIERS: [&str; 8] = [
        "in",
        "out",
        "inout",
        "uniform",
        "const",
        "precise",
        "row_major",
        "column_major",
    ];
    let mut params = Vec::new();
    let mut current: Vec<&Token> = Vec::new();
    let mut nesting = 0i32;
    let mut finish = |current: &mut Vec<&Token>| {
        let mut words: Vec<String> = Vec::new();
        let mut skip = false;
        for t in current.drain(..) {
            let s = text(source, t);
            if s == "=" || s == ":" {
                skip = true;
            }
            if skip || MODIFIERS.contains(&s) {
                continue;
            }
            words.push(s.to_string());
        }
        // A trailing array size belongs to the type; the name before it does not.
        let dims = words.iter().position(|w| w == "[").unwrap_or(words.len());
        let (head, tail) = words.split_at(dims);
        let mut head = head.to_vec();
        if head.len() >= 2 && head.last().is_some_and(|w| w.bytes().all(is_word_byte)) {
            head.pop();
        }
        head.extend_from_slice(tail);
        if !head.is_empty() {
            params.push(head);
        }
    };
    for t in &toks[open + 1..close] {
        let s = text(source, t);
        match s {
            "(" | "[" | "<" => nesting += 1,
            ")" | "]" | ">" => nesting -= 1,
            "," if nesting == 0 => {
                finish(&mut current);
                continue;
            }
            _ => {}
        }
        current.push(t);
    }
    finish(&mut current);
    params
}

/// A member function defined outside its struct: `ret S::m(params) { body }`.
struct OutOfLine<'a> {
    owner: &'a str,
    method: &'a str,
    /// Tokens: the item's first, the method name's, the parameter list's parentheses, the body's
    /// closing brace.
    item: usize,
    name: usize,
    open: usize,
    close: usize,
    end: usize,
}

/// A top-level declaration (variable, function, struct, cbuffer) between two positions.
#[derive(Clone, Copy)]
struct TopItem<'a> {
    first: usize,
    last: usize,
    name: Option<&'a str>,
}

/// Moves every member function defined outside its struct into the struct, in place of its
/// declaration, and moves the declarations those bodies use up to the struct.
fn inline_member_definitions(source: &str) -> String {
    if !source.contains("::") {
        return source.to_string();
    }
    let toks = tokens(source);
    let is = |k: usize, s: &str| toks.get(k).is_some_and(|t| text(source, t) == s);
    let word_at = |k: usize| {
        toks.get(k)
            .filter(|t| t.kind == Kind::Word)
            .map(|t| text(source, t))
    };

    let mut structs: Vec<StructDef> = Vec::new();
    for k in 0..toks.len() {
        if toks[k].depth == 0
            && is(k, "struct")
            && let Some(name) = word_at(k + 1)
            && is(k + 2, "{")
            && let Some(close) = matching(source, &toks, k + 2)
        {
            structs.push(StructDef {
                name,
                item: item_start(source, &toks, k, 0),
                open: k + 2,
                close,
            });
        }
    }
    let mut definitions: Vec<OutOfLine> = Vec::new();
    let mut k = 0;
    while k + 3 < toks.len() {
        if toks[k].depth == 0
            && let Some(owner) = word_at(k)
            && structs.iter().any(|s| s.name == owner)
            && is(k + 1, "::")
            && let Some(method) = word_at(k + 2)
            && is(k + 3, "(")
            && let Some(close) = matching(source, &toks, k + 3)
        {
            // An optional semantic, then the body.
            let mut body = close + 1;
            while body < toks.len() && !is(body, "{") && !is(body, ";") {
                body += 1;
            }
            if is(body, "{")
                && let Some(end) = matching(source, &toks, body)
            {
                definitions.push(OutOfLine {
                    owner,
                    method,
                    item: item_start(source, &toks, k, 0),
                    name: k + 2,
                    open: k + 3,
                    close,
                    end,
                });
                k = end + 1;
                continue;
            }
        }
        k += 1;
    }
    if definitions.is_empty() {
        return source.to_string();
    }

    let mut removed: Vec<Range<usize>> = Vec::new();
    let mut inserts: Vec<(usize, String)> = Vec::new();
    let mut moved_any = false;
    // Per struct that receives bodies: the identifiers they use, and the last definition moved.
    let mut uses: Vec<(usize, BTreeSet<&str>, usize)> = Vec::new();
    for (s_index, s) in structs.iter().enumerate() {
        // The struct's member declarations: `ret m(params) [: semantic];`.
        let member_depth = toks[s.open].depth + 1;
        let mut declarations: Vec<(usize, usize, usize, usize)> = Vec::new();
        for m in s.open + 1..s.close {
            if toks[m].depth == member_depth
                && toks[m].kind == Kind::Word
                && is(m + 1, "(")
                && let Some(close) = matching(source, &toks, m + 1)
            {
                let mut end = close + 1;
                while end < s.close
                    && !is(end, ";")
                    && !is(end, "{")
                    && toks[end].depth == member_depth
                {
                    end += 1;
                }
                if is(end, ";") {
                    declarations.push((item_start(source, &toks, m, member_depth), m, close, end));
                }
            }
        }
        let mut used: BTreeSet<&str> = BTreeSet::new();
        let mut last = 0usize;
        for d in definitions.iter().filter(|d| d.owner == s.name) {
            let types = parameter_types(source, &toks, d.open, d.close);
            let Some(&(first, _, _, semi)) = declarations.iter().find(|(_, m, close, _)| {
                text(source, &toks[*m]) == d.method
                    && parameter_types(source, &toks, m + 1, *close) == types
            }) else {
                continue;
            };
            // The return type (and modifiers): what comes before `S::`.
            let ret = &source[toks[d.item].start..toks[d.name - 2].start];
            let member = format!("{ret}{}", &source[toks[d.name].start..toks[d.end].end]);
            removed.push(toks[first].start..toks[semi].end);
            inserts.push((toks[first].start, member));
            removed.push(toks[d.item].start..toks[d.end].end);
            for t in &toks[d.open..d.end] {
                if t.kind == Kind::Word {
                    used.insert(text(source, t));
                }
            }
            last = last.max(d.end);
            moved_any = true;
        }
        if !used.is_empty() {
            uses.push((s_index, used, last));
        }
    }
    if !moved_any {
        return source.to_string();
    }

    // Top-level declarations between each struct and its last moved definition that the moved
    // bodies use (directly or through other moved declarations), unless they name a struct
    // that receives bodies: moved up, in order, before the first such struct.
    let receiving: HashSet<&str> = uses.iter().map(|(s, ..)| structs[*s].name).collect();
    let mut hoisted: BTreeSet<usize> = BTreeSet::new();
    let mut hoist_items: Vec<(usize, TopItem)> = Vec::new();
    for (s_index, used, last) in &uses {
        let s = &structs[*s_index];
        let items = top_items(source, &toks, s.close + 1, *last, &definitions);
        let mut wanted: BTreeSet<&str> = used.clone();
        loop {
            let mut grew = false;
            for item in &items {
                let Some(name) = item.name else { continue };
                if !wanted.contains(name) || hoisted.contains(&item.first) {
                    continue;
                }
                let words: Vec<&str> = toks[item.first..=item.last]
                    .iter()
                    .filter(|t| t.kind == Kind::Word)
                    .map(|t| text(source, t))
                    .collect();
                if words.iter().any(|w| receiving.contains(w)) {
                    continue;
                }
                hoisted.insert(item.first);
                hoist_items.push((s.item, *item));
                for w in words {
                    grew |= wanted.insert(w);
                }
            }
            if !grew {
                break;
            }
        }
    }
    hoist_items.sort_by_key(|(_, item)| item.first);
    for (at, item) in hoist_items {
        let range = toks[item.first].start..toks[item.last].end;
        inserts.push((toks[at].start, format!("{}\n", &source[range.clone()])));
        removed.push(range);
    }
    edit(source, removed, inserts)
}

/// `this.` before each call of a struct's member function in the bodies of its member functions.
fn qualify_member_calls(source: &str) -> String {
    if !source.contains("struct") {
        return source.to_string();
    }
    let toks = tokens(source);
    let is = |k: usize, s: &str| toks.get(k).is_some_and(|t| text(source, t) == s);
    let mut inserts = Vec::new();
    let mut k = 0;
    while k < toks.len() {
        let Some(close) = (is(k, "struct")
            && toks.get(k + 1).is_some_and(|t| t.kind == Kind::Word)
            && is(k + 2, "{"))
        .then(|| matching(source, &toks, k + 2))
        .flatten() else {
            k += 1;
            continue;
        };
        let member_depth = toks[k + 2].depth + 1;
        // The member functions: their names, and their bodies' token ranges.
        let mut methods: HashSet<&str> = HashSet::new();
        let mut bodies: Vec<(usize, usize)> = Vec::new();
        let mut m = k + 3;
        while m < close {
            if toks[m].depth == member_depth
                && toks[m].kind == Kind::Word
                && is(m + 1, "(")
                && let Some(params) = matching(source, &toks, m + 1)
            {
                let mut end = params + 1;
                while end < close && !is(end, ";") && !is(end, "{") {
                    end += 1;
                }
                // A static member function has no `this`: neither its body nor calls of it are
                // qualified.
                let first = item_start(source, &toks, m, member_depth);
                if toks[first..m].iter().any(|t| text(source, t) == "static") {
                    m = if is(end, "{") {
                        matching(source, &toks, end).map_or(end + 1, |b| b + 1)
                    } else {
                        end + 1
                    };
                    continue;
                }
                methods.insert(text(source, &toks[m]));
                if is(end, "{")
                    && let Some(body) = matching(source, &toks, end)
                {
                    bodies.push((end, body));
                    m = body + 1;
                    continue;
                }
                m = end + 1;
                continue;
            }
            m += 1;
        }
        for (open, end) in bodies {
            for c in open + 1..end {
                let t = &toks[c];
                if t.kind == Kind::Word
                    && methods.contains(text(source, t))
                    && is(c + 1, "(")
                    && !(is(c - 1, ".") || is(c - 1, "::"))
                {
                    inserts.push((t.start, "this.".to_string()));
                }
            }
        }
        k = close + 1;
    }
    if inserts.is_empty() {
        return source.to_string();
    }
    edit(source, Vec::new(), inserts)
}

/// The top-level items whose first token is in `from..to`, other than out-of-line member
/// definitions, with the name each declares.
fn top_items<'a>(
    source: &'a str,
    toks: &[Token],
    from: usize,
    to: usize,
    definitions: &[OutOfLine],
) -> Vec<TopItem<'a>> {
    let mut items = Vec::new();
    let mut k = from;
    while k < to.min(toks.len()) {
        if toks[k].depth != 0 || text(source, &toks[k]) == ";" {
            k += 1;
            continue;
        }
        // The item runs to its `;` at the top level, or to the brace closing its body (a
        // function, struct or cbuffer; `struct S { } s;` and initializer lists end at `;`).
        let mut j = k;
        let mut last = None;
        let mut name = None;
        while j < toks.len() {
            let s = text(source, &toks[j]);
            if s == ";" && toks[j].depth == 0 {
                last = Some(j);
                break;
            }
            if s == "(" && name.is_none() && j > k && toks[j - 1].kind == Kind::Word {
                name = Some(text(source, &toks[j - 1]));
            }
            if s == "{" && toks[j].depth == 0 {
                let Some(close) = matching(source, toks, j) else {
                    break;
                };
                let initializer = toks[k..j].iter().any(|t| text(source, t) == "=");
                if !initializer && toks.get(close + 1).is_none_or(|t| text(source, t) != ";") {
                    last = Some(close);
                    break;
                }
                j = close;
            }
            if matches!(s, "[" | "=" | ":")
                && name.is_none()
                && j > k
                && toks[j - 1].kind == Kind::Word
            {
                name = Some(text(source, &toks[j - 1]));
            }
            j += 1;
        }
        let Some(last) = last else { break };
        if name.is_none() && last > k {
            // `groupshared uint x;`: the word before the `;`.
            name = toks[k..last]
                .iter()
                .rev()
                .find(|t| t.kind == Kind::Word)
                .map(|t| text(source, t));
        }
        if !definitions.iter().any(|d| d.item == k) {
            items.push(TopItem {
                first: k,
                last,
                name,
            });
        }
        k = last + 1;
    }
    items
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn cbuffer_statics_move_after_the_block() {
        let source = "cbuffer UnityDOTSInstancing_Props : register(b2) {\n    uint Meta_A;\n    static const int Mode_A = 1;\n    uint Meta_B;\n    static const int Mode_B = Mode_A + 1;\n};\nfloat4 f() { return Mode_B; }\n";
        let fixed = hoist_cbuffer_statics(source);
        let block = &fixed[..fixed.find('}').unwrap()];
        assert!(
            block.contains("uint Meta_A;") && block.contains("uint Meta_B;"),
            "{fixed}"
        );
        assert!(!block.contains("static"), "{fixed}");
        let after = &fixed[fixed.find("};").unwrap()..];
        let (a, b) = (
            after.find("Mode_A = 1").unwrap(),
            after.find("Mode_B = Mode_A + 1").unwrap(),
        );
        assert!(a < b, "{fixed}");
        // glslang then reads the constant (2), not a block member (0): the fragment program
        // compiles to a constant colour.
        let members = |source: &str| -> Vec<String> {
            let program = format!("{source}float4 frag() : SV_Target {{ return f() + Meta_A; }}\n");
            let compiler = shaderc::Compiler::new().unwrap();
            let mut o = shaderc::CompileOptions::new().unwrap();
            o.set_source_language(shaderc::SourceLanguage::HLSL);
            let spirv = compiler
                .compile_into_spirv(
                    &program,
                    shaderc::ShaderKind::Fragment,
                    "t",
                    "frag",
                    Some(&o),
                )
                .unwrap();
            let module =
                naga::front::spv::parse_u8_slice(spirv.as_binary_u8(), &Default::default())
                    .unwrap();
            let block = module
                .global_variables
                .iter()
                .find_map(|(_, v)| match &module.types[v.ty].inner {
                    naga::TypeInner::Struct { members, .. } => Some(members.clone()),
                    _ => None,
                })
                .unwrap();
            block.iter().filter_map(|m| m.name.clone()).collect()
        };
        let simple = "cbuffer P { uint Meta_A; static const int Mode_A = 1; };\nfloat4 f() { return Mode_A; }\n";
        assert!(
            members(simple).iter().any(|m| m == "Mode_A"),
            "glslang now keeps statics out of cbuffers: the rewrite may be unnecessary"
        );
        assert_eq!(members(&fixed), ["Meta_A", "Meta_B"]);
        // Unchanged: a source without statics in a block.
        let plain = "cbuffer C { float4 x; };\nstatic const float k = 1;\n";
        assert_eq!(hoist_cbuffer_statics(plain), plain);
    }

    fn squash(s: &str) -> String {
        s.split_whitespace().collect::<Vec<_>>().join(" ")
    }

    #[test]
    fn namespaces_and_their_qualifiers_are_dropped() {
        let source = "namespace A { struct S { uint x; }; groupshared uint g[4]; uint f() { return g[0]; } }\n\
                      using namespace A;\n\
                      uint h(A::S s) { A::S t = (A::S)0; return A::f() + s.x + t.x; }";
        assert_eq!(
            squash(&flatten_namespaces(source)),
            squash(
                "struct S { uint x; }; groupshared uint g[4]; uint f() { return g[0]; }\n\
                 uint h(S s) { S t = (S)0; return f() + s.x + t.x; }"
            )
        );
        // Nested namespaces, and a source without any, are read alike.
        assert_eq!(
            squash(&flatten_namespaces(
                "namespace A { namespace B { int k; } } int m() { return A::B::k; }"
            )),
            "int k; int m() { return k; }"
        );
        let plain = "struct S { uint x; }; // namespace in a comment {\n";
        assert_eq!(flatten_namespaces(plain), plain);
    }

    #[test]
    fn member_definitions_move_into_their_struct() {
        let source = "struct Wave { uint i; uint Get(); void Init(uint g); uint Sum(uint v); int Sum(int v); };\n\
                      groupshared uint g_Scratch[8];\n\
                      static const uint K = 2;\n\
                      uint Twice(uint v) { return v * K; }\n\
                      uint Wave::Get() { return i; }\n\
                      void Wave::Init(uint groupIndex) { i = groupIndex; }\n\
                      int Wave::Sum(int v) { return v; }\n\
                      uint Wave::Sum(uint v) { return Twice(g_Scratch[v]); }\n\
                      uint Other(Wave w) { return w.Get(); }";
        let out = squash(&inline_member_definitions(source));
        assert_eq!(
            out,
            squash(
                "groupshared uint g_Scratch[8];\nstatic const uint K = 2;\nuint Twice(uint v) { return v * K; }\n\
                 struct Wave { uint i; uint Get() { return i; } void Init(uint groupIndex) { i = groupIndex; } \
                 uint Sum(uint v) { return Twice(g_Scratch[v]); } int Sum(int v) { return v; } };\n\
                 uint Other(Wave w) { return w.Get(); }"
            )
        );
    }

    /// Calls between member functions are qualified (in moved and in-struct bodies alike); a
    /// call of another object's member, a free function and a declaration are not.
    #[test]
    fn member_calls_are_qualified() {
        let source = "uint Free(uint v) { return v; }\n\
                      struct S { uint x; uint And(uint v) { return v & x; } \
                      bool All(bool v) { S o = (S)0; return And(v) != o.And(v) + Free(x); } };";
        assert_eq!(
            squash(&qualify_member_calls(source)),
            squash(
                "uint Free(uint v) { return v; }\n\
                 struct S { uint x; uint And(uint v) { return v & x; } \
                 bool All(bool v) { S o = (S)0; return this.And(v) != o.And(v) + Free(x); } };"
            )
        );
        // Static member functions have no `this`.
        let statics = "struct T { static uint One() { return 1u; } static uint Two() { return One() + One(); } };";
        assert_eq!(qualify_member_calls(statics), statics);
    }

    /// A declaration the bodies use that needs the struct stays where it is; declarations the
    /// bodies do not use stay too.
    #[test]
    fn declarations_needing_the_struct_stay() {
        let source = "struct S { uint x; uint F(); };\n\
                      uint Unused() { return 1; }\n\
                      uint Uses(S s) { return s.x; }\n\
                      uint S::F() { return Uses(this); }";
        let out = squash(&inline_member_definitions(source));
        assert_eq!(
            out,
            squash(
                "struct S { uint x; uint F() { return Uses(this); } };\n\
                 uint Unused() { return 1; }\nuint Uses(S s) { return s.x; }"
            )
        );
    }

    /// Glslang reads the result: Threading's shape (namespace, out-of-line members, scratch
    /// memory declared after the struct, a struct parameter of semantics for the kernel).
    #[test]
    fn glslang_reads_the_rewritten_threading_shape() {
        let source = r#"
#line 1 "Threading.hlsl"
namespace Threading
{
    struct Wave
    {
        uint indexG;
        uint GetIndex();
        void Init(uint groupIndex);
        uint Sum(uint v);
        float Sum(float v);
        uint And(uint v);
        bool AllTrue(bool v);
    };
    struct Group
    {
        uint groupIndex : SV_GroupIndex;
        uint3 groupID : SV_GroupID;
        Wave GetWave() { Wave wave = (Wave)0; wave.Init(groupIndex); return wave; }
        uint GetWaveCount();
    };
}
#line 1 "ThreadingEmuImpl.hlsl"
namespace Threading
{
    groupshared uint g_Scratch[64];
    uint Wave::GetIndex() { return indexG; }
    void Wave::Init(uint groupIndex) { indexG = groupIndex; }
    uint Wave::Sum(uint v) { GroupMemoryBarrierWithGroupSync(); g_Scratch[indexG] = v; GroupMemoryBarrierWithGroupSync(); return g_Scratch[0]; }
    float Wave::Sum(float v) { return asfloat(Sum(asuint(v))); }
    uint Wave::And(uint v) { return v & indexG; }
    bool Wave::AllTrue(bool v) { return And(v) != 0u; }
    uint Group::GetWaveCount() { return 2u; }
}
RWStructuredBuffer<uint> o;
[numthreads(64, 1, 1)]
void Kernel(Threading::Group group)
{
    Threading::Wave wave = group.GetWave();
    o[group.groupIndex] = wave.Sum(wave.GetIndex()) + group.GetWaveCount() + wave.AllTrue(true);
}
"#;
        let fixed = flatten_scopes(source);
        let compiler = shaderc::Compiler::new().unwrap();
        let mut o = shaderc::CompileOptions::new().unwrap();
        o.set_source_language(shaderc::SourceLanguage::HLSL);
        assert!(
            compiler
                .compile_into_spirv(
                    source,
                    shaderc::ShaderKind::Compute,
                    "t",
                    "Kernel",
                    Some(&o)
                )
                .is_err(),
            "glslang now reads namespaces: the rewrite may be unnecessary"
        );
        if let Err(e) = compiler.compile_into_spirv(
            &fixed,
            shaderc::ShaderKind::Compute,
            "t",
            "Kernel",
            Some(&o),
        ) {
            panic!("{e}\n{fixed}");
        }
    }
}
