//! Members of a struct with textures or samplers read straight from the call that returns it
//! (`Build(_T, sampler_T, ...).tex.Sample(...)`, `Build(...).GetTransformedUV(uv)`): HLSL resolves
//! a resource statically, so FXC (Unity's compiler) reads the struct the call built; glslang
//! samples only a texture it can name (it reports the texture as not found) and passes such a
//! temporary to a member function as a value of another type. It reads them from a local
//! variable, so each such call becomes a local of the function the call is in, declared where
//! its body starts, and the call's uses read the local; identical calls share one. A call whose
//! arguments name the function's own locals (whose values the start of the body does not have
//! yet), or another such call, is left to glslang.
//!
//! Shader Graph's texture slots that no node feeds read so (`UnityBuildTexture2DStructNoScale(t)`
//! expands to such a call of core's `UnityBuildTexture2DStructInternal`), in every target,
//! including the Visual Effect Graph's.

use crate::scopes::{Kind, Token, edit, matching, text, tokens};
use std::collections::{HashMap, HashSet};
use std::fmt::Write;

/// Types of the members glslang does not take from a temporary: the texture and sampler objects.
fn opaque_type(name: &str) -> bool {
    name.starts_with("Texture")
        || name.starts_with("RWTexture")
        || matches!(name, "SamplerState" | "SamplerComparisonState")
        || name.starts_with("sampler")
}

/// The prefix of the locals the calls become.
const LOCAL: &str = "unbound_opaque_";

/// The source with each such call's uses read from a local of its function.
pub(crate) fn opaque_members(source: &str) -> String {
    let toks = tokens(source);
    let structs = opaque_structs(source, &toks);
    if structs.is_empty() {
        return source.to_string();
    }
    let returning = functions_returning(source, &toks, &structs);
    if returning.is_empty() {
        return source.to_string();
    }
    let punct = |k: usize, p: &str| {
        toks.get(k)
            .is_some_and(|t| t.kind == Kind::Punct && text(source, t) == p)
    };
    let mut removed = Vec::new();
    let mut inserts = Vec::new();
    let mut count = 0usize;
    for body in bodies(source, &toks) {
        let mut locals = declared(source, &toks, body.open, body.close);
        locals.extend(written_parameters(source, &toks, body.params, body.open));
        // Per call text, its local.
        let mut hoisted: HashMap<String, String> = HashMap::new();
        let mut declarations = String::new();
        let mut k = body.open + 1;
        while k < body.close {
            let t = &toks[k];
            let returned = (t.kind == Kind::Word && punct(k + 1, "("))
                .then(|| returning.get(text(source, t)))
                .flatten();
            let close = returned.and_then(|_| matching(source, &toks, k + 1));
            let (Some(ty), Some(close)) = (returned, close) else {
                k += 1;
                continue;
            };
            let movable = punct(close + 1, ".")
                && (k + 2..close).all(|j| {
                    let a = &toks[j];
                    a.kind != Kind::Word
                        || !(locals.contains(text(source, a))
                            || (returning.contains_key(text(source, a)) && punct(j + 1, "(")))
                });
            if !movable {
                k += 1;
                continue;
            }
            // The call's text without layout, so calls written alike share their local.
            let call: String = (k..=close).fold(String::new(), |mut s, j| {
                s.push_str(text(source, &toks[j]));
                s.push(' ');
                s
            });
            let local = hoisted.entry(call).or_insert_with(|| {
                let local = format!("{LOCAL}{count}");
                count += 1;
                let _ = write!(
                    declarations,
                    " {ty} {local} = {};",
                    &source[t.start..toks[close].end]
                );
                local
            });
            removed.push(t.start..toks[close].end);
            inserts.push((t.start, local.clone()));
            k = close + 1;
        }
        if !declarations.is_empty() {
            inserts.push((toks[body.open].end, declarations));
        }
    }
    if removed.is_empty() {
        return source.to_string();
    }
    edit(source, removed, inserts)
}

/// Top-level structs with texture or sampler members.
fn opaque_structs<'a>(source: &'a str, toks: &[Token]) -> HashSet<&'a str> {
    let mut found = HashSet::new();
    for k in 0..toks.len() {
        let t = &toks[k];
        if t.depth != 0 || t.kind != Kind::Word || text(source, t) != "struct" {
            continue;
        }
        let (Some(name), Some(open)) = (toks.get(k + 1), toks.get(k + 2)) else {
            continue;
        };
        if name.kind != Kind::Word || text(source, open) != "{" {
            continue;
        }
        let Some(close) = matching(source, toks, k + 2) else {
            continue;
        };
        // A member declaration starts after the struct's `{`, a `;` or a member function's body.
        let mut first = true;
        for j in k + 3..close {
            let s = text(source, &toks[j]);
            if toks[j].depth != t.depth + 1 || s == "{" {
                continue;
            }
            if first && opaque_type(s) {
                found.insert(text(source, name));
                break;
            }
            first = matches!(s, ";" | "}");
        }
    }
    found
}

