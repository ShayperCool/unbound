//! Parameters declared `inout bool` split into an `in` copy and an `out` one.
//!
//! FXC passes a number to an `inout bool` parameter by converting it to `bool` on the way in and
//! back on the way out. glslang crashes compiling such a call (the process with it), whatever the
//! number's type (`float`, `half`, `int`, `uint`): a Visual Effect Graph output drawn by a Shader
//! Graph keeps its particles' `alive` as a `float` and passes it to its blocks' `inout bool alive`.
//! It compiles an `in bool` and an `out bool` parameter given a number, with the same
//! conversions. So each such parameter of a function declared outside any braces becomes
//! `bool <name>_unbound_in, out bool <name>`, its body starting with `<name> = <name>_unbound_in;`,
//! and each call of the function passes the argument to both (an argument is an l-value; its
//! expression is written twice, so one with a side effect, such as `a[i++]`, has it twice).
//!
//! Left as they are (glslang may still crash on a number given to them): functions of a name
//! another declaration of which has no such parameter at that place (a call cannot be told apart
//! without the arguments' types), names also declared as a struct's member, and functions whose
//! parameters hold a template type (`vector<float, 3>`, whose commas this reading does not
//! follow).

use crate::scopes::{Kind, Token, edit, matching, text, tokens};
use std::collections::{BTreeMap, BTreeSet, HashSet};

/// Words before a name that make its parenthesis a call, not a declaration.
const NOT_TYPES: &[&str] = &[
    "return", "else", "do", "case", "in", "out", "inout", "uniform",
];

/// The suffix of the `in` copy of a split parameter.
const IN_SUFFIX: &str = "_unbound_in";

/// The name token of an `inout bool <name>` parameter starting at token `k`, a scalar `bool`
/// followed by the end of the parameter (`,`, `)`, a semantic or a default).
fn inout_bool_at(source: &str, toks: &[Token], k: usize) -> Option<usize> {
    let word = |j: usize| {
        toks.get(j)
            .filter(|t| t.kind == Kind::Word)
            .map(|t| text(source, t))
    };
    let punct = |j: usize| {
        toks.get(j)
            .filter(|t| t.kind == Kind::Punct)
            .map(|t| text(source, t))
    };
    if word(k) != Some("inout") || word(k + 1) != Some("bool") || word(k + 2).is_none() {
        return None;
    }
    matches!(punct(k + 3), Some("," | ")" | ":" | "=")).then_some(k + 2)
}

/// The top-level comma-separated parts of the bracket opened at token `open` and closed at
/// `close`: each part's first and last token (exclusive end).
fn parts(source: &str, toks: &[Token], open: usize, close: usize) -> Vec<(usize, usize)> {
    let mut out = Vec::new();
    let (mut depth, mut start) = (0i32, open + 1);
    for k in open + 1..close {
        if toks[k].kind != Kind::Punct {
            continue;
        }
        match text(source, &toks[k]) {
            "(" | "[" | "{" => depth += 1,
            ")" | "]" | "}" => depth -= 1,
            "," if depth == 0 => {
                out.push((start, k));
                start = k + 1;
            }
            _ => {}
        }
    }
    if start < close {
        out.push((start, close));
    }
    out
}

/// A function declaration or definition: its parameters' bracket, and the brace of its body.
struct Declaration {
    open: usize,
    close: usize,
    body: Option<usize>,
}

/// The program read for the rewrite: its tokens, the declarations outside any braces by name,
/// and the places of the split parameters of each name split.
struct Reading<'a> {
    toks: Vec<Token>,
    declarations: BTreeMap<&'a str, Vec<Declaration>>,
    split: BTreeMap<&'a str, Vec<usize>>,
}