/// Top-level functions returning one of those structs: per name, the struct.
fn functions_returning<'a>(
    source: &'a str,
    toks: &[Token],
    structs: &HashSet<&'a str>,
) -> HashMap<&'a str, &'a str> {
    let mut found = HashMap::new();
    for k in 1..toks.len() {
        let (t, ty) = (&toks[k], &toks[k - 1]);
        if t.depth == 0
            && t.kind == Kind::Word
            && toks.get(k + 1).is_some_and(|p| text(source, p) == "(")
            && structs.contains(text(source, ty))
        {
            found.insert(text(source, t), text(source, ty));
        }
    }
    found
}

/// A top-level function's body: the token indices of its braces, and of its parameter list's
/// opening parenthesis.
struct Body {
    params: usize,
    open: usize,
    close: usize,
}

/// The bodies of the top-level functions (`type name(parameters) [: semantic] { ... }`).
fn bodies(source: &str, toks: &[Token]) -> Vec<Body> {
    let mut found = Vec::new();
    let mut k = 0;
    while k < toks.len() {
        let named = toks[k].depth == 0
            && text(source, &toks[k]) == "("
            && k >= 2
            && toks[k - 1].kind == Kind::Word
            && (toks[k - 2].kind == Kind::Word || text(source, &toks[k - 2]) == ">");
        let Some(close) = named.then(|| matching(source, toks, k)).flatten() else {
            k += 1;
            continue;
        };
        let mut after = close + 1;
        while after < toks.len() && !matches!(text(source, &toks[after]), "{" | ";" | "=" | "(") {
            after += 1;
        }
        match toks.get(after).map(|t| text(source, t)) {
            Some("{") => match matching(source, toks, after) {
                Some(end) => {
                    found.push(Body {
                        params: k,
                        open: after,
                        close: end,
                    });
                    k = end + 1;
                }
                None => k = after + 1,
            },
            _ => k = close + 1,
        }
    }
    found
}

/// A number, not a name.
fn is_number(source: &str, t: &Token) -> bool {
    text(source, t).starts_with(|c: char| c.is_ascii_digit())
}

/// The names a body declares (a word after a type or a template's `>`, before `=`, `;`, `,`, `[`,
/// `)` or `:`, and the further names of its declaration after its commas), or more: a name it may
/// declare is not read at its start.
fn declared<'a>(source: &'a str, toks: &[Token], open: usize, close: usize) -> HashSet<&'a str> {
    let mut found = HashSet::new();
    for k in open + 1..close {
        let (t, before) = (&toks[k], &toks[k - 1]);
        let typed = (before.kind == Kind::Word && text(source, before) != "return")
            || text(source, before) == ">";
        let next = toks.get(k + 1).map_or("", |n| text(source, n));
        if t.kind == Kind::Word
            && !is_number(source, t)
            && typed
            && matches!(next, "=" | ";" | "," | "[" | ")" | ":")
        {
            found.insert(text(source, t));
            further_names(source, toks, k + 1, close, &mut found);
        }
    }
    found
}

/// The names a declaration lists after its first (`float a = 0, b, c[2];`): the word after each of
/// its commas outside brackets, up to its `;`.
fn further_names<'a>(
    source: &'a str,
    toks: &[Token],
    from: usize,
    end: usize,
    found: &mut HashSet<&'a str>,
) {
    let mut nesting = 0i32;
    for j in from..end {
        match text(source, &toks[j]) {
            "(" | "[" | "{" => nesting += 1,
            ")" | "]" | "}" => {
                nesting -= 1;
                if nesting < 0 {
                    return;
                }
            }
            ";" if nesting == 0 => return,
            "," if nesting == 0 => {
                if let Some(name) = toks.get(j + 1).filter(|n| n.kind == Kind::Word) {
                    found.insert(text(source, name));
                }
            }
            _ => {}
        }
    }
}

/// The parameters a function writes (`out`, `inout`): their values at the start of the body are
/// not those the body later reads.
fn written_parameters<'a>(
    source: &'a str,
    toks: &[Token],
    open: usize,
    body: usize,
) -> HashSet<&'a str> {
    let mut found = HashSet::new();
    let mut written = false;
    for k in open + 1..body {
        let t = &toks[k];
        match text(source, t) {
            "out" | "inout" => written = true,
            "," | ")" => written = false,
            name if written
                && t.kind == Kind::Word
                && toks
                    .get(k + 1)
                    .is_some_and(|n| matches!(text(source, n), "," | ")" | ":" | "=" | "[")) =>
            {
                found.insert(name);
            }
            _ => {}
        }
    }
    found
}