fn read(source: &str) -> Reading<'_> {
    let toks = tokens(source);
    let word = |j: usize| {
        toks.get(j)
            .filter(|t| t.kind == Kind::Word)
            .map(|t| text(source, t))
    };
    let punct = |j: usize| {
        toks.get(j)
            .filter(|t| t.kind == Kind::Punct)
            .map(|t| text(source, t))
    };
    // Declarations: a type word, the name, a parenthesised list followed by a body, a `;` or a
    // semantic. Those outside any braces by name; the names of members (inside a struct's
    // braces, or defined as `Struct::name`) apart.
    let mut declarations: BTreeMap<&str, Vec<Declaration>> = BTreeMap::new();
    let mut members = BTreeSet::new();
    for k in 1..toks.len() {
        let Some(name) = word(k) else {
            continue;
        };
        if punct(k + 1) != Some("(") {
            continue;
        }
        let qualified = punct(k - 1) == Some("::");
        if !qualified && word(k - 1).is_none_or(|before| NOT_TYPES.contains(&before)) {
            continue;
        }
        let Some(close) = matching(source, &toks, k + 1) else {
            continue;
        };
        let after = punct(close + 1);
        if !matches!(after, Some("{" | ";" | ":")) {
            continue;
        }
        if qualified || toks[k].depth != 0 {
            members.insert(name);
            continue;
        }
        let body = if after == Some("{") {
            Some(close + 1)
        } else if after == Some(":") && punct(close + 3) == Some("{") {
            Some(close + 3)
        } else {
            None
        };
        declarations.entry(name).or_default().push(Declaration {
            open: k + 1,
            close,
            body,
        });
    }
    // The split parameters of each name: the places of its `inout bool` parameters, the same in
    // every declaration of the name, none of which holds a template type.
    let mut split = BTreeMap::new();
    for (&name, list) in &declarations {
        if members.contains(name) {
            continue;
        }
        let templated = list.iter().any(|d| {
            (d.open + 1..d.close)
                .any(|j| toks[j].kind == Kind::Punct && text(source, &toks[j]) == "<")
        });
        if templated {
            continue;
        }
        let places = |d: &Declaration| -> Vec<usize> {
            parts(source, &toks, d.open, d.close)
                .iter()
                .enumerate()
                .filter(|(_, (first, _))| inout_bool_at(source, &toks, *first).is_some())
                .map(|(i, _)| i)
                .collect()
        };
        let first = places(&list[0]);
        if !first.is_empty() && list.iter().all(|d| places(d) == first) {
            split.insert(name, first);
        }
    }
    Reading {
        toks,
        declarations,
        split,
    }
}

/// Whether the rewrite splits a parameter of the program (it then compiles rewritten).
pub(crate) fn splits(source: &str) -> bool {
    source.contains("inout") && !read(source).split.is_empty()
}

/// The program with each scalar `inout bool` parameter split and its calls passing the argument
/// twice (see the module's description); unchanged without such a parameter.
pub(crate) fn inout_bools(source: &str) -> String {
    if !source.contains("inout") {
        return source.to_string();
    }
    let Reading {
        toks,
        declarations,
        split,
    } = read(source);
    if split.is_empty() {
        return source.to_string();
    }
    let word = |j: usize| {
        toks.get(j)
            .filter(|t| t.kind == Kind::Word)
            .map(|t| text(source, t))
    };
    let punct = |j: usize| {
        toks.get(j)
            .filter(|t| t.kind == Kind::Punct)
            .map(|t| text(source, t))
    };
    let mut removed = Vec::new();
    let mut inserts = Vec::new();
    let mut declared = HashSet::new();
    for name in split.keys() {
        for d in &declarations[name] {
            declared.insert(d.open);
            let mut assignments = String::new();
            for (first, _) in parts(source, &toks, d.open, d.close) {
                let Some(id) = inout_bool_at(source, &toks, first) else {
                    continue;
                };
                let id = text(source, &toks[id]);
                // `inout bool alive` -> `bool alive_unbound_in, out bool alive`.
                removed.push(toks[first].start..toks[first + 1].end);
                inserts.push((toks[first].start, format!("bool {id}{IN_SUFFIX}, out bool")));
                assignments.push_str(&format!(" {id} = {id}{IN_SUFFIX};"));
            }
            if let Some(body) = d.body {
                inserts.push((toks[body].end, assignments));
            }
        }
    }
    // Calls: the name, not a member's (after `.` or `::`), then a parenthesised list that is no
    // declaration's.
    for k in 0..toks.len() {
        let Some(name) = word(k) else {
            continue;
        };
        let Some(places) = split.get(name) else {
            continue;
        };
        if punct(k + 1) != Some("(")
            || declared.contains(&(k + 1))
            || (k > 0 && matches!(punct(k - 1), Some("." | "::")))
        {
            continue;
        }
        let Some(close) = matching(source, &toks, k + 1) else {
            continue;
        };
        let arguments = parts(source, &toks, k + 1, close);
        for &place in places {
            let Some(&(first, end)) = arguments.get(place) else {
                continue;
            };
            let argument = &source[toks[first].start..toks[end - 1].end];
            inserts.push((toks[end - 1].end, format!(", {argument}")));
        }
    }
    edit(source, removed, inserts)
}