#[cfg(test)]
mod tests {
    use super::*;

    const BUILDER: &str = "struct UT { Texture2D tex; SamplerState samplerstate; float4 st;\n\
        float2 Transformed(float2 uv) { return uv * st.xy + st.zw; } };\n\
        UT Build(Texture2D t, SamplerState s, float4 st) { UT r; r.tex = t; r.samplerstate = s; r.st = st; return r; }\n\
        Texture2D _T; SamplerState sampler_T; float4 _T_ST;\n";

    #[test]
    fn calls_read_through_become_one_local_at_the_start_of_their_function() {
        let source = format!(
            "{BUILDER}float4 F(float2 uv)\n{{\n    float4 c = Build(_T, sampler_T, _T_ST).tex.Sample(Build( _T, sampler_T, _T_ST ).samplerstate,\n\
             Build(_T, sampler_T, _T_ST).Transformed(uv));\n    if (Build(_T, sampler_T, float4(1, 1, 0, 0)).st.x > 0) c *= 2;\n    return c;\n}}\n"
        );
        let out = opaque_members(&source);
        assert!(
            out.ends_with(
                "float4 F(float2 uv)\n{ UT unbound_opaque_0 = Build(_T, sampler_T, _T_ST); UT unbound_opaque_1 = Build(_T, sampler_T, float4(1, 1, 0, 0));\n    \
                 float4 c = unbound_opaque_0.tex.Sample(unbound_opaque_0.samplerstate,\n\
                 unbound_opaque_0.Transformed(uv));\n    if (unbound_opaque_1.st.x > 0) c *= 2;\n    return c;\n}\n"
            ),
            "{out}"
        );
        // The builder and a call whose result is not read through stay.
        assert!(out.contains("UT Build(Texture2D t, SamplerState s, float4 st) { UT r;"));
        let whole = format!("{BUILDER}UT G() {{ return Build(_T, sampler_T, _T_ST); }}\n");
        assert_eq!(opaque_members(&whole), whole);
        // Nothing to change: the same text.
        assert_eq!(opaque_members(&out), out);
    }

    #[test]
    fn calls_of_the_functions_locals_or_of_other_such_calls_stay() {
        let local = format!(
            "{BUILDER}float4 F(float2 uv) {{ float4 st = float4(1, 1, 0, 0); return Build(_T, sampler_T, st).tex.Sample(sampler_T, uv); }}\n"
        );
        assert_eq!(opaque_members(&local), local);
        // A local declared after a comma, and a parameter the function writes.
        let listed = format!(
            "{BUILDER}float4 F(float2 uv) {{ float4 a = 0, st = float4(1, 1, 0, 0); return Build(_T, sampler_T, st).tex.Sample(sampler_T, uv); }}\n"
        );
        assert_eq!(opaque_members(&listed), listed);
        let written = format!(
            "{BUILDER}void F(float2 uv, inout float4 st, out float4 c) {{ st *= 2; c = Build(_T, sampler_T, st).tex.Sample(sampler_T, uv); }}\n"
        );
        assert_eq!(opaque_members(&written), written);
        let nested = format!(
            "{BUILDER}float4 F(float2 uv) {{ return Build(Build(_T, sampler_T, _T_ST).tex, sampler_T, _T_ST).tex.Sample(sampler_T, uv); }}\n"
        );
        let out = opaque_members(&nested);
        // The inner call moves; the outer one, which reads it, stays.
        assert!(
            out.ends_with("{ UT unbound_opaque_0 = Build(_T, sampler_T, _T_ST); return Build(unbound_opaque_0.tex, sampler_T, _T_ST).tex.Sample(sampler_T, uv); }\n"),
            "{out}"
        );
        // A struct without textures or samplers is glslang's to read.
        let plain = "struct P { float4 v; };\nP Make(float4 v) { P p; p.v = v; return p; }\nfloat4 F() { return Make(1).v; }\n";
        assert_eq!(opaque_members(plain), plain);
    }

    #[test]
    fn parameters_are_read_at_the_start_of_the_body() {
        let source = format!(
            "{BUILDER}float4 F(Texture2D t, float2 uv) {{ return Build(t, sampler_T, _T_ST).tex.Sample(sampler_T, uv); }}\n"
        );
        let out = opaque_members(&source);
        assert!(
            out.ends_with("{ UT unbound_opaque_0 = Build(t, sampler_T, _T_ST); return unbound_opaque_0.tex.Sample(sampler_T, uv); }\n"),
            "{out}"
        );
    }
}