#[cfg(test)]
mod tests {
    use super::*;

    const ALIVE: &str = "struct S { float alive; uint n; };\n\
        void SetAlive(inout bool alive, bool value) { alive = value && alive; }\n";

    #[test]
    fn inout_bool_parameters_are_split_and_their_arguments_passed_twice() {
        let source = format!(
            "{ALIVE}void F(inout S s) {{ SetAlive(s.alive, true); SetAlive( s.alive , s.n != 0 ); }}\n"
        );
        assert!(splits(&source));
        assert_eq!(
            inout_bools(&source),
            "struct S { float alive; uint n; };\n\
             void SetAlive(bool alive_unbound_in, out bool alive, bool value) { alive = alive_unbound_in; alive = value && alive; }\n\
             void F(inout S s) { SetAlive(s.alive, s.alive, true); SetAlive( s.alive, s.alive , s.n != 0 ); }\n"
        );
        // A prototype and a definition with a semantic; calls after `return` and `else`, nested
        // in another call's arguments; two parameters given one l-value.
        let shapes = "bool Both(inout bool a, int k, inout bool b);\n\
                      bool Both(inout bool a, int k, inout bool b) : SV_Target { a = !a; b = a; return a; }\n\
                      float G(float x, float y) { if (x > 0) return Both(x, 1, y); else Both(x, 2, x); return max(Both(x, 3, y), y); }\n";
        assert_eq!(
            inout_bools(shapes),
            "bool Both(bool a_unbound_in, out bool a, int k, bool b_unbound_in, out bool b);\n\
             bool Both(bool a_unbound_in, out bool a, int k, bool b_unbound_in, out bool b) : SV_Target { a = a_unbound_in; b = b_unbound_in; a = !a; b = a; return a; }\n\
             float G(float x, float y) { if (x > 0) return Both(x, x, 1, y, y); else Both(x, x, 2, x, x); return max(Both(x, x, 3, y, y), y); }\n"
        );
    }

    #[test]
    fn parameters_a_call_cannot_be_matched_to_stay() {
        // Vectors of bool, plain bool parameters and `out bool`; a name declared without the
        // parameter elsewhere (an overload of another kind); a struct's members, defined in its
        // braces or as `S::M`, and their calls; a template type among the parameters.
        let kept = "void A(inout bool2 b) { } void B(bool b, out bool c) { c = b; }\n\
                    void C(inout bool b) { } void C(inout float f) { } void G() { float x = 0; C(x); }\n\
                    struct T { void M(inout bool b) { b = !b; } void N(inout bool b); };\n\
                    void T::N(inout bool b) { b = !b; }\n\
                    void H(T t) { bool b = true; t.M(b); t.N(b); }\n\
                    void V(vector<float, 3> v, inout bool b) { } void W() { bool b = true; V(0, b); }\n";
        assert!(!splits(kept));
        assert_eq!(inout_bools(kept), kept);
        assert!(!splits(
            "void A(inout bool2 b) { } void B(out bool c) { c = true; }"
        ));
    }

    /// glslang crashes on a number passed to an `inout bool` parameter (so the unrewritten
    /// program is not compiled here); the rewritten one compiles, for each kind of number.
    #[test]
    fn numbers_passed_to_split_bool_parameters_compile() {
        let compiler = shaderc::Compiler::new().unwrap();
        let mut o = shaderc::CompileOptions::new().unwrap();
        o.set_source_language(shaderc::SourceLanguage::HLSL);
        o.set_target_env(
            shaderc::TargetEnv::Vulkan,
            shaderc::EnvVersion::Vulkan1_1 as u32,
        );
        for number in ["float", "half", "int", "uint", "bool"] {
            let source = format!(
                "void SetAlive(inout bool alive, bool value) {{ alive = value && alive; }}\n\
                 float4 V() : SV_Position {{ {number} a = 1; SetAlive(a, true); return a; }}\n"
            );
            let fixed = inout_bools(&source);
            compiler
                .compile_into_spirv(&fixed, shaderc::ShaderKind::Vertex, "t", "V", Some(&o))
                .unwrap_or_else(|e| panic!("{number}: {e}\n{fixed}"));
        }
    }
}
